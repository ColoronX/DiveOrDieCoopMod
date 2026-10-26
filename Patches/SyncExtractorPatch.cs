using System;
using System.Reflection;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using ED.Hub;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncExtractorPatch
	{
		private enum ExtractorAction
		{
			CreateRequest = 1,
			Create = 2,
			Remove = 3
		}

		public static bool IsApplyingRemoteExtractor;

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);
			Vector2 position = new Vector2(msg.PosX, msg.PosY);

			switch ((ExtractorAction)msg.IntArg1)
			{
				case ExtractorAction.CreateRequest:
					if (!NetworkManager.Instance.IsHost)
						return;

					GameState gs = Singleton<GameState>.Instance;
					if (gs?.PoolState == null)
						return;

					int maxExt = gs.CampState.GetAttributeValue(CampAttributeID.MAX_EXTRACTORS);
					if (gs.PoolState.PersistentExtractorData.Count >= maxExt)
						return;

					ExtractorLocationTrigger trigger = FindTriggerNear(position);
					if (trigger?.Data == null)
						return;

					Singleton<PersistentPoolItemManager>.Instance.SpawnNewExtractor(trigger.Data, position);
					break;

				case ExtractorAction.Create:
					if (NetworkManager.Instance.IsHost)
						return;

					ApplyCreate(position);
					break;

				case ExtractorAction.Remove:
					ApplyRemove(position);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;
			}
		}

		private static void ApplyCreate(Vector2 position)
		{
			ExtractorLocationTrigger trigger = FindTriggerNear(position);
			if (trigger?.Data == null)
				return;

			ExtractorData existing = FindExtractorDataAt(position);
			if (existing != null)
				return;

			IsApplyingRemoteExtractor = true;

			try
			{
				Singleton<PersistentPoolItemManager>.Instance.SpawnNewExtractor(trigger.Data, position);
			}
			finally
			{
				IsApplyingRemoteExtractor = false;
			}
		}

		private static void ApplyRemove(Vector2 position)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return;

			ExtractorData target = FindExtractorDataAt(position);
			if (target == null)
				return;

			IsApplyingRemoteExtractor = true;

			try
			{
				gs.PoolState.RemovePersistentExtractor(target);

				if (target.ExtractorObject != null)
				{
					if (target.ExtractorObject.transform.parent != null)
						UnityEngine.Object.Destroy(target.ExtractorObject.transform.parent.gameObject);
					else
						UnityEngine.Object.Destroy(target.ExtractorObject);
				}
			}
			finally
			{
				IsApplyingRemoteExtractor = false;
			}
		}

		private static ExtractorLocationTrigger FindTriggerNear(Vector2 position)
		{
			ExtractorLocationTrigger closest = null;
			float minDistance = float.MaxValue;

			foreach (ExtractorLocationTrigger trigger in UnityEngine.Object.FindObjectsOfType<ExtractorLocationTrigger>())
			{
				if (trigger == null)
					continue;

				Collider2D col = trigger.GetComponent<Collider2D>();
				if (col != null && col.OverlapPoint(position))
					return trigger;

				float dist = Vector2.Distance(trigger.transform.position, position);
				if (dist < minDistance)
				{
					minDistance = dist;
					closest = trigger;
				}
			}

			return minDistance <= 35f ? closest : null;
		}

		private static ExtractorData FindExtractorDataAt(Vector2 position)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState?.PersistentExtractorData == null)
				return null;

			foreach (ExtractorData extractor in gs.PoolState.PersistentExtractorData)
			{
				Vector2 extractorPos = new Vector2(extractor.Position.x, extractor.Position.y);

				if (Vector2.Distance(extractorPos, position) < 0.2f)
					return extractor;
			}

			return null;
		}

		[HarmonyPatch(typeof(UsableItem_Extractor), "Update")]
		[HarmonyPrefix]
		private static bool Update_Guard_Prefix(UsableItem_Extractor __instance)
		{
			PlayerLocation loc = AccessTools.Field(typeof(UsableItem_Extractor), "_playerLocation")?.GetValue(__instance) as PlayerLocation;

			if (loc == null && Singleton<PlayerController>.HasInstance() && Singleton<PlayerController>.Instance != null)
			{
				loc = Singleton<PlayerController>.Instance.GetSubSystem<PlayerLocation>();
				AccessTools.Field(typeof(UsableItem_Extractor), "_playerLocation")?.SetValue(__instance, loc);
			}

			return loc != null;
		}

		[HarmonyPatch(typeof(UsableItem_Extractor), "UseInternal")]
		[HarmonyPrefix]
		private static bool UseInternal_Extractor_Prefix(UsableItem_Extractor __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || net.IsHost)
				return true;

			GameState gs = Singleton<GameState>.Instance;
			int maxExt = gs.CampState.GetAttributeValue(CampAttributeID.MAX_EXTRACTORS);

			if (gs.PoolState.PersistentExtractorData.Count >= maxExt)
			{
				Singleton<WidgetErrorNotification>.Instance.DisplayNotification(
					Singleton<LocUtility>.Instance.GetLocStringText("notification_extractorLimit").Replace("{max}", maxExt.ToString()),
					3f, true, false);
				return false;
			}

			PlayerLocation playerLocation = AccessTools.Field(typeof(UsableItem_Extractor), "_playerLocation")?.GetValue(__instance) as PlayerLocation;
			ExtractorLocationTrigger trigger = playerLocation?.GetCurrentExtractionTrigger();

			if (trigger == null || trigger.Data == null)
			{
				Singleton<WidgetErrorNotification>.Instance.DisplayNotification(
					Singleton<LocUtility>.Instance.GetLocStringText("notification_extractorNoResources"),
					3f, true, false);
				return false;
			}

			Vector3 pos = Singleton<PlayerController>.Instance.transform.position;

			net.SendToHost(new DiveStateSyncMessage
			{
				IntArg1 = (int)ExtractorAction.CreateRequest,
				PosX = pos.x,
				PosY = pos.y
			}.ToNetMessage(MessageType.ExtractorSync));

			FieldInfo sfxField = AccessTools.Field(typeof(UsableItem_Extractor), "_sfx");

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

			FieldInfo invField = AccessTools.Field(typeof(UsableItem_Extractor), "_playerInventory");
			DivingInventoryUI inventory = invField?.GetValue(__instance) as DivingInventoryUI;

			if (inventory != null)
				inventory.RemoveItems(__instance.Data.Id, 1);

			FieldInfo widgetIdField = AccessTools.Field(typeof(UsableItem_Extractor), "_currentWidgetId");

			if (widgetIdField != null)
			{
				int widgetId = (int)widgetIdField.GetValue(__instance);

				if (widgetId != -1)
				{
					Singleton<DivingUIOrchestrator>.Instance.GetWidget<DivingUIExtractorResourcesManager>().Hide(widgetId);
					widgetIdField.SetValue(__instance, -1);
				}
			}

			return false;
		}

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.SpawnNewExtractor))]
		[HarmonyPostfix]
		private static void SpawnNewExtractor_Postfix(ExtractorResourcesData resourcesData, Vector2 position, CollectibleObject __result)
		{
			if (IsApplyingRemoteExtractor)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsHost)
				return;

			net.Broadcast(new DiveStateSyncMessage
			{
				IntArg1 = (int)ExtractorAction.Create,
				PosX = position.x,
				PosY = position.y
			}.ToNetMessage(MessageType.ExtractorSync));
		}

		[HarmonyPatch(typeof(PoolState), nameof(PoolState.RemovePersistentExtractor))]
		[HarmonyPrefix]
		private static void RemovePersistentExtractor_Prefix(ExtractorData extractorData)
		{
			if (IsApplyingRemoteExtractor || extractorData == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)ExtractorAction.Remove,
				PosX = extractorData.Position.x,
				PosY = extractorData.Position.y
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.ExtractorSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.ExtractorSync));
		}
	}
}