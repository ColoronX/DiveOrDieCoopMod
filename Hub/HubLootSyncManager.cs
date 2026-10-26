using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using ED.Hub;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Hub
{
	public sealed class HubLootSyncManager : MonoBehaviour
	{
		public static HubLootSyncManager Instance { get; private set; }

		public readonly Dictionary<int, Dictionary<string, int>> ClientBackpacks = new Dictionary<int, Dictionary<string, int>>();

		public int TeamFoodValue { get; set; }
		public int TeamMementoValue { get; set; }

		private NetworkManager _subscribedNetworkManager;
		private ManualLogSource _log;

		private void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
			Instance = this;
			this.gameObject.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(this.gameObject);
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".HubLootSync");
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
		}

		public void RecordClientPickup(int peerId, string itemId)
		{
			if (string.IsNullOrEmpty(itemId)) return;
			if (!this.ClientBackpacks.ContainsKey(peerId)) this.ClientBackpacks[peerId] = new Dictionary<string, int>();
			if (!this.ClientBackpacks[peerId].ContainsKey(itemId)) this.ClientBackpacks[peerId][itemId] = 0;
			
			this.ClientBackpacks[peerId][itemId]++;
			this._log.LogInfo($"[Host] Peer {peerId} collected {itemId}. Total: {this.ClientBackpacks[peerId][itemId]}");
		}

		public void RecordClientDrop(int peerId, string itemId)
		{
			if (this.ClientBackpacks.ContainsKey(peerId) && this.ClientBackpacks[peerId].ContainsKey(itemId))
			{
				this.ClientBackpacks[peerId][itemId]--;
				if (this.ClientBackpacks[peerId][itemId] <= 0) this.ClientBackpacks[peerId].Remove(itemId);
				this._log.LogInfo($"[Host] Peer {peerId} dropped {itemId}.");
			}
		}

		public void ClearClientBackpack(int peerId)
		{
			if (this.ClientBackpacks.ContainsKey(peerId))
			{
				this.ClientBackpacks[peerId].Clear();
				this._log.LogInfo($"[Host] Cleared backpack tracking for dead Peer {peerId}.");
			}
		}

		public void ProcessAllPendingClientLoot()
		{
			CampState camp = Singleton<GameState>.Instance?.CampState;
			if (camp == null) return;

			int addedCount = 0;
			foreach (var peerKvp in this.ClientBackpacks)
			{
				foreach (var itemKvp in peerKvp.Value)
				{
					string itemId = itemKvp.Key;
					int count = itemKvp.Value;
					if (count <= 0) continue;

					ItemData data = ResolveItemData(itemId);
					if (data != null)
					{
						Item item = new Item(data, count);
						camp.AddItem(item);
						addedCount += count;

						if (data.Type == ItemType.FOOD)
						{
							var nutritionAttr = data.GetAttribute(ItemAttributeID.NUTRITION_VALUE);
							if (nutritionAttr != null) this.TeamFoodValue += nutritionAttr.Value * count;
						}
						else if (data.Type == ItemType.MEMENTO)
						{
							var moraleAttr = data.GetAttribute(ItemAttributeID.MORALE_VALUE);
							if (moraleAttr != null) this.TeamMementoValue += moraleAttr.Value * count;
						}
					}
				}
			}

			if (addedCount > 0)
			{
				this._log.LogInfo($"[Host] Pooled {addedCount} items from surviving clients into CampState.");
			}
			
			this.ClientBackpacks.Clear();
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			if (this._subscribedNetworkManager == null || !this._subscribedNetworkManager.IsHost) return;

			if (message.Type == MessageType.DiveStateSync)
			{
				DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

				if (msg.Action == DiveStateAction.DestroyCollectible)
				{
					this.RecordClientPickup(peerId, msg.StringArg);
				}
				else if (msg.Action == DiveStateAction.AddExperiencePoints)
				{
					if (Singleton<GameState>.Instance?.ProgressionState?.divingState != null)
					{
						Singleton<GameState>.Instance.ProgressionState.divingState.AddExperiencePoints(msg.StringArg, msg.IntArg1);
					}
				}
			}
			else if (message.Type == MessageType.CollectibleSpawn)
			{
				CollectibleSpawnMessage msg = CollectibleSpawnMessage.FromPayload(message.Payload);
				if (msg.ItemId != "SYNC_FLARE") 
				{
					this.RecordClientDrop(peerId, msg.ItemId);
				}
			}
			else if (message.Type == MessageType.DiverDied)
			{
				this.ClearClientBackpack(peerId);
			}
		}

		public static ItemData ResolveItemData(string itemId)
		{
			if (string.IsNullOrEmpty(itemId)) return null;
			GameDatabase db = Singleton<GameDatabase>.Instance;
			if (db == null) return null;
			if (db.HasData<CollectibleData>(itemId)) return db.GetClonedData<CollectibleData>(itemId);
			if (db.HasData<CraftableItemData>(itemId)) return db.GetClonedData<CraftableItemData>(itemId);
			if (db.HasData<EquipmentItemData>(itemId)) return db.GetClonedData<EquipmentItemData>(itemId);
			if (db.HasData<UpgradeItemData>(itemId)) return db.GetClonedData<UpgradeItemData>(itemId);
			if (db.HasData<UpgradeEquipmentData>(itemId)) return db.GetClonedData<UpgradeEquipmentData>(itemId);
			if (db.HasData<UsableItemData>(itemId)) return db.GetClonedData<UsableItemData>(itemId);
			if (db.HasData<FishableData>(itemId)) return db.GetClonedData<FishableData>(itemId);
			if (db.HasData<ItemData>(itemId)) return db.GetClonedData<ItemData>(itemId);
			return null;
		}
	}

	[HarmonyPatch(typeof(HubOrchestrator), "AfterDivePreProcessor")]
	internal static class HubOrchestrator_AfterDivePreProcessor_Loot_Patch
	{
		private static void Prefix()
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
			{
				HubLootSyncManager.Instance?.ProcessAllPendingClientLoot();
			}
		}
	}

	[HarmonyPatch(typeof(HubOrchestrator), "ApplyThresholdsEffects")]
	internal static class HubOrchestrator_ApplyThresholdsEffects_Loot_Patch
	{
		private static void Prefix()
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
			{
				int extraFood = HubLootSyncManager.Instance?.TeamFoodValue ?? 0;
				int extraMemento = HubLootSyncManager.Instance?.TeamMementoValue ?? 0;

				DivingState dive = Singleton<GameState>.Instance?.ProgressionState?.divingState;

				if (extraFood > 0)
				{
					if (dive != null) dive.totalFoodValue += extraFood; 
				}
				if (extraMemento > 0)
				{
					if (dive != null) dive.totalMementoValue += extraMemento;
				}

				if (HubLootSyncManager.Instance != null)
				{
					HubLootSyncManager.Instance.TeamFoodValue = 0;
					HubLootSyncManager.Instance.TeamMementoValue = 0;
				}
			}
		}
	}

	[HarmonyPatch(typeof(DivingState), nameof(DivingState.AddExperiencePoints))]
	internal static class DivingState_AddExperiencePoints_Patch
	{
		private static void Postfix(string survivorId, int points)
		{
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost)
			{
				DiveStateSyncMessage msg = new DiveStateSyncMessage
				{
					Action = DiveStateAction.AddExperiencePoints,
					StringArg = survivorId,
					IntArg1 = points
				};
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
		}
	}
}

