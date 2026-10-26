using System.Linq;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Hub;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class FishingHutPatch
	{
		[HarmonyPatch(typeof(NetworkManager), "Awake")]
		[HarmonyPostfix]
		private static void NetworkManager_Awake_Postfix(NetworkManager __instance)
		{
			__instance.MessageReceived += OnMessageReceived;
		}

		private static void OnMessageReceived(int peerId, NetMessage message)
		{
			if (message.Type != MessageType.DiveStateSync || NetworkManager.Instance == null || !NetworkManager.Instance.IsHost) return;
			var msg = DiveStateSyncMessage.FromPayload(message.Payload);

			bool stateChanged = false;

			if (msg.IntArg1 == -1001)
			{
				int slotIndex = msg.IntArg2;
				int fishableIndex = (int)msg.PosX;
				var camp = Singleton<GameState>.Instance.CampState;
				
				if (slotIndex >= 0 && slotIndex < camp.FishableInventories.Count)
				{
					var inv = camp.FishableInventories[slotIndex];
					if (fishableIndex >= 0 && fishableIndex < inv.Count)
					{
						var fish = inv[fishableIndex];
						inv.RemoveAt(fishableIndex);
						
						camp.ChangeNutritionValue(0, fish.NutritionValue);
						camp.ChangeMoraleValue(0, fish.MoraleValue);
						
						if (fish.FishableType == FishableDataType.MATERIAL)
						{
							camp.AddItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(fish.Id), 1));
						}
						else if (fish.Id == "Geode")
						{
							var list = Singleton<GameDatabase>.Instance.GetAllData<CollectibleData>().Values.Where(x => Const.MINERALS_COLLECTIBLE.Contains(x.Id) && x.Tier <= 1 && !x.IsBlessed).ToList();
							var mat = list[UnityEngine.Random.Range(0, list.Count)];
							camp.AddItem(new Item(mat, 2));
						}
						else if (fish.Id == "YoungGoldBoonfish")
						{
							foreach (var s in camp.Survivors) s.AddExperience(300, true);
						}
						
						stateChanged = true;
					}
				}
			}
			else if (msg.IntArg1 == -1002)
			{
				int slotIndex = msg.IntArg2;
				int fishableIndex = (int)msg.PosX;
				var camp = Singleton<GameState>.Instance.CampState;
				if (slotIndex >= 0 && slotIndex < camp.FishableInventories.Count)
				{
					var inv = camp.FishableInventories[slotIndex];
					if (fishableIndex >= 0 && fishableIndex < inv.Count)
					{
						inv.RemoveAt(fishableIndex);
						stateChanged = true;
					}
				}
			}
			else if (msg.IntArg1 == -1003)
			{
				int slotIndex = msg.IntArg2;
				var camp = Singleton<GameState>.Instance.CampState;
				var netData = Singleton<GameDatabase>.Instance.GetClonedData<FishingNetData>("BaitedFishingNet");
				if (netData != null)
				{
					foreach (var kvp in netData.CraftingComponents)
					{
						int held = camp.GetHeldItemCount(ItemType.MATERIAL, kvp.Key);
						if (held >= kvp.Value)
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key), kvp.Value));
						else if (!kvp.Key.Contains("_Blessed"))
						{
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key), held));
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key + "_Blessed"), kvp.Value - held));
						}
					}
					camp.FishingNets[slotIndex] = netData.Id;
					camp.FishingNetDurations[slotIndex] = 4;
					stateChanged = true;
				}
			}
			else if (msg.IntArg1 == -1004)
			{
				int slotIndex = msg.IntArg2;
				var camp = Singleton<GameState>.Instance.CampState;
				var netData = Singleton<GameDatabase>.Instance.GetClonedData<FishingNetData>("MagnetizedFishingNet");
				if (netData != null)
				{
					foreach (var kvp in netData.CraftingComponents)
					{
						int held = camp.GetHeldItemCount(ItemType.MATERIAL, kvp.Key);
						if (held >= kvp.Value)
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key), kvp.Value));
						else if (!kvp.Key.Contains("_Blessed"))
						{
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key), held));
							camp.RemoveItem(new Item(Singleton<GameDatabase>.Instance.GetClonedData<CollectibleData>(kvp.Key + "_Blessed"), kvp.Value - held));
						}
					}
					camp.FishingNets[slotIndex] = netData.Id;
					camp.FishingNetDurations[slotIndex] = 4;
					stateChanged = true;
				}
			}
			else if (msg.IntArg1 == -1005)
			{
				int priorityIndex = msg.IntArg2;
				Singleton<GameState>.Instance.CampState.FishingNets[0] = (priorityIndex == 0) ? "BaitedFishingNetFisherman" : "MagnetizedFishingNetFisherman";
				stateChanged = true;
			}

			if (stateChanged)
			{
				Singleton<GameState>.Instance?.Save();
				HubSyncManager.Instance?.ForceSendSync();
			}
		}

		[HarmonyPatch(typeof(HubActionSyncManager), nameof(HubActionSyncManager.SyncClientCampStateToHost))]
		[HarmonyPrefix]
		private static bool BlockDestructiveJsonSync()
		{
			var fishingHut = UnityEngine.Object.FindObjectOfType<MenuFishingHut>();
			if (fishingHut != null && fishingHut.IsDisplayed)
			{
				return false; 
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.CollectFishable))]
		[HarmonyPrefix]
		private static bool CollectFishable_Prefix(int slotIndex, int fishableIndex)
		{
			CampState camp = Singleton<GameState>.Instance?.CampState;
			if (camp?.FishableInventories == null || slotIndex < 0 || slotIndex >= camp.FishableInventories.Count) return false;
			if (fishableIndex < 0 || fishableIndex >= camp.FishableInventories[slotIndex].Count) return false;
			
			if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				var msg = new DiveStateSyncMessage { Action = DiveStateAction.AddEventCount, IntArg1 = -1001, IntArg2 = slotIndex, PosX = fishableIndex };
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.DiscardFishable))]
		[HarmonyPrefix]
		private static bool DiscardFishable_Prefix(int slotIndex, int fishableIndex)
		{
			if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				var msg = new DiveStateSyncMessage { Action = DiveStateAction.AddEventCount, IntArg1 = -1002, IntArg2 = slotIndex, PosX = fishableIndex };
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.OnFishingSlotCraftFishNetClicked))]
		[HarmonyPrefix]
		private static bool CraftFishNet_Prefix(int slotIndex)
		{
			if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				var msg = new DiveStateSyncMessage { Action = DiveStateAction.AddEventCount, IntArg1 = -1003, IntArg2 = slotIndex };
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.OnFishingSlotTreasureNetClicked))]
		[HarmonyPrefix]
		private static bool CraftTreasureNet_Prefix(int slotIndex)
		{
			if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				var msg = new DiveStateSyncMessage { Action = DiveStateAction.AddEventCount, IntArg1 = -1004, IntArg2 = slotIndex };
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.OnFishermanPriorityToggled))]
		[HarmonyPrefix]
		private static bool TogglePriority_Prefix(int priorityIndex)
		{
			if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				var msg = new DiveStateSyncMessage { Action = DiveStateAction.AddEventCount, IntArg1 = -1005, IntArg2 = priorityIndex };
				NetworkManager.Instance.SendToHost(msg.ToNetMessage());
			}
			return true;
		}

		[HarmonyPatch(typeof(MenuFishingHut), nameof(MenuFishingHut.OnCollectAllButtonClick))]
		[HarmonyPrefix]
		private static bool OnCollectAllButtonClick_Prefix(int slotIndex)
		{
			CampState camp = Singleton<GameState>.Instance?.CampState;
			if (camp?.FishableInventories == null) return false;
			if (slotIndex < 0 || slotIndex >= camp.FishableInventories.Count) return false;

			var inventory = camp.FishableInventories[slotIndex];
			return inventory != null && inventory.Count > 0;
		}
	}
}

