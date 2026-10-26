using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public sealed class RandomEventSyncManager : MonoBehaviour
	{
		public static RandomEventSyncManager Instance { get; private set; }

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
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".EventSync");
		}

		private void Update()
		{
			if (NetworkManager.Instance != null && !ReferenceEquals(NetworkManager.Instance, this._subscribedNetworkManager))
			{
				if (this._subscribedNetworkManager != null)
				{
					this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
				}
				this._subscribedNetworkManager = NetworkManager.Instance;
				this._subscribedNetworkManager.MessageReceived += this.OnMessageReceived;
			}
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			if (message.Type != MessageType.DynamicSpawn) return;
			if (this._subscribedNetworkManager == null || this._subscribedNetworkManager.IsHost) return;

			DynamicSpawnMessage msg = DynamicSpawnMessage.FromPayload(message.Payload);
			this._log.LogInfo($"[Client] Triggering synchronized dynamic event: {msg.EventName}");

			if (Singleton<RandomEventManager>.HasInstance())
			{
				Singleton<RandomEventManager>.Instance.SpawnRandomEvent(msg.EventName);
			}
		}

		private void OnDestroy()
		{
			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
			}
			if (Instance == this) Instance = null;
		}

		private ManualLogSource _log;
		private NetworkManager _subscribedNetworkManager;
	}

	[HarmonyPatch(typeof(RandomEventManager), nameof(RandomEventManager.SpawnRandomEvent))]
	internal static class RandomEventManager_SpawnRandomEvent_Patch
	{
		private static bool Prefix()
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost)
			{
				return false;
			}
			return true;
		}

		private static void Postfix(string eventName)
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected && NetworkManager.Instance.IsHost)
			{
				NetworkManager.Instance.Broadcast(new DynamicSpawnMessage
				{
					EventName = eventName,
					NetId = eventName
				}.ToNetMessage());
			}
		}
	}
}

