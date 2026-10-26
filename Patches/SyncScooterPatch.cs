using System;
using System.Reflection;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncScooterPatch
	{
		private static string _currentEquippedScooter = string.Empty;

		[HarmonyPatch(typeof(UsableItem_UnderwaterScooter), "SelectInternal")]
		[HarmonyPostfix]
		private static void Select_Postfix(UsableItem_UnderwaterScooter __instance)
		{
			_currentEquippedScooter = __instance.IsScooterEldritch ? "EldritchScooter" : "Scooter_v1";
			SendScooterState(_currentEquippedScooter, false);
		}

		[HarmonyPatch(typeof(UsableItem_UnderwaterScooter), "DeselectInternal")]
		[HarmonyPostfix]
		private static void Deselect_Postfix()
		{
			_currentEquippedScooter = string.Empty;
			SendScooterState(string.Empty, false);
		}

		[HarmonyPatch(typeof(UsableItem_UnderwaterScooter), "UseInternal")]
		[HarmonyPostfix]
		private static void Use_Postfix(UsableItem_UnderwaterScooter __instance)
		{
			bool isActive = (bool)(AccessTools.Field(typeof(UsableItem_UnderwaterScooter), "_isScooterActive")?.GetValue(__instance) ?? false);
			SendScooterState(_currentEquippedScooter, isActive);
		}

		[HarmonyPatch(typeof(UsableItem_UnderwaterScooter), nameof(UsableItem_UnderwaterScooter.DisableScooter))]
		[HarmonyPostfix]
		private static void DisableScooter_Postfix()
		{
			SendScooterState(_currentEquippedScooter, false);
		}

		private static void SendScooterState(string attachmentId, bool isActive)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				StringArg = attachmentId ?? string.Empty,
				IntArg1 = isActive ? 1 : 0,
				IntArg2 = net.IsHost ? PeerInfo.HostPeerId : 1
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.ScooterSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.ScooterSync));
		}

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			if (NetworkManager.Instance.IsHost)
			{
				msg.IntArg2 = peerId;
				SetRemoteScooter(peerId, msg.StringArg, msg.IntArg1 == 1);
				NetworkManager.Instance.Broadcast(msg.ToNetMessage(MessageType.ScooterSync), peerId);
			}
			else
			{
				SetRemoteScooter(msg.IntArg2, msg.StringArg, msg.IntArg1 == 1);
			}
		}

		public static void SetRemoteScooter(int peerId, string attachmentId, bool isActive = false)
		{
			RemoteDiverController remote = DiveSyncManager.Instance?.GetRemoteDiver(peerId);
			if (remote == null)
				return;

			try
			{
				Type skeletonType = Type.GetType("Spine.Unity.SkeletonAnimation, spine-unity") ?? Type.GetType("Spine.Unity.SkeletonAnimation, Assembly-CSharp");
				Component skeletonAnim = remote.GetComponentInChildren(skeletonType, true);

				if (skeletonAnim == null)
					return;

				PropertyInfo skelProp = skeletonAnim.GetType().GetProperty("Skeleton");
				object skel = skelProp?.GetValue(skeletonAnim);

				if (skel == null)
					return;

				MethodInfo setAtt = skel.GetType().GetMethod("SetAttachment", new Type[] { typeof(string), typeof(string) });
				setAtt?.Invoke(skel, new object[] { "EldritchScooter", string.IsNullOrEmpty(attachmentId) ? null : attachmentId });
			}
			catch { }
		}
	}
}