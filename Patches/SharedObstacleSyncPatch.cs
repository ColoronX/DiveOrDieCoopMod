using System;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using ED.Basics;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SharedObstacleSyncPatch
	{
		public static bool IsApplyingNetworkBreak;

		[HarmonyPatch(typeof(DestructibleDamageProcessor), nameof(DestructibleDamageProcessor.Die))]
		[HarmonyPostfix]
		private static void Destructible_Die_Postfix(DestructibleDamageProcessor __instance)
		{
			if (IsApplyingNetworkBreak || __instance == null)
				return;

			BroadcastBreak(__instance.transform.position);
		}

		[HarmonyPatch(typeof(VineDamageProcessor), nameof(VineDamageProcessor.Die))]
		[HarmonyPostfix]
		private static void Vine_Die_Postfix(VineDamageProcessor __instance)
		{
			if (IsApplyingNetworkBreak || __instance == null)
				return;

			BroadcastBreak(__instance.transform.position);
		}

		[HarmonyPatch(typeof(ProtoWebCoral), "Die")]
		[HarmonyPostfix]
		private static void WebCoral_Die_Postfix(ProtoWebCoral __instance)
		{
			if (IsApplyingNetworkBreak || __instance == null)
				return;

			BroadcastBreak(__instance.transform.position);
		}

		private static void BroadcastBreak(Vector3 pos)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				PosX = pos.x,
				PosY = pos.y
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.ObstacleSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.ObstacleSync));
		}

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);
			ApplyRemoteBreak(msg.PosX, msg.PosY);

			if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
				NetworkManager.Instance.Broadcast(message, peerId);
		}

		public static void ApplyRemoteBreak(float x, float y)
		{
			Vector2 breakPos = new Vector2(x, y);
			IsApplyingNetworkBreak = true;

			try
			{
				foreach (DestructibleDamageProcessor dest in GameObject.FindObjectsOfType<DestructibleDamageProcessor>())
				{
					if (dest != null && Vector2.Distance(dest.transform.position, breakPos) <= 2.5f)
						dest.Die(new DamageSource("SyncedBreak"));
				}

				foreach (VineDamageProcessor vine in GameObject.FindObjectsOfType<VineDamageProcessor>())
				{
					if (vine != null && Vector2.Distance(vine.transform.position, breakPos) <= 2.5f)
						vine.Die(new DamageSource("SyncedBreak"));
				}

				foreach (ProtoWebCoral coral in GameObject.FindObjectsOfType<ProtoWebCoral>())
				{
					if (coral != null && Vector2.Distance(coral.transform.position, breakPos) <= 2.5f)
						coral.Die(new DamageSource("SyncedBreak"));
				}
			}
			catch { }
			finally
			{
				IsApplyingNetworkBreak = false;
			}
		}
	}

	[HarmonyPatch(typeof(ProtoTentacleHurtBox), "OnTriggerEnter2D")]
	internal static class ProtoTentacleHurtBox_OnTriggerEnter2D_Patch
	{
		private static bool Prefix(Collider2D collision)
		{
			if (collision == null)
				return false;

			if (collision.name.StartsWith("DiveOrDieCoop.RemoteDiver") ||
				collision.GetComponentInParent<PlayerController>() == null)
				return false;

			return true;
		}
	}
}