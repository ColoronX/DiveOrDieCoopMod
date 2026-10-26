using BepInEx;
using BepInEx.Configuration;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.LAN;
using DiveOrDieCoop.UI;
#if STEAMWORKS_NET
using DiveOrDieCoop.Networking.Steam;
#endif
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop
{
	[BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
	public sealed class Plugin : BaseUnityPlugin
	{
		private void Awake()
		{
			this._playerNameConfig = this.Config.Bind("Network", "PlayerName", "Diver", "Name shown to other players during handshake.");
			this._portConfig = this.Config.Bind("Network", "Port", 27511, "TCP port used for LAN hosting.");
			this._discoveryPortConfig = this.Config.Bind("Network", "DiscoveryPort", LanHostDiscovery.DefaultDiscoveryPort, "UDP port used for LAN host discovery broadcasts.");
			this._maxPlayersConfig = this.Config.Bind("Network", "MaxPlayers", 4,
				new ConfigDescription(
					"Maximum peers a hosted lobby accepts. Capped at 12: with a 20-character roster, 6-8 are typically manning buildings and unavailable to dive, so 12 concurrent divers is the practical ceiling.",
					new AcceptableValueRange<int>(2, 12)));

			this._harmony = new Harmony(PluginInfo.GUID);
			this._harmony.PatchAll();

#if STEAMWORKS_NET
			GameObject steamRuntimeObject = new GameObject("DiveOrDieCoop.SteamRuntime") { hideFlags = HideFlags.HideAndDontSave };
			steamRuntimeObject.AddComponent<SteamRuntime>();
			Object.DontDestroyOnLoad(steamRuntimeObject);

			GameObject steamLobbyObject = new GameObject("DiveOrDieCoop.SteamLobbyManager") { hideFlags = HideFlags.HideAndDontSave };
			SteamLobbyManager steamLobbyManager = steamLobbyObject.AddComponent<SteamLobbyManager>();
			steamLobbyManager.Initialize();
			Object.DontDestroyOnLoad(steamLobbyObject);
#endif

			SceneManager.sceneLoaded += this.OnSceneLoaded;
			this.EnsureManagersExist("initial Awake");
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			this.EnsureManagersExist($"scene '{scene.name}' loaded ({mode})");
		}

		private void EnsureManagersExist(string reasonForLog)
		{
			bool createdAnything = false;

			if (NetworkManager.Instance == null)
			{
				GameObject networkManagerObject = new GameObject("DiveOrDieCoop.NetworkManager") { hideFlags = HideFlags.HideAndDontSave };
				NetworkManager networkManager = networkManagerObject.AddComponent<NetworkManager>();
				networkManager.LocalPlayerName = this._playerNameConfig.Value;
				networkManager.Port = this._portConfig.Value;
				networkManager.DiscoveryPort = this._discoveryPortConfig.Value;
				networkManager.MaxPlayers = this._maxPlayersConfig.Value;
				Object.DontDestroyOnLoad(networkManagerObject);
				createdAnything = true;
			}

			if (CoopDebugOverlay.Instance == null)
			{
				GameObject overlayObject = new GameObject("DiveOrDieCoop.DebugOverlay") { hideFlags = HideFlags.HideAndDontSave };
				overlayObject.AddComponent<CoopDebugOverlay>();
				Object.DontDestroyOnLoad(overlayObject);
				createdAnything = true;
			}

			if (DiveSyncManager.Instance == null)
			{
				GameObject diveSyncObject = new GameObject("DiveOrDieCoop.DiveSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				diveSyncObject.AddComponent<DiveSyncManager>();
				Object.DontDestroyOnLoad(diveSyncObject);
				createdAnything = true;
			}

			if (DiveLootSyncManager.Instance == null)
			{
				GameObject diveLootObject = new GameObject("DiveOrDieCoop.DiveLootSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				diveLootObject.AddComponent<DiveLootSyncManager>();
				Object.DontDestroyOnLoad(diveLootObject);
				createdAnything = true;
			}

			if (DiveTransitionManager.Instance == null)
			{
				GameObject diveTransObject = new GameObject("DiveOrDieCoop.DiveTransitionManager") { hideFlags = HideFlags.HideAndDontSave };
				diveTransObject.AddComponent<DiveTransitionManager>();
				Object.DontDestroyOnLoad(diveTransObject);
				createdAnything = true;
			}

			if (CombatSyncManager.Instance == null)
			{
				GameObject combatSyncObject = new GameObject("DiveOrDieCoop.CombatSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				combatSyncObject.AddComponent<CombatSyncManager>();
				Object.DontDestroyOnLoad(combatSyncObject);
				createdAnything = true;
			}

			if (WorldSyncManager.Instance == null)
			{
				GameObject worldSyncObject = new GameObject("DiveOrDieCoop.WorldSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				worldSyncObject.AddComponent<WorldSyncManager>();
				Object.DontDestroyOnLoad(worldSyncObject);
				createdAnything = true;
			}

			if (HubLootSyncManager.Instance == null)
			{
				GameObject inventorySyncObject = new GameObject("DiveOrDieCoop.HubLootSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				inventorySyncObject.AddComponent<HubLootSyncManager>();
				Object.DontDestroyOnLoad(inventorySyncObject);
				createdAnything = true;
			}

			if (RandomEventSyncManager.Instance == null)
			{
				GameObject randomEventSyncObject = new GameObject("DiveOrDieCoop.RandomEventSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				randomEventSyncObject.AddComponent<RandomEventSyncManager>();
				Object.DontDestroyOnLoad(randomEventSyncObject);
				createdAnything = true;
			}

			if (HubSyncManager.Instance == null)
			{
				GameObject hubSyncObject = new GameObject("DiveOrDieCoop.HubSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				hubSyncObject.AddComponent<HubSyncManager>();
				Object.DontDestroyOnLoad(hubSyncObject);
				createdAnything = true;
			}

			if (HubActionSyncManager.Instance == null)
			{
				GameObject hubActionSyncObject = new GameObject("DiveOrDieCoop.HubActionSyncManager") { hideFlags = HideFlags.HideAndDontSave };
				hubActionSyncObject.AddComponent<HubActionSyncManager>();
				Object.DontDestroyOnLoad(hubActionSyncObject);
				createdAnything = true;
			}

			if (createdAnything)
			{
				this.Logger.LogInfo($"(Re)created mod managers on: {reasonForLog}.");
			}
		}

		private ConfigEntry<string> _playerNameConfig;
		private ConfigEntry<int> _portConfig;
		private ConfigEntry<int> _discoveryPortConfig;
		private ConfigEntry<int> _maxPlayersConfig;
		private Harmony _harmony;
	}
}

