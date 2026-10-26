using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public sealed class DiveLootSyncManager : MonoBehaviour
	{
		public static DiveLootSyncManager Instance { get; private set; }
		public bool IsApplyingNetworkSpawn { get; set; }

		private readonly Dictionary<int, CollectibleObject> _collectiblesByNetId = new Dictionary<int, CollectibleObject>();
		private int _nextSpawnNetId = 1;
		private int _nextDropId = 1000000;
		private NetworkManager _subscribedNetworkManager;
		private ManualLogSource _log;

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
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".DiveLootSync");
		}

		private void Update()
		{
			if (NetworkManager.Instance != null && !ReferenceEquals(NetworkManager.Instance, this._subscribedNetworkManager))
			{
				if (this._subscribedNetworkManager != null)
					this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;

				this._subscribedNetworkManager = NetworkManager.Instance;
				this._subscribedNetworkManager.MessageReceived += this.OnMessageReceived;
			}

			Patches.SyncFlaresPatch.ProcessPending();
		}

		public void ResetRegistry()
		{
			this._collectiblesByNetId.Clear();
			this._nextSpawnNetId = 1;
			this._nextDropId = 1000000;
			this.IsApplyingNetworkSpawn = false;
			Patches.SyncFlaresPatch.ClearRemoteFlares();
		}

		public void RegisterMapCollectible(CollectibleObject collectible)
		{
			if (collectible == null)
				return;

			int netId = this._nextSpawnNetId++;
			this._collectiblesByNetId[netId] = collectible;

			DynamicEntityNetId tag = collectible.gameObject.GetComponent<DynamicEntityNetId>()
				?? collectible.gameObject.AddComponent<DynamicEntityNetId>();

			tag.Id = netId.ToString();
		}

		public void OnLocalItemDropped(CollectibleObject spawnedCollectible, string itemId, Vector3 position)
		{
			if (spawnedCollectible == null || this.IsApplyingNetworkSpawn)
				return;

			NetworkManager net = this._subscribedNetworkManager;
			if (net == null || !net.IsConnected)
				return;

			int dropId = this._nextDropId++;
			this._collectiblesByNetId[dropId] = spawnedCollectible;

			DynamicEntityNetId tag = spawnedCollectible.gameObject.GetComponent<DynamicEntityNetId>()
				?? spawnedCollectible.gameObject.AddComponent<DynamicEntityNetId>();

			tag.Id = dropId.ToString();

			CollectibleSpawnMessage msg = new CollectibleSpawnMessage
			{
				ItemId = itemId,
				PosX = position.x,
				PosY = position.y,
				PosZ = position.z,
				NetId = dropId
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage());
			else
				net.SendToHost(msg.ToNetMessage());
		}

		public void OnItemCollectedNatively(CollectibleObject collectible)
		{
			if (collectible == null)
				return;

			NetworkManager net = this._subscribedNetworkManager;
			if (net == null || !net.IsConnected)
				return;

			DynamicEntityNetId tag = collectible.GetComponent<DynamicEntityNetId>();
			if (tag == null || !int.TryParse(tag.Id, out int netId))
				return;

			if (!this._collectiblesByNetId.Remove(netId))
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				Action = DiveStateAction.DestroyCollectible,
				IntArg1 = netId,
				StringArg = collectible.Data != null ? collectible.Data.Id : string.Empty,
				PosX = collectible.transform.position.x,
				PosY = collectible.transform.position.y
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage());
			else
				net.SendToHost(msg.ToNetMessage());
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			if (this._subscribedNetworkManager == null)
				return;

			switch (message.Type)
			{
				case MessageType.FlareSync:
					HandleFlare(peerId, message);
					return;

				case MessageType.ObstacleSync:
					Patches.SharedObstacleSyncPatch.HandleMessage(peerId, message);
					return;

				case MessageType.IdolPowerSync:
					Patches.SharedIdolPowersPatch.HandleMessage(peerId, message);
					return;

				case MessageType.FlashlightSync:
					Patches.SyncFlashlightPatch.HandleMessage(peerId, message);
					return;

				case MessageType.ScooterSync:
					Patches.SyncScooterPatch.HandleMessage(peerId, message);
					return;

				case MessageType.BeaconSync:
					Patches.SyncBeaconPatch.HandleMessage(peerId, message);
					return;

				case MessageType.ExtractorSync:
					Patches.SyncExtractorPatch.HandleMessage(peerId, message);
					return;

				case MessageType.BloodGateSync:
					Patches.SyncBloodGatesPatch.HandleMessage(peerId, message);
					return;

				case MessageType.LockerSync:
					Patches.SyncWaterproofLockerPatch.HandleMessage(peerId, message);
					return;

				case MessageType.DiveStateSync:
					HandleDiveState(peerId, message);
					return;

				case MessageType.CollectibleSpawn:
					HandleCollectibleSpawn(peerId, message);
					return;
			}
		}

		private void HandleFlare(int peerId, NetMessage message)
		{
			FlareSyncMessage msg = FlareSyncMessage.FromPayload(message.Payload);

			if (this._subscribedNetworkManager.IsHost)
			{
				msg.SourcePeerId = peerId;
				Patches.SyncFlaresPatch.HandleRemoteFlare(msg);
				this._subscribedNetworkManager.Broadcast(msg.ToNetMessage(), peerId);
			}
			else
			{
				Patches.SyncFlaresPatch.HandleRemoteFlare(msg);
			}
		}

		private void HandleDiveState(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			if (msg.Action == DiveStateAction.DestroyCollectible)
				this.DespawnByNetId(msg.IntArg1);

			if (this._subscribedNetworkManager.IsHost)
				this._subscribedNetworkManager.Broadcast(message, peerId);
		}

		private void HandleCollectibleSpawn(int peerId, NetMessage message)
		{
			CollectibleSpawnMessage msg = CollectibleSpawnMessage.FromPayload(message.Payload);

			this.IsApplyingNetworkSpawn = true;

			try
			{
				CollectibleManager manager = Singleton<DivingOrchestrator>.Instance?.CollectibleManager;

				if (manager != null)
				{
					CollectibleObject spawned = manager.SpawnCollectible(
						msg.ItemId,
						new Vector3(msg.PosX, msg.PosY, msg.PosZ),
						true,
						null,
						false
					);

					if (spawned != null)
					{
						this._collectiblesByNetId[msg.NetId] = spawned;

						DynamicEntityNetId tag = spawned.gameObject.GetComponent<DynamicEntityNetId>()
							?? spawned.gameObject.AddComponent<DynamicEntityNetId>();

						tag.Id = msg.NetId.ToString();
					}
				}
			}
			catch { }
			finally
			{
				this.IsApplyingNetworkSpawn = false;
			}

			if (this._subscribedNetworkManager.IsHost)
				this._subscribedNetworkManager.Broadcast(message, peerId);
		}

		private void DespawnByNetId(int netId)
		{
			if (!this._collectiblesByNetId.TryGetValue(netId, out CollectibleObject target) || target == null)
				return;

			this._collectiblesByNetId.Remove(netId);

			try
			{
				Singleton<DivingInteractionManager>.Instance?.RemoveInteractable(target);
				target.DisableCollection();
				Destroy(target.gameObject);
			}
			catch { }
		}

		private void OnDestroy()
		{
			if (this._subscribedNetworkManager != null)
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;

			if (Instance == this)
				Instance = null;
		}
	}

	[HarmonyPatch(typeof(CollectibleManager), nameof(CollectibleManager.AddCollectible))]
	internal static class CollectibleManager_AddCollectible_Patch
	{
		private static void Postfix(CollectibleObject collectible)
		{
			if (DiveLootSyncManager.Instance != null && !DiveLootSyncManager.Instance.IsApplyingNetworkSpawn)
				DiveLootSyncManager.Instance.RegisterMapCollectible(collectible);
		}
	}

	[HarmonyPatch(typeof(CollectibleManager), nameof(CollectibleManager.SpawnCollectible), new Type[] { typeof(string), typeof(Vector3), typeof(bool), typeof(Floating), typeof(bool), typeof(bool) })]
	internal static class CollectibleManager_SpawnCollectible_Patch
	{
		private static void Postfix(CollectibleObject __result, string id, Vector3 position, bool useDropPrefab)
		{
			if (useDropPrefab && __result != null &&
				DiveLootSyncManager.Instance != null &&
				!DiveLootSyncManager.Instance.IsApplyingNetworkSpawn)
			{
				DiveLootSyncManager.Instance.OnLocalItemDropped(__result, id, position);
			}
		}
	}

	[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.TryCollectItem))]
	internal static class CollectibleObject_TryCollectItem_Patch
	{
		private static void Postfix(CollectibleObject __instance)
		{
			DiveLootSyncManager.Instance?.OnItemCollectedNatively(__instance);
		}
	}
}