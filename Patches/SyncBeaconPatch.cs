using System;
using System.Collections;
using System.Reflection;
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
	public static class SyncBeaconPatch
	{
		private enum BeaconAction
		{
			CreateRequest = 1,
			Create = 2,
			Remove = 3,
			Rename = 4
		}

		public static bool IsApplyingRemoteBeacon;

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			switch ((BeaconAction)msg.IntArg1)
			{
				case BeaconAction.CreateRequest:
					if (!NetworkManager.Instance.IsHost)
						return;

					GameState gs = Singleton<GameState>.Instance;
					if (gs?.PoolState == null)
						return;

					int uniqueId = gs.PoolState.NextPersistentItemUniqueId;
					string name = msg.StringArg ?? "Beacon";
					PersistentBeacon beacon = new PersistentBeacon(msg.PosX, msg.PosY, "Beacon_" + uniqueId, uniqueId, name);

					var addMethod = AccessTools.Method(gs.PoolState.GetType(), "AddPersistentBeacon");
					addMethod?.Invoke(gs.PoolState, new object[] { beacon });

					IsApplyingRemoteBeacon = true;
					try
					{
						Singleton<PersistentPoolItemManager>.Instance.SpawnBeacon(beacon);
					}
					finally
					{
						IsApplyingRemoteBeacon = false;
					}
					break;

				case BeaconAction.Create:
					if (NetworkManager.Instance.IsHost)
						return;

					string[] createParts = (msg.StringArg ?? string.Empty).Split('|');
					if (createParts.Length != 2 || !int.TryParse(createParts[1], out int createId))
						return;

					PersistentBeacon existing = FindBeaconById(createId);
					if (existing != null)
						return;

					PersistentBeacon remoteBeacon = new PersistentBeacon(
						msg.PosX,
						msg.PosY,
						"Beacon_" + createId,
						createId,
						createParts[0]
					);

					GameState clientGs = Singleton<GameState>.Instance;
					var clientAddMethod = AccessTools.Method(clientGs.PoolState.GetType(), "AddPersistentBeacon");
					clientAddMethod?.Invoke(clientGs.PoolState, new object[] { remoteBeacon });

					IsApplyingRemoteBeacon = true;
					try
					{
						Singleton<PersistentPoolItemManager>.Instance.SpawnBeacon(remoteBeacon);
					}
					finally
					{
						IsApplyingRemoteBeacon = false;
					}
					break;

				case BeaconAction.Remove:
					ApplyRemove(msg.StringArg);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;

				case BeaconAction.Rename:
					ApplyRename(msg.StringArg);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;
			}
		}

		private static PersistentBeacon FindBeaconById(int uniqueId)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return null;

			IEnumerable list = AccessTools.Property(gs.PoolState.GetType(), "PersistentBeacons")?.GetValue(gs.PoolState) as IEnumerable
				?? AccessTools.Field(gs.PoolState.GetType(), "PersistentBeacons")?.GetValue(gs.PoolState) as IEnumerable;

			if (list == null)
				return null;

			foreach (PersistentBeacon beacon in list)
			{
				if (beacon.UniqueId == uniqueId)
					return beacon;
			}

			return null;
		}

		private static void ApplyRemove(string value)
		{
			if (!int.TryParse(value, out int uniqueId))
				return;

			GameState gs = Singleton<GameState>.Instance;
			PersistentBeacon target = FindBeaconById(uniqueId);

			if (gs?.PoolState == null || target == null)
				return;

			GameObject shell = null;
			var spawned = AccessTools.Field(typeof(PersistentPoolItemManager), "_spawnedBeacons")?.GetValue(Singleton<PersistentPoolItemManager>.Instance) as IDictionary;

			if (spawned != null && spawned.Contains(target))
			{
				object poolObj = spawned[target];

				if (poolObj != null)
				{
					FieldInfo field = AccessTools.Field(poolObj.GetType(), "collectibleObject");
					CollectibleObject collectible = field?.GetValue(poolObj) as CollectibleObject;

					if (collectible?.transform.parent != null)
						shell = collectible.transform.parent.gameObject;
				}
			}

			IsApplyingRemoteBeacon = true;
			try
			{
				var removeMethod = AccessTools.Method(gs.PoolState.GetType(), "RemovePersistentBeacon");
				removeMethod?.Invoke(gs.PoolState, new object[] { target });
				Singleton<PersistentPoolItemManager>.Instance.DespawnBeacon(target);

				if (shell != null)
					UnityEngine.Object.Destroy(shell);
			}
			finally
			{
				IsApplyingRemoteBeacon = false;
			}
		}

		private static void ApplyRename(string value)
		{
			string[] parts = (value ?? string.Empty).Split(new[] { '|' }, 2);

			if (parts.Length != 2 || !int.TryParse(parts[0], out int uniqueId))
				return;

			PersistentBeacon target = FindBeaconById(uniqueId);
			if (target == null)
				return;

			IsApplyingRemoteBeacon = true;
			try
			{
				target.Name = parts[1];
				Singleton<PersistentPoolItemManager>.Instance.RenameBeacon(target, parts[1]);
			}
			finally
			{
				IsApplyingRemoteBeacon = false;
			}
		}

		[HarmonyPatch(typeof(UsableItem_Beacon), "OnPopupConfirm")]
		[HarmonyPrefix]
		private static bool OnPopupConfirm_Prefix(UsableItem_Beacon __instance, string name)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || net.IsHost)
				return true;

			Vector2 offset = Vector2.zero;
			FieldInfo offsetField = AccessTools.Field(typeof(UsableItem_Beacon), "_instantiationOffset");
			if (offsetField != null)
				offset = (Vector2)offsetField.GetValue(__instance);

			Vector2 pos = (Vector2)Singleton<PlayerController>.Instance.transform.position + offset;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BeaconAction.CreateRequest,
				StringArg = name,
				PosX = pos.x,
				PosY = pos.y
			};

			net.SendToHost(msg.ToNetMessage(MessageType.BeaconSync));

			FieldInfo sfxField = AccessTools.Field(typeof(UsableItem_Beacon), "_sfx");
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

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.SpawnBeacon))]
		[HarmonyPostfix]
		private static void SpawnBeacon_Postfix(PersistentBeacon beacon)
		{
			if (IsApplyingRemoteBeacon)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsHost)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BeaconAction.Create,
				StringArg = $"{beacon.Name}|{beacon.UniqueId}",
				PosX = beacon.PosX,
				PosY = beacon.PosY
			};

			net.Broadcast(msg.ToNetMessage(MessageType.BeaconSync));
		}

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.DespawnBeacon))]
		[HarmonyPrefix]
		private static void DespawnBeacon_Prefix(PersistentBeacon beacon)
		{
			if (IsApplyingRemoteBeacon || beacon == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BeaconAction.Remove,
				StringArg = beacon.UniqueId.ToString()
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.BeaconSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.BeaconSync));
		}

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.RenameBeacon))]
		[HarmonyPostfix]
		private static void RenameBeacon_Postfix(PersistentBeacon beacon, string newName)
		{
			if (IsApplyingRemoteBeacon || beacon == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BeaconAction.Rename,
				StringArg = $"{beacon.UniqueId}|{newName}"
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.BeaconSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.BeaconSync));
		}
	}
}