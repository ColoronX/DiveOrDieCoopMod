using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using ED.Hub;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Diving
{
	public sealed class DiveTransitionManager : MonoBehaviour
	{
		public static DiveTransitionManager Instance { get; private set; }

		public DiveStatus LocalStatus { get; private set; } = DiveStatus.InHub;
		public bool IsPerformingFinalOutro { get; private set; }
		public bool IsLocalReady { get; private set; }

		public readonly Dictionary<int, bool> PeerReadyStates =
			new Dictionary<int, bool>();

		public readonly Dictionary<int, string> PeerSurvivorClaims =
			new Dictionary<int, string>();

		public readonly Dictionary<int, string> PeerSurfaceSpotSelections =
			new Dictionary<int, string>();

		private readonly Dictionary<int, DiveStatus> _peerStatuses =
			new Dictionary<int, DiveStatus>();

		public int ActiveDiveSeed { get; private set; }
		public string ActiveSurfaceSpotId { get; private set; } = "Default";

		public string ClaimedSurvivorId { get; set; } = string.Empty;

		public SurvivorData LocalClaimedSurvivor { get; set; }

		private NetworkManager _subscribedNetworkManager;
		private ManualLogSource _log;
		private string _spectatorBannerText = string.Empty;
		private SurfaceTrigger _pendingSurfaceTrigger;
		private int _spectatorPeerId = -1;

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

			this._log =
				BepInEx.Logging.Logger.CreateLogSource(
					PluginInfo.NAME + ".Transition");

			SceneManager.sceneLoaded += this.OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == "_Diving")
			{
				this.LocalStatus = DiveStatus.Active;
				this.IsPerformingFinalOutro = false;
				this._pendingSurfaceTrigger = null;
				this._spectatorPeerId = -1;

				this.BroadcastStatus(DiveStatus.Active);
			}
			else if (scene.name == "_Hub")
			{
				this.LocalStatus = DiveStatus.InHub;
				this.IsLocalReady = false;
				this.IsPerformingFinalOutro = false;
				this.ClaimedSurvivorId = string.Empty;

				this.PeerReadyStates.Clear();
				this.PeerSurvivorClaims.Clear();
				this.PeerSurfaceSpotSelections.Clear();

				this._peerStatuses.Clear();
				this._spectatorPeerId = -1;

				if (Singleton<GameFlowManager>.HasInstance())
					Singleton<GameFlowManager>.Instance.ReleaseAllLockLevels();

				Time.timeScale = 1f;

				this.BroadcastStatus(DiveStatus.InHub);
			}
		}

		private void Update()
		{
			if (NetworkManager.Instance != null &&
				!ReferenceEquals(
					NetworkManager.Instance,
					this._subscribedNetworkManager))
			{
				if (this._subscribedNetworkManager != null)
				{
					this._subscribedNetworkManager.MessageReceived -=
						this.OnMessageReceived;

					this._subscribedNetworkManager.PeerDisconnected -=
						this.OnPeerDisconnected;

					this._subscribedNetworkManager.PeerConnected -=
						this.OnPeerConnected;
				}

				this._subscribedNetworkManager =
					NetworkManager.Instance;

				this._subscribedNetworkManager.MessageReceived +=
					this.OnMessageReceived;

				this._subscribedNetworkManager.PeerDisconnected +=
					this.OnPeerDisconnected;

				this._subscribedNetworkManager.PeerConnected +=
					this.OnPeerConnected;
			}

			if (this.LocalStatus != DiveStatus.Active &&
				this.LocalStatus != DiveStatus.InHub &&
				!this.IsPerformingFinalOutro &&
				SceneManager.GetActiveScene().name == "_Diving")
			{
				this.UpdateSpectatorTarget();

				if (Keyboard.current != null)
				{
					if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
						this.SelectNextSpectator(1);
					else if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
						this.SelectNextSpectator(-1);
				}
			}
		}

		/*
		 * CLIENT STAGING ONLY.
		 *
		 * This function does NOT initialize DivingState and does NOT load
		 * the Diving scene. The native Hub UI remains active.
		 */
		public void ToggleClientReady(string survivorId)
		{
			NetworkManager net = this._subscribedNetworkManager;

			if (net == null ||
				!net.IsConnected ||
				net.IsHost)
			{
				return;
			}

			int peerId = net.LocalPeerId;

			if (peerId < 0)
				peerId = 1;

			bool newReady = !this.IsLocalReady;

			if (!newReady)
			{
				this.IsLocalReady = false;
				this.ClaimedSurvivorId = string.Empty;

				this.PeerReadyStates[peerId] = false;
				this.PeerSurvivorClaims.Remove(peerId);
				this.PeerSurfaceSpotSelections.Remove(peerId);
			}
			else
			{
				this.IsLocalReady = true;
				this.ClaimedSurvivorId = survivorId ?? string.Empty;

				string skinId =
					this.LocalClaimedSurvivor?.GraphicsData?.SkinId ??
					string.Empty;

				string surfaceSpotId =
					EnsureValidSurfaceSpotId(
						Singleton<GameState>.Instance?.ProgressionState?.selectedSurfaceSpotId);

				string claimPayload =
					$"{this.ClaimedSurvivorId}|{skinId}";

				this.PeerReadyStates[peerId] = true;
				this.PeerSurvivorClaims[peerId] = claimPayload;
				this.PeerSurfaceSpotSelections[peerId] = surfaceSpotId;
			}

			string selectedSurface =
				this.PeerSurfaceSpotSelections.TryGetValue(
					peerId,
					out string localSurface)
					? localSurface
					: string.Empty;

			string selectedClaim =
				this.PeerSurvivorClaims.TryGetValue(
					peerId,
					out string localClaim)
					? localClaim
					: string.Empty;

			net.SendToHost(
				new DiveReadyStatusMessage
				{
					PeerId = peerId,
					IsReady = this.IsLocalReady,
					SelectedSurvivorId = selectedClaim,
					SelectedSurfaceSpotId = selectedSurface
				}.ToNetMessage());

			this.RefreshDeck();
		}

		private void RefreshDeck()
		{
			MenuDivingDeck deck =
				FindObjectOfType<MenuDivingDeck>();

			if (deck != null && deck.IsDisplayed)
				deck.UpdateSurvivorList();
		}

		public bool AreAllPeersReady()
		{
			if (this._subscribedNetworkManager == null ||
				!this._subscribedNetworkManager.IsConnected)
			{
				return true;
			}

			return this._subscribedNetworkManager.ConnectedPeers.All(
				p => this.PeerReadyStates.TryGetValue(
					p.Key,
					out bool ready) && ready);
		}

		public bool IsSurvivorClaimedByTeammate(string survivorId)
		{
			if (string.IsNullOrEmpty(survivorId) ||
				this._subscribedNetworkManager == null ||
				!this._subscribedNetworkManager.IsConnected)
			{
				return false;
			}

			int myPeerId =
				this._subscribedNetworkManager.LocalPeerId;

			foreach (var kvp in this.PeerSurvivorClaims)
			{
				if (kvp.Key == myPeerId)
					continue;

				if (GetSurvivorId(kvp.Value) == survivorId)
					return true;
			}

			return false;
		}

		private static string GetSurvivorId(string claim)
		{
			if (string.IsNullOrEmpty(claim))
				return string.Empty;

			int separator = claim.IndexOf('|');

			return separator >= 0
				? claim.Substring(0, separator)
				: claim;
		}

		public void HostLaunchSynchronizedDive()
		{
			if (this._subscribedNetworkManager == null ||
				!this._subscribedNetworkManager.IsHost)
			{
				return;
			}

			if (!this.AreAllPeersReady())
			{
				this._log.LogWarning(
					"[Host] Attempted to start expedition before all peers were ready.");

				return;
			}

			/*
			 * Final authority:
			 * the host's current Hub surface selection determines the
			 * single world all players will enter.
			 */
			this.ActiveSurfaceSpotId =
				EnsureValidSurfaceSpotId(
					Singleton<GameState>.Instance?.ProgressionState?.selectedSurfaceSpotId);

			HashSet<string> claimedSurvivors =
				new HashSet<string>();

			foreach (string claim in this.PeerSurvivorClaims.Values)
			{
				string survivorId = GetSurvivorId(claim);

				if (string.IsNullOrEmpty(survivorId))
					continue;

				if (!claimedSurvivors.Add(survivorId))
				{
					this._log.LogError(
						$"[Host] Cannot start expedition. Survivor '{survivorId}' is claimed by multiple players.");

					return;
				}
			}

			this.ActiveDiveSeed = Environment.TickCount;

			UnityEngine.Random.InitState(
				this.ActiveDiveSeed);

			this.EnsureDivingStateInitialized();

			string hostSurvivorId =
				this.LocalClaimedSurvivor?.Id ??
				this.ClaimedSurvivorId ??
				string.Empty;

			string hostSkinId =
				this.LocalClaimedSurvivor?.GraphicsData?.SkinId ??
				string.Empty;

			this.PeerSurvivorClaims[PeerInfo.HostPeerId] =
				$"{hostSurvivorId}|{hostSkinId}";

			this._subscribedNetworkManager.Broadcast(
				new DiveSeedMessage
				{
					Seed = this.ActiveDiveSeed,
					SurfaceSpotId = this.ActiveSurfaceSpotId,
					PeerSurvivorClaims = new Dictionary<int, string>(
						this.PeerSurvivorClaims)
				}.ToNetMessage());

			this.ExecuteDiveSceneLoad();
		}

		private void ExecuteDiveSceneLoad()
		{
			this.EnsureDivingStateInitialized();

			if (this.ActiveDiveSeed != 0)
				UnityEngine.Random.InitState(this.ActiveDiveSeed);

			GameState gs =
				Singleton<GameState>.Instance;

			if (gs?.ProgressionState != null)
			{
				gs.ProgressionState.selectedSurfaceSpotId =
					EnsureValidSurfaceSpotId(
						this.ActiveSurfaceSpotId);
			}

			this.LocalStatus = DiveStatus.Active;

			this.BroadcastStatus(DiveStatus.Active);

			if (Singleton<SceneLoader>.HasInstance())
			{
				Singleton<SceneLoader>.Instance.LoadScene(
					SceneLoader.eScene.Diving,
					true,
					false,
					false);
			}
		}

		public static string EnsureValidSurfaceSpotId(
			string spotId = null)
		{
			GameState gs =
				Singleton<GameState>.Instance;

			string valid =
				!string.IsNullOrEmpty(spotId)
					? spotId
					: gs?.ProgressionState?.selectedSurfaceSpotId;

			if (string.IsNullOrEmpty(valid))
				valid = "Default";

			if (gs?.ProgressionState != null)
				gs.ProgressionState.selectedSurfaceSpotId =
					valid;

			return valid;
		}

		public void EnsureDivingStateInitialized()
		{
			GameState gs =
				Singleton<GameState>.Instance;

			if (gs == null)
				return;

			if (gs.ProgressionState == null)
			{
				AccessTools.Field(
					typeof(GameState),
					"_progressionState")?.SetValue(
						gs,
						new ProgressionState());
			}

			CampState camp = gs.CampState;

			if (camp == null)
				return;

			SurvivorData diver =
				this.LocalClaimedSurvivor;

			if (diver == null)
			{
				string mySurvivorId =
					this.ClaimedSurvivorId;

				if (string.IsNullOrEmpty(mySurvivorId))
				{
					int myPeerId =
						this._subscribedNetworkManager != null
							? this._subscribedNetworkManager.LocalPeerId
							: PeerInfo.HostPeerId;

					if (myPeerId < 0)
						myPeerId = PeerInfo.HostPeerId;

					if (this.PeerSurvivorClaims.TryGetValue(
						myPeerId,
						out string claim))
					{
						mySurvivorId =
							GetSurvivorId(claim);
					}
				}

				if (!string.IsNullOrEmpty(mySurvivorId))
					diver = camp.GetSurvivor(mySurvivorId);
			}

			if (diver == null &&
				camp.Survivors != null &&
				camp.Survivors.Count > 0)
			{
				diver =
					camp.Survivors.FirstOrDefault(
						s => s != null && s.CanDive())
					?? camp.Survivors[0];
			}

			if (diver == null)
				return;

			this.LocalClaimedSurvivor = diver;
			this.ClaimedSurvivorId = diver.Id;

			int peerId =
				this._subscribedNetworkManager != null
					? this._subscribedNetworkManager.LocalPeerId
					: PeerInfo.HostPeerId;

			if (peerId < 0)
				peerId = PeerInfo.HostPeerId;

			this.PeerSurvivorClaims[peerId] =
				$"{diver.Id}|{diver.GraphicsData?.SkinId ?? string.Empty}";

			if (diver.Suit == null ||
				diver.OxygenTank == null)
			{
				diver.UpdateDefaultEquipment();
			}

			Dictionary<string, Item> initialInventory =
				new Dictionary<string, Item>();

			if (diver.Usables != null)
			{
				foreach (Item item in diver.Usables)
				{
					if (item?.Data != null &&
						!initialInventory.ContainsKey(item.Data.Id))
					{
						initialInventory[item.Data.Id] =
							item.Clone();
					}
				}
			}

			gs.ProgressionState.divingState =
				new DivingState(
					diver,
					initialInventory,
					new List<Item>());
		}

		public void OnLocalPlayerDied()
		{
			if (this._subscribedNetworkManager == null ||
				!this._subscribedNetworkManager.IsConnected ||
				this._subscribedNetworkManager.ConnectedPeers.Count == 0)
			{
				return;
			}

			if (this.LocalStatus != DiveStatus.Dead)
			{
				this.LocalStatus = DiveStatus.Dead;
				this.BroadcastStatus(DiveStatus.Dead);
				this.ScatterDeadDiverLootImmediately();

				string deadSurvivorId =
					this.ClaimedSurvivorId;

				if (this._subscribedNetworkManager.IsHost)
				{
					SurvivorData victim =
						Singleton<GameState>.Instance?
							.CampState?
							.GetSurvivor(deadSurvivorId);

					if (victim != null)
					{
						Singleton<GameState>.Instance
							.CampState
							.KillSurvivor(
								victim,
								"Abyssal Death");
					}

					this.CheckIfAllDivingPeersFinished();
				}
				else
				{
					this._subscribedNetworkManager.SendToHost(
						new DiverDiedMessage
						{
							PeerId =
								this._subscribedNetworkManager.LocalPeerId,
							SurvivorId = deadSurvivorId,
							DeathSource = "Abyssal Death"
						}.ToNetMessage());
				}

				this.EnterSpectatorMode(
					"DIED - SPECTATING TEAMMATE");

				DiveSyncManager.Instance?
					.SendImmediateLocalSnapshot();
			}
		}

		public void EnterSpectatorMode(
			string bannerText)
		{
			this._spectatorBannerText = bannerText;
			Time.timeScale = 1f;

			PlayerController player =
				Singleton<PlayerController>.Instance;

			if (player != null)
			{
				player.SetIntangible(true);
				player.SetInvulnerable(true);
				player.SetStoppedMovement(true);
				player.PauseOxygenDepletion(true);
				player.enabled = false;

				if (this.LocalStatus != DiveStatus.Dead)
				{
					Collider2D col =
						player.GetComponent<Collider2D>();

					if (col != null)
						col.enabled = false;

					if (player.RigidBody != null)
					{
						player.RigidBody.velocity =
							Vector2.zero;

						player.RigidBody.isKinematic = true;
					}
				}
			}

			DeathPanel deathPanel =
				FindObjectOfType<DeathPanel>();

			if (deathPanel != null)
				deathPanel.gameObject.SetActive(false);

			this._spectatorPeerId = -1;
			this.SelectNextSpectator(1);
		}

		private void UpdateSpectatorTarget()
		{
			List<RemoteDiverController> alive =
				DiveSyncManager.Instance?.GetAliveRemoteDivers();

			if (alive == null || alive.Count == 0)
				return;

			RemoteDiverController target =
				alive.FirstOrDefault(
					x => x.PeerId == this._spectatorPeerId);

			if (target == null)
			{
				this._spectatorPeerId =
					alive[0].PeerId;

				target = alive[0];
			}

			if (Singleton<MainVirtualCameraManager>.HasInstance())
			{
				Singleton<MainVirtualCameraManager>.Instance
					.OverrideTargetTransform(
						target.transform);
			}
		}

		private void SelectNextSpectator(int direction)
		{
			List<RemoteDiverController> alive =
				DiveSyncManager.Instance?.GetAliveRemoteDivers();

			if (alive == null || alive.Count == 0)
				return;

			int currentIndex =
				alive.FindIndex(
					x => x.PeerId == this._spectatorPeerId);

			if (currentIndex < 0)
				currentIndex =
					direction > 0
						? 0
						: alive.Count - 1;
			else
				currentIndex =
					(currentIndex + direction + alive.Count)
					% alive.Count;

			this._spectatorPeerId =
				alive[currentIndex].PeerId;

			if (Singleton<MainVirtualCameraManager>.HasInstance())
			{
				Singleton<MainVirtualCameraManager>.Instance
					.OverrideTargetTransform(
						alive[currentIndex].transform);
			}
		}

		private void ScatterDeadDiverLootImmediately()
		{
			try
			{
				PlayerController player =
					Singleton<PlayerController>.Instance;

				CollectibleManager manager =
					Singleton<DivingOrchestrator>.Instance?
						.CollectibleManager;

				if (player == null || manager == null)
					return;

				Vector3 deathPos =
					player.transform.position;

				DivingInventoryUI inventoryUI =
					FindObjectOfType<DivingInventoryUI>();

				List<Item> items =
					inventoryUI != null
						? inventoryUI.LoseInventory(false)
						: null;

				if (items != null)
				{
					foreach (Item item in items)
					{
						if (item?.Data == null)
							continue;

						int count =
							Mathf.Max(1, item.Count);

						for (int i = 0; i < count; i++)
						{
							Vector3 dropPos =
								deathPos +
								(Vector3)(
									UnityEngine.Random.insideUnitCircle *
									0.75f);

							manager.SpawnCollectible(
								item.Data.Id,
								dropPos,
								true,
								null,
								false);
						}
					}
				}
			}
			catch (Exception ex)
			{
				this._log.LogWarning(
					$"Failed to scatter loot: {ex.Message}");
			}
		}

		public bool OnSurfacingTriggered(
			SurfaceTrigger trigger)
		{
			if (Time.timeSinceLevelLoad < 6f)
				return false;

			bool isMultiplayer =
				this._subscribedNetworkManager != null &&
				this._subscribedNetworkManager.IsConnected &&
				this._subscribedNetworkManager.ConnectedPeers.Count > 0;

			if (!isMultiplayer ||
				this.IsPerformingFinalOutro)
			{
				return true;
			}

			if (this.LocalStatus != DiveStatus.Surfaced)
			{
				this.LocalStatus = DiveStatus.Surfaced;
				this._pendingSurfaceTrigger = trigger;

				this.BroadcastStatus(
					DiveStatus.Surfaced);

				this.EnterSpectatorMode(
					"SURFACED - SPECTATING TEAMMATE");
			}

			if (this._subscribedNetworkManager.IsHost)
				this.CheckIfAllDivingPeersFinished();

			return false;
		}

		private void BroadcastStatus(
			DiveStatus status)
		{
			if (this._subscribedNetworkManager == null)
				return;

			int myPeerId =
				this._subscribedNetworkManager.LocalPeerId;

			NetMessage message =
				new PlayerDiveStatusMessage
				{
					PeerId = myPeerId,
					Status = status
				}.ToNetMessage();

			if (this._subscribedNetworkManager.IsHost)
				this._subscribedNetworkManager.Broadcast(message);
			else
				this._subscribedNetworkManager.SendToHost(message);
		}

		private void CheckIfAllDivingPeersFinished()
		{
			if (this.LocalStatus == DiveStatus.Active)
				return;

			if (this._subscribedNetworkManager != null &&
				this._subscribedNetworkManager.ConnectedPeers.Count > 0)
			{
				foreach (var kvp in
					this._subscribedNetworkManager.ConnectedPeers)
				{
					if (!this._peerStatuses.TryGetValue(
						kvp.Key,
						out DiveStatus status) ||
						status == DiveStatus.Active)
					{
						return;
					}
				}
			}

			this.IsPerformingFinalOutro = true;

			this._subscribedNetworkManager?.Broadcast(
				new FinalDiveEndedMessage().ToNetMessage());

			this.ExecuteNativeExit();
		}

		private void ExecuteNativeExit()
		{
			this.IsPerformingFinalOutro = true;
			this._spectatorBannerText = string.Empty;
			this._spectatorPeerId = -1;

			this.PeerReadyStates.Clear();
			this.PeerSurvivorClaims.Clear();
			this.PeerSurfaceSpotSelections.Clear();

			if (Singleton<MainVirtualCameraManager>.HasInstance())
			{
				Singleton<MainVirtualCameraManager>.Instance
					.ResetTargetTransform();
			}

			if (this._pendingSurfaceTrigger != null)
			{
				SurfaceTrigger trigger =
					this._pendingSurfaceTrigger;

				this._pendingSurfaceTrigger = null;

				try
				{
					MethodInfo triggerInternal =
						AccessTools.Method(
							typeof(SurfaceTrigger),
							"TriggerInternal");

					if (triggerInternal != null &&
						trigger != null)
					{
						trigger.enabled = true;

						PlayerController player =
							Singleton<PlayerController>.Instance;

						if (player != null)
						{
							player.enabled = true;

							Collider2D col =
								player.GetComponent<Collider2D>();

							if (col != null)
								col.enabled = true;
						}

						ParameterInfo[] parameters =
							triggerInternal.GetParameters();

						object[] args =
							parameters.Length == 0
								? null
								: new object[parameters.Length];

						for (int i = 0; i < parameters.Length; i++)
						{
							args[i] =
								parameters[i].ParameterType.IsValueType
									? Activator.CreateInstance(
										parameters[i].ParameterType)
									: null;
						}

						triggerInternal.Invoke(
							trigger,
							args);

						return;
					}
				}
				catch (Exception ex)
				{
					this._log.LogWarning(
						$"Native surface trigger re-invoke failed: {ex.Message}");
				}
			}

			if (Singleton<SceneLoader>.HasInstance())
			{
				Singleton<SceneLoader>.Instance.LoadScene(
					SceneLoader.eScene.Hub,
					true,
					true,
					false);
			}
		}

		private void OnMessageReceived(
			int peerId,
			NetMessage message)
		{
			if (this._subscribedNetworkManager == null ||
				message == null)
			{
				return;
			}

			if (message.Type == MessageType.DiveReadyStatus)
			{
				DiveReadyStatusMessage msg =
					DiveReadyStatusMessage.FromPayload(
						message.Payload);

				if (this._subscribedNetworkManager.IsHost)
				{
					int targetId = peerId;

					if (!msg.IsReady)
					{
						this.PeerReadyStates[targetId] = false;
						this.PeerSurvivorClaims.Remove(targetId);
						this.PeerSurfaceSpotSelections.Remove(targetId);

						this._subscribedNetworkManager.Broadcast(
							new DiveReadyStatusMessage
							{
								PeerId = targetId,
								IsReady = false,
								SelectedSurvivorId = string.Empty,
								SelectedSurfaceSpotId = string.Empty
							}.ToNetMessage());

						this.RefreshDeck();
						return;
					}

					string requestedClaim =
						msg.SelectedSurvivorId ?? string.Empty;

					string requestedSurvivorId =
						GetSurvivorId(requestedClaim);

					bool duplicate =
						string.IsNullOrEmpty(
							requestedSurvivorId) ||
						this.PeerSurvivorClaims.Any(
							kv =>
								kv.Key != targetId &&
								GetSurvivorId(kv.Value) ==
								requestedSurvivorId);

					if (duplicate)
					{
						this.PeerReadyStates[targetId] = false;
						this.PeerSurvivorClaims.Remove(targetId);
						this.PeerSurfaceSpotSelections.Remove(targetId);

						this._subscribedNetworkManager.SendToPeer(
							targetId,
							new DiveReadyStatusMessage
							{
								PeerId = targetId,
								IsReady = false,
								SelectedSurvivorId = string.Empty,
								SelectedSurfaceSpotId = string.Empty
							}.ToNetMessage());

						this.RefreshDeck();
						return;
					}

					string surfaceSpot =
						EnsureValidSurfaceSpotId(
							msg.SelectedSurfaceSpotId);

					this.PeerReadyStates[targetId] = true;
					this.PeerSurvivorClaims[targetId] =
						requestedClaim;

					this.PeerSurfaceSpotSelections[targetId] =
						surfaceSpot;

					this._subscribedNetworkManager.Broadcast(
						new DiveReadyStatusMessage
						{
							PeerId = targetId,
							IsReady = true,
							SelectedSurvivorId = requestedClaim,
							SelectedSurfaceSpotId = surfaceSpot
						}.ToNetMessage());

					this.RefreshDeck();
					return;
				}

				int canonicalPeerId = msg.PeerId;

				if (canonicalPeerId < 0)
					canonicalPeerId = peerId;

				if (msg.IsReady &&
					!string.IsNullOrEmpty(
						msg.SelectedSurvivorId))
				{
					this.PeerReadyStates[canonicalPeerId] = true;

					this.PeerSurvivorClaims[canonicalPeerId] =
						msg.SelectedSurvivorId;

					this.PeerSurfaceSpotSelections[
						canonicalPeerId] =
						msg.SelectedSurfaceSpotId ?? string.Empty;

					if (canonicalPeerId ==
						this._subscribedNetworkManager.LocalPeerId)
					{
						this.IsLocalReady = true;
						this.ClaimedSurvivorId =
							GetSurvivorId(
								msg.SelectedSurvivorId);
					}
				}
				else
				{
					this.PeerReadyStates[canonicalPeerId] =
						false;

					this.PeerSurvivorClaims.Remove(
						canonicalPeerId);

					this.PeerSurfaceSpotSelections.Remove(
						canonicalPeerId);

					if (canonicalPeerId ==
						this._subscribedNetworkManager.LocalPeerId)
					{
						this.IsLocalReady = false;
						this.ClaimedSurvivorId =
							string.Empty;
					}
				}

				this.RefreshDeck();
				return;
			}

			if (message.Type == MessageType.DiveSeed &&
				!this._subscribedNetworkManager.IsHost)
			{
				DiveSeedMessage msg =
					DiveSeedMessage.FromPayload(
						message.Payload);

				this.ActiveDiveSeed = msg.Seed;

				this.ActiveSurfaceSpotId =
					EnsureValidSurfaceSpotId(
						msg.SurfaceSpotId);

				this.PeerSurvivorClaims.Clear();

				if (msg.PeerSurvivorClaims != null)
				{
					foreach (var kvp in msg.PeerSurvivorClaims)
					{
						this.PeerSurvivorClaims[kvp.Key] =
							kvp.Value;
					}
				}

				/*
				 * THIS is the first time the client is allowed to
				 * initialize its DivingState and load the dive.
				 */
				if (this._subscribedNetworkManager.LocalPeerId >= 0 &&
					this.PeerSurvivorClaims.TryGetValue(
						this._subscribedNetworkManager.LocalPeerId,
						out string ownClaim))
				{
					string ownSurvivorId =
						GetSurvivorId(ownClaim);

					this.ClaimedSurvivorId =
						ownSurvivorId;

					SurvivorData ownSurvivor =
						Singleton<GameState>.Instance?
							.CampState?
							.GetSurvivor(
								ownSurvivorId);

					if (ownSurvivor != null)
						this.LocalClaimedSurvivor =
							ownSurvivor;
				}

				this.ExecuteDiveSceneLoad();
				return;
			}

			if (message.Type == MessageType.DiverDied &&
				this._subscribedNetworkManager.IsHost)
			{
				DiverDiedMessage death =
					DiverDiedMessage.FromPayload(
						message.Payload);

				SurvivorData victim =
					Singleton<GameState>.Instance?
						.CampState?
						.GetSurvivor(
							death.SurvivorId);

				if (victim != null)
				{
					Singleton<GameState>.Instance
						.CampState
						.KillSurvivor(
							victim,
							death.DeathSource);

					Singleton<GameState>.Instance.Save();

					HubSyncManager.Instance?
						.ForceSendSync();
				}

				return;
			}

			if (message.Type == MessageType.PlayerDiveStatus)
			{
				PlayerDiveStatusMessage msg =
					PlayerDiveStatusMessage.FromPayload(
						message.Payload);

				this._peerStatuses[peerId] =
					msg.Status;

				if (this._subscribedNetworkManager.IsHost)
				{
					this._subscribedNetworkManager.Broadcast(
						message,
						peerId);

					if (SceneManager.GetActiveScene().name ==
						"_Diving")
					{
						this.CheckIfAllDivingPeersFinished();
					}
				}

				return;
			}

			if (message.Type == MessageType.FinalDiveEnded &&
				!this._subscribedNetworkManager.IsHost)
			{
				this.ExecuteNativeExit();
			}
		}

		private void OnPeerConnected(PeerInfo peer)
		{
			if (this._subscribedNetworkManager == null ||
				this._subscribedNetworkManager.IsHost)
			{
				return;
			}

			if (peer.PeerId != PeerInfo.HostPeerId)
				return;

			if (this.LocalStatus == DiveStatus.InHub)
				return;

			this.ExecuteNativeExit();
		}

		private void OnPeerDisconnected(
			int peerId,
			string reason)
		{
			this._peerStatuses.Remove(peerId);
			this.PeerReadyStates.Remove(peerId);
			this.PeerSurvivorClaims.Remove(peerId);
			this.PeerSurfaceSpotSelections.Remove(peerId);

			if (this._spectatorPeerId == peerId)
				this._spectatorPeerId = -1;

			if (this._subscribedNetworkManager != null &&
				this._subscribedNetworkManager.IsHost &&
				SceneManager.GetActiveScene().name ==
				"_Diving")
			{
				this.CheckIfAllDivingPeersFinished();
			}
		}

		private void OnGUI()
		{
			if (this.LocalStatus != DiveStatus.Active &&
				this.LocalStatus != DiveStatus.InHub &&
				!this.IsPerformingFinalOutro &&
				!string.IsNullOrEmpty(
					this._spectatorBannerText))
			{
				GUIStyle style =
					new GUIStyle(GUI.skin.box)
					{
						fontSize = 20,
						alignment =
							TextAnchor.MiddleCenter,
						normal =
						{
							textColor =
								Color.white
						}
					};

				GUI.Box(
					new Rect(
						(Screen.width / 2) - 250,
						40,
						500,
						50),
					this._spectatorBannerText,
					style);
			}
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -=
				this.OnSceneLoaded;

			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -=
					this.OnMessageReceived;

				this._subscribedNetworkManager.PeerDisconnected -=
					this.OnPeerDisconnected;

				this._subscribedNetworkManager.PeerConnected -=
					this.OnPeerConnected;
			}

			if (Instance == this)
				Instance = null;
		}
	}

	[HarmonyPatch(typeof(SurfaceTrigger), "TriggerInternal")]
	internal static class SurfaceTrigger_TriggerInternal_Patch
	{
		private static bool Prefix(
			SurfaceTrigger __instance)
		{
			__instance.enabled = false;

			return DiveTransitionManager.Instance?
				.OnSurfacingTriggered(__instance)
				?? true;
		}
	}

	[HarmonyPatch(
		typeof(PlayerDamageProcessor),
		nameof(PlayerDamageProcessor.Die),
		new Type[]
		{
			typeof(IDamageSource),
			typeof(bool)
		})]
	internal static class PlayerDamageProcessor_Die_Patch
	{
		private static void Postfix()
		{
			DiveTransitionManager.Instance?
				.OnLocalPlayerDied();
		}
	}

	[HarmonyPatch(
		typeof(PlayerDamageFeedback),
		nameof(PlayerDamageFeedback.DisplayDeathFeedback))]
	internal static class PlayerDamageFeedback_DisplayDeathFeedback_Patch
	{
		private static void Postfix(string source)
		{
			DiveSyncManager.Instance?.SetLocalDeathAnimation(
				string.Equals(
					source,
					"oxygen",
					StringComparison.OrdinalIgnoreCase)
					? "death_drowning"
					: "dead");
		}
	}

	[HarmonyPatch(
		typeof(DeathPanel),
		nameof(DeathPanel.Display))]
	internal static class DeathPanel_Display_Patch
	{
		private static bool Prefix(
			DeathPanel __instance)
		{
			bool isMultiplayer =
				NetworkManager.Instance != null &&
				NetworkManager.Instance.IsConnected &&
				NetworkManager.Instance.ConnectedPeers.Count > 0;

			if (isMultiplayer)
			{
				__instance.gameObject.SetActive(false);
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(HubOrchestrator), "Start")]
	internal static class HubOrchestrator_Start_Patch
	{
		private static void Postfix()
		{
			if (Singleton<GameFlowManager>.HasInstance())
				Singleton<GameFlowManager>.Instance.ReleaseAllLockLevels();

			Time.timeScale = 1f;
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;

			foreach (CanvasGroup cg in
				UnityEngine.Object.FindObjectsOfType<CanvasGroup>())
			{
				if (cg != null &&
					(cg.name.Contains("Damage") ||
					 cg.name.Contains("Heal") ||
					 cg.name.Contains("Hurt") ||
					 cg.name.Contains("Blood")))
				{
					cg.alpha = 0f;
				}
			}
		}
	}

	[HarmonyPatch(
		typeof(DivingLocationTrigger),
		"OnTriggerExit2D")]
	internal static class DivingLocationTrigger_OnTriggerExit2D_Patch
	{
		private static Exception Finalizer(
			Exception __exception) => null;
	}

	[HarmonyPatch(
		typeof(ExtractorLocationTrigger),
		"OnTriggerExit2D")]
	internal static class ExtractorLocationTrigger_OnTriggerExit2D_Patch
	{
		private static Exception Finalizer(
			Exception __exception) => null;
	}

	[HarmonyPatch(
		typeof(PlayerLocation),
		"OnExitLocation")]
	internal static class PlayerLocation_OnExitLocation_Patch
	{
		private static Exception Finalizer(
			Exception __exception) => null;
	}

	[HarmonyPatch(
		typeof(PlayerDarknessDetection),
		"OnDestroy")]
	internal static class PlayerDarknessDetection_OnDestroy_Patch
	{
		private static Exception Finalizer(
			Exception __exception) => null;
	}
}