using System;
using System.Reflection;
using System.Collections.Generic;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncWaterproofLockerPatch
	{
		private enum LockerAction
		{
			CreateRequest = 1,
			Create = 2,
			Remove = 3,
			AddItem = 4,
			RemoveItem = 5
		}

		public static bool IsApplyingRemoteLocker;

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			switch ((LockerAction)msg.IntArg1)
			{
				case LockerAction.CreateRequest:
					if (!NetworkManager.Instance.IsHost)
						return;

					GameState gs = Singleton<GameState>.Instance;
					if (gs?.PoolState == null)
						return;

					int uniqueId = gs.PoolState.NextPersistentItemUniqueId;
					PersistentContainer container = new PersistentContainer(
						msg.PosX,
						msg.PosY,
						"Locker_" + uniqueId,
						uniqueId,
						9,
						new InventoryState()
					);

					gs.PoolState.AddPersistentContainer(container);
					Singleton<PersistentPoolItemManager>.Instance.SpawnLocker(container);
					break;

				case LockerAction.Create:
					if (NetworkManager.Instance.IsHost)
						return;

					if (int.TryParse(msg.StringArg, out int createId))
						ApplyCreate(createId, msg.PosX, msg.PosY);
					break;

				case LockerAction.Remove:
					if (int.TryParse(msg.StringArg, out int removeId))
					{
						ApplyRemove(removeId);

						if (NetworkManager.Instance.IsHost)
							NetworkManager.Instance.Broadcast(message, peerId);
					}
					break;

				case LockerAction.AddItem:
					ApplyItemChange(msg.StringArg, true);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;

				case LockerAction.RemoveItem:
					ApplyItemChange(msg.StringArg, false);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;
			}
		}

		private static void ApplyCreate(int uniqueId, float posX, float posY)
		{
			PersistentContainer existing = FindLockerById(uniqueId);
			if (existing != null)
				return;

			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return;

			PersistentContainer container = new PersistentContainer(
				posX,
				posY,
				"Locker_" + uniqueId,
				uniqueId,
				9,
				new InventoryState()
			);

			gs.PoolState.AddPersistentContainer(container);

			IsApplyingRemoteLocker = true;

			try
			{
				Singleton<PersistentPoolItemManager>.Instance.SpawnLocker(container);
			}
			finally
			{
				IsApplyingRemoteLocker = false;
			}
		}

		private static void ApplyRemove(int uniqueId)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return;

			PersistentContainer target = FindLockerById(uniqueId);
			if (target == null)
				return;

			GameObject shell = null;

			var spawnedDict = AccessTools.Field(typeof(PersistentPoolItemManager), "_spawnedLockers")
				?.GetValue(Singleton<PersistentPoolItemManager>.Instance)
				as Dictionary<PersistentContainer, PersistentPoolObject>;

			if (spawnedDict != null && spawnedDict.TryGetValue(target, out PersistentPoolObject poolObj))
			{
				if (poolObj?.collectibleObject?.transform.parent != null)
					shell = poolObj.collectibleObject.transform.parent.gameObject;
			}

			IsApplyingRemoteLocker = true;

			try
			{
				gs.PoolState.RemovePersistentContainer(target);
				Singleton<PersistentPoolItemManager>.Instance.DespawnLocker(target);

				if (shell != null)
					UnityEngine.Object.Destroy(shell);
			}
			finally
			{
				IsApplyingRemoteLocker = false;
			}
		}

		private static void ApplyItemChange(string value, bool add)
		{
			string[] parts = (value ?? string.Empty).Split('|');

			if (parts.Length != 2 || !int.TryParse(parts[0], out int uniqueId))
				return;

			PersistentContainer target = FindLockerById(uniqueId);
			if (target == null)
				return;

			if (add)
			{
				ItemData data = HubLootSyncManager.ResolveItemData(parts[1]);

				if (data != null)
				{
					target.InventoryState.AddItem(new Item(data, 1));
					RefreshLockerVisualsAndUI(target);
				}
			}
			else
			{
				Item itemToRemove = null;

				foreach (Item item in target.InventoryState.Items.Values)
				{
					if (item != null && item.Data != null && item.Data.Id == parts[1])
					{
						itemToRemove = item;
						break;
					}
				}

				if (itemToRemove != null)
				{
					target.InventoryState.RemoveItem(itemToRemove, 1);
					RefreshLockerVisualsAndUI(target);
				}
			}
		}

		private static PersistentContainer FindLockerById(int uniqueId)
		{
			var poolState = Singleton<GameState>.Instance?.PoolState;

			if (poolState == null || poolState.PersistentContainers == null)
				return null;

			foreach (PersistentContainer container in poolState.PersistentContainers)
			{
				if (container.UniqueId == uniqueId)
					return container;
			}

			return null;
		}

		private static void RefreshLockerVisualsAndUI(PersistentContainer container)
		{
			var spawnedDict = AccessTools.Field(typeof(PersistentPoolItemManager), "_spawnedLockers")
				?.GetValue(Singleton<PersistentPoolItemManager>.Instance)
				as Dictionary<PersistentContainer, PersistentPoolObject>;

			if (spawnedDict != null && spawnedDict.TryGetValue(container, out PersistentPoolObject poolObj))
			{
				if (poolObj?.collectibleObject?.transform.parent != null)
				{
					Locker locker = poolObj.collectibleObject.transform.parent.GetComponentInChildren<Locker>();

					if (locker != null)
						locker.SetLocker(container);
				}
			}

			if (Singleton<DivingUIOrchestrator>.HasInstance())
			{
				LockerWidget widget = Singleton<DivingUIOrchestrator>.Instance.GetWidget<LockerWidget>();

				if (widget != null && widget.gameObject.activeSelf)
				{
					PersistentContainer current = AccessTools.Field(typeof(LockerWidget), "_persistentContainer")
						?.GetValue(widget) as PersistentContainer;

					if (current != null && current.UniqueId == container.UniqueId)
						widget.UpdateLockerInventory();
				}
			}
		}

		[HarmonyPatch(typeof(UsableItem_WaterproofLocker), "UseInternal")]
		[HarmonyPrefix]
		private static bool UseInternal_Locker_Prefix(UsableItem_WaterproofLocker __instance)
		{
			NetworkManager net = NetworkManager.Instance;

			if (net == null || !net.IsConnected || net.IsHost)
				return true;

			Vector2 offset = Vector2.zero;
			FieldInfo offsetField = AccessTools.Field(typeof(UsableItem_WaterproofLocker), "_instantiationOffset");

			if (offsetField != null)
				offset = (Vector2)offsetField.GetValue(__instance);

			Vector2 pos = (Vector2)Singleton<PlayerController>.Instance.transform.position + offset;

			net.SendToHost(new DiveStateSyncMessage
			{
				IntArg1 = (int)LockerAction.CreateRequest,
				PosX = pos.x,
				PosY = pos.y
			}.ToNetMessage(MessageType.LockerSync));

			FieldInfo sfxField = AccessTools.Field(typeof(UsableItem_WaterproofLocker), "_sfx");

			if (sfxField != null)
			{
				EventReference sfx = (EventReference)sfxField.GetValue(__instance);

				if (!sfx.IsNull)
				{
					EventInstance instance = RuntimeManager.CreateInstance(sfx);
					instance.start();
					instance.release();
				}
			}

			return false;
		}

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.SpawnLocker))]
		[HarmonyPostfix]
		private static void SpawnLocker_Postfix(PersistentContainer locker)
		{
			if (IsApplyingRemoteLocker)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsHost || locker == null)
				return;

			net.Broadcast(new DiveStateSyncMessage
			{
				IntArg1 = (int)LockerAction.Create,
				StringArg = locker.UniqueId.ToString(),
				PosX = locker.PosX,
				PosY = locker.PosY
			}.ToNetMessage(MessageType.LockerSync));
		}

		[HarmonyPatch(typeof(PoolState), nameof(PoolState.RemovePersistentContainer))]
		[HarmonyPrefix]
		private static void RemovePersistentContainer_Prefix(PersistentContainer container)
		{
			if (IsApplyingRemoteLocker || container == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)LockerAction.Remove,
				StringArg = container.UniqueId.ToString()
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.LockerSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.LockerSync));
		}

		[HarmonyPatch(typeof(LockerWidget), nameof(LockerWidget.TransferItemFromPlayerToLocker))]
		[HarmonyPostfix]
		private static void TransferItemFromPlayerToLocker_Postfix(LockerWidget __instance, Item item)
		{
			SendItemChange(__instance, item, LockerAction.AddItem);
		}

		[HarmonyPatch(typeof(LockerWidget), nameof(LockerWidget.TransferItemFromLockerToPlayer))]
		[HarmonyPostfix]
		private static void TransferItemFromLockerToPlayer_Postfix(LockerWidget __instance, Item item)
		{
			SendItemChange(__instance, item, LockerAction.RemoveItem);
		}

		[HarmonyPatch(typeof(LockerWidget), nameof(LockerWidget.SendItemToSurface))]
		[HarmonyPostfix]
		private static void SendItemToSurface_Postfix(LockerWidget __instance, Item item)
		{
			bool isLockerItem = (bool)(AccessTools.Field(typeof(LockerWidget), "_isLockerItemSelected")?.GetValue(__instance) ?? false);

			if (isLockerItem)
				SendItemChange(__instance, item, LockerAction.RemoveItem);
		}

		private static void SendItemChange(LockerWidget widget, Item item, LockerAction action)
		{
			if (IsApplyingRemoteLocker)
				return;

			NetworkManager net = NetworkManager.Instance;

			if (net == null || !net.IsConnected || item?.Data == null)
				return;

			PersistentContainer container = AccessTools.Field(typeof(LockerWidget), "_persistentContainer")
				?.GetValue(widget) as PersistentContainer;

			if (container == null)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)action,
				StringArg = $"{container.UniqueId}|{item.Data.Id}"
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.LockerSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.LockerSync));
		}
	}
}