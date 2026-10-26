using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Hub;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Hub
{
	public sealed class HubSyncManager : MonoBehaviour
	{
		public static HubSyncManager Instance { get; private set; }
		public static bool IsApplyingSync { get; private set; } = false;
		private const float SendIntervalSeconds = 1f;

		public bool HasReceivedPostDiveSync { get; set; }

		public class HubStateContractResolver : DefaultContractResolver
		{
			protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
			{
				JsonProperty prop = base.CreateProperty(member, memberSerialization);

				if (member is PropertyInfo propertyInfo)
				{
					if (propertyInfo.GetIndexParameters().Length > 0)
					{
						prop.Ignored = true;
						return prop;
					}

					if (!prop.Writable && propertyInfo.GetSetMethod(true) != null)
					{
						prop.Writable = true;
					}

					if (propertyInfo.Name == "LastGainedExperience")
					{
						prop.Ignored = false;
						prop.Writable = true;
					}
				}

				return prop;
			}
		}

		public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
		{
			ContractResolver = new HubStateContractResolver(),
			Converters = { new StringEnumConverter() },
			NullValueHandling = NullValueHandling.Ignore,
			ObjectCreationHandling = ObjectCreationHandling.Replace,
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore
		};

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this.gameObject);
				return;
			}
			Instance = this;
			this.gameObject.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(this.gameObject);
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".HubSync");
			SceneManager.sceneLoaded += this.OnSceneLoaded;
		}

		private void Update()
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && !ReferenceEquals(net, this._subscribedNetworkManager))
			{
				if (this._subscribedNetworkManager != null)
				{
					this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
					this._subscribedNetworkManager.PeerConnected -= this.OnPeerConnected;
				}
				this._subscribedNetworkManager = net;
				this._subscribedNetworkManager.MessageReceived += this.OnMessageReceived;
				this._subscribedNetworkManager.PeerConnected += this.OnPeerConnected;
			}

			if (this._currentOrchestrator == null || net == null || !net.IsHost) return;
			if (SceneManager.GetActiveScene().name == "_Diving") return;

			this._sendTimer += Time.deltaTime;
			if (this._sendTimer >= SendIntervalSeconds)
			{
				this._sendTimer = 0f;
				this.SendHubStateIfChanged();
			}
		}

		public void ForceSendSync()
		{
			this._sendTimer = SendIntervalSeconds; 
		}

		private void OnPeerConnected(PeerInfo peer)
		{
			if (NetworkManager.Instance == null) return;

			if (NetworkManager.Instance.IsHost)
			{
				this.SendFullStateTo(peer.PeerId);
			}
			else
			{
				NetworkManager.Instance.SendToHost(new HubSyncRequestMessage().ToNetMessage());
			}
		}

		public void SendFullStateTo(int peerId)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs == null || gs.CampState == null || gs.ProgressionState == null) return;

			try
			{
				string campJson = JsonConvert.SerializeObject(gs.CampState, Formatting.None, JsonSettings);
				string progJson = JsonConvert.SerializeObject(gs.ProgressionState, Formatting.None, JsonSettings);
				string poolJson = gs.PoolState != null ? JsonConvert.SerializeObject(gs.PoolState, Formatting.None, JsonSettings) : string.Empty;

				NetworkManager.Instance?.SendToPeer(peerId, new HubStateSyncMessage
				{
					CampStateJson = campJson,
					ProgressionStateJson = progJson,
					PoolStateJson = poolJson
				}.ToNetMessage());
				this._log.LogInfo($"[Host] Sent synchronized Hub state to peer {peerId}.");
			}
			catch (Exception ex)
			{
				this._log.LogWarning($"Failed to serialize state for peer {peerId}: {ex.Message}");
			}
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == "_Hub")
			{
				this.HasReceivedPostDiveSync = false;
			}

			HubOrchestrator orchestrator = FindObjectOfType<HubOrchestrator>();
			if (orchestrator != null && this._currentOrchestrator != orchestrator)
			{
				this._currentOrchestrator = orchestrator;
				if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
				{
					NetworkManager.Instance.SendToHost(new HubSyncRequestMessage().ToNetMessage());
				}
			}
			else if (orchestrator == null && this._currentOrchestrator != null)
			{
				this._currentOrchestrator = null;
				this._lastCampJson = null;
				this._lastProgJson = null;
				this._lastPoolJson = null;
			}
		}

		private void SendHubStateIfChanged()
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs == null || gs.CampState == null || gs.ProgressionState == null) return;

			if (this._currentOrchestrator == null || !Singleton<SceneLoader>.HasInstance() || Singleton<SceneLoader>.Instance.LoadedScene != SceneLoader.eScene.Hub)
			{
				return;
			}

			try
			{
				string campJson = JsonConvert.SerializeObject(gs.CampState, Formatting.None, JsonSettings);
				string progJson = JsonConvert.SerializeObject(gs.ProgressionState, Formatting.None, JsonSettings);
				string poolJson = gs.PoolState != null ? JsonConvert.SerializeObject(gs.PoolState, Formatting.None, JsonSettings) : string.Empty;

				if (campJson != this._lastCampJson || progJson != this._lastProgJson || poolJson != this._lastPoolJson)
				{
					this._lastCampJson = campJson;
					this._lastProgJson = progJson;
					this._lastPoolJson = poolJson;
					NetworkManager.Instance?.Broadcast(new HubStateSyncMessage
					{
						CampStateJson = campJson,
						ProgressionStateJson = progJson,
						PoolStateJson = poolJson
					}.ToNetMessage());
				}
			}
			catch { }
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null) return;

			if (message.Type == MessageType.HubSyncRequest && net.IsHost)
			{
				this.SendFullStateTo(peerId);
			}
			else if (message.Type == MessageType.HubStateSync)
			{
				HubStateSyncMessage msg = HubStateSyncMessage.FromPayload(message.Payload);

				if (net.IsHost)
				{
					if (HubActionSyncManager.Instance != null && HubActionSyncManager.Instance.AllowClientCampControl)
					{
						this.ApplySyncedState(msg);
						Singleton<GameState>.Instance?.Save();
						this.RefreshHubUI();
						this.ForceSendSync();
					}
					else
					{
						this.SendFullStateTo(peerId);
					}
					return;
				}

				this.ApplySyncedState(msg);

				string activeScene = SceneManager.GetActiveScene().name;
				bool inGameScene = activeScene == "_Hub" || activeScene == "_Diving" ||
				                   (Singleton<SceneLoader>.HasInstance() && 
				                    (Singleton<SceneLoader>.Instance.LoadedScene == SceneLoader.eScene.Hub || 
				                     Singleton<SceneLoader>.Instance.LoadedScene == SceneLoader.eScene.Diving));

				if (!inGameScene)
				{
					this._log.LogInfo("[Client] Loading into Hub...");
					if (Singleton<SceneLoader>.HasInstance())
					{
						Singleton<SceneLoader>.Instance.LoadScene(SceneLoader.eScene.Hub, false, true, false);
					}
					return;
				}

				this.RefreshHubUI();
			}
		}

		public void RefreshHubUI()
		{
			if (this._currentOrchestrator != null)
			{
				AccessTools.Method(typeof(HubOrchestrator), "UpdateWidgetsAndMenu")?.Invoke(this._currentOrchestrator, null);

				HubEnvironmentManager env = FindObjectOfType<HubEnvironmentManager>();
				if (env != null && Singleton<GameState>.Instance?.ProgressionState != null)
				{
					env.SetEnvironment(Singleton<GameState>.Instance.ProgressionState.hubEnvironmentType);
				}

				this.RefreshOpenMenuUI();
			}

			foreach (WidgetMoraleAndNutrition widget in Resources.FindObjectsOfTypeAll<WidgetMoraleAndNutrition>())
			{
				if (widget != null && widget.gameObject.scene.isLoaded)
				{
					widget.UpdateState();
				}
			}
		}

		private void RefreshOpenMenuUI()
		{
			HubMenuManager menuManager = FindObjectOfType<HubMenuManager>();
			if (menuManager == null || menuManager.CurrentOpenMenu == null) return;

			Menu openMenu = menuManager.CurrentOpenMenu;

			if (openMenu is MenuInfirmary infirmary)
			{
				MenuInfirmaryTreatmentPanel treatmentPanel = AccessTools.Field(typeof(MenuInfirmary), "_treatmentPanel")?.GetValue(infirmary) as MenuInfirmaryTreatmentPanel;
				if (treatmentPanel != null && treatmentPanel.gameObject.activeSelf)
				{
					SurvivorData currentSurvivor = AccessTools.Field(typeof(MenuInfirmaryTreatmentPanel), "_survivorData")?.GetValue(treatmentPanel) as SurvivorData;
					if (currentSurvivor != null)
					{
						SurvivorData freshSurvivor = Singleton<GameState>.Instance?.CampState?.GetSurvivor(currentSurvivor.Id);
						if (freshSurvivor != null)
						{
							treatmentPanel.SetData(freshSurvivor);
						}
					}
				}
				infirmary.UpdateBedCards();
			}
			else if (openMenu is MenuLighthouse lighthouse)
			{
				lighthouse.UpdateVisitingSurvivorList();
				lighthouse.UpdateCampStatus();
				lighthouse.UpdateExpeditionCards();
			}
			else if (openMenu is MenuFishingHut fishingHut)
			{
				fishingHut.UpdateUI();
			}
			else if (openMenu is MenuAltar altar)
			{
				altar.UpdateTributesDisplay();
				altar.UpdateSelectedIdolDisplay();
			}
			else if (openMenu is MenuMemorial memorial)
			{
				memorial.UpdateDeadList();
				memorial.UpdateBurningDetails();
			}
			else if (openMenu is MenuWorkshop workshop)
			{
				Singleton<GameState>.Instance?.CampState?.UpdateAvailableCraftingRecipes();

				var usablesCrafting = AccessTools.Field(typeof(MenuWorkshop), "_usablesCrafting")?.GetValue(workshop) as MenuWorkshopCraftingInventory;
				var upgradesCrafting = AccessTools.Field(typeof(MenuWorkshop), "_upgradesCrafting")?.GetValue(workshop) as MenuWorkshopCraftingInventory;
				var selectedPanel = AccessTools.Field(typeof(MenuWorkshop), "_selectedPanel")?.GetValue(workshop) as MenuWorkshopSelectedPanel;

				usablesCrafting?.UpdateCraftablesList();
				upgradesCrafting?.UpdateCraftablesList();

				if (selectedPanel != null && selectedPanel.gameObject.activeSelf)
				{
					bool isPopupOpen = (bool)(AccessTools.Field(typeof(MenuWorkshopSelectedPanel), "_craftedItemDisplayed")?.GetValue(selectedPanel) ?? false);
					if (!isPopupOpen)
					{
						var craftableData = AccessTools.Field(typeof(MenuWorkshopSelectedPanel), "_craftableData")?.GetValue(selectedPanel) as CraftableItemData;
						if (craftableData != null)
						{
							selectedPanel.Display(craftableData);
						}
					}
				}
			}
		}

		private void ApplySyncedState(HubStateSyncMessage msg)
		{
			IsApplyingSync = true;
			try
			{
				CampState newCamp = JsonConvert.DeserializeObject<CampState>(msg.CampStateJson, JsonSettings);
				ProgressionState newProg = !string.IsNullOrEmpty(msg.ProgressionStateJson)
					? JsonConvert.DeserializeObject<ProgressionState>(msg.ProgressionStateJson, JsonSettings)
					: null;
				PoolState newPool = !string.IsNullOrEmpty(msg.PoolStateJson) 
					? JsonConvert.DeserializeObject<PoolState>(msg.PoolStateJson, JsonSettings) 
					: null;

				GameState gs = Singleton<GameState>.Instance;
				if (gs == null) return;

				DivingState localDivingState = gs.ProgressionState?.divingState;

				if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
				{
					AccessTools.Field(typeof(GameState), "_currentSaveSlot")?.SetValue(gs, -1);

					if (newProg != null && localDivingState != null)
					{
						newProg.divingState = localDivingState;
					}
				}

				if (newCamp != null) AccessTools.Field(typeof(GameState), "_campState")?.SetValue(gs, newCamp);
				
				if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
				{
					if (newProg != null) AccessTools.Field(typeof(GameState), "_progressionState")?.SetValue(gs, newProg);
				}
				else
				{
					if (localDivingState != null && gs.ProgressionState != null)
					{
						gs.ProgressionState.divingState = localDivingState;
					}
				}

				if (newPool != null) AccessTools.Field(typeof(GameState), "_poolState")?.SetValue(gs, newPool);

				this.HasReceivedPostDiveSync = true;
			}
			catch (Exception e)
			{
				this._log.LogError("Error deserializing Hub State: " + e);
			}
			finally
			{
				IsApplyingSync = false;
			}
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= this.OnSceneLoaded;
			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
				this._subscribedNetworkManager.PeerConnected -= this.OnPeerConnected;
			}
			if (Instance == this) Instance = null;
		}

		private ManualLogSource _log;
		private NetworkManager _subscribedNetworkManager;
		private HubOrchestrator _currentOrchestrator;
		private float _sendTimer;
		private string _lastCampJson;
		private string _lastProgJson;
		private string _lastPoolJson;
	}

	[HarmonyPatch(typeof(GameState), nameof(GameState.Save))]
	internal static class GameState_Save_Patch
	{
		private static bool Prefix()
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost)
			{
				return false;
			}
			return true;
		}
	}
}

