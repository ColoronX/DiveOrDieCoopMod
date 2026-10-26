using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DiveOrDieCoop.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncFlashlightPatch
	{
		private static bool _lastLocalFlashlightState;

		[HarmonyPatch(typeof(PlayerController), "Update")]
		[HarmonyPostfix]
		private static void PlayerController_Update_Postfix(PlayerController __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			bool curState = __instance.IsFlashlightOn();

			if (curState == _lastLocalFlashlightState)
				return;

			_lastLocalFlashlightState = curState;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = curState ? 1 : 0,
				IntArg2 = net.IsHost ? PeerInfo.HostPeerId : 1
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.FlashlightSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.FlashlightSync));
		}

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			if (NetworkManager.Instance.IsHost)
			{
				msg.IntArg2 = peerId;
				SetRemoteFlashlightState(peerId, msg.IntArg1 == 1);
				NetworkManager.Instance.Broadcast(msg.ToNetMessage(MessageType.FlashlightSync), peerId);
			}
			else
			{
				SetRemoteFlashlightState(msg.IntArg2, msg.IntArg1 == 1);
			}
		}

		public static void SetRemoteFlashlightState(int peerId, bool isOn)
		{
			RemoteDiverController remote = DiveSyncManager.Instance?.GetRemoteDiver(peerId);
			if (remote == null)
				return;

			foreach (Behaviour behaviour in remote.GetComponentsInChildren<Behaviour>(true))
			{
				if (behaviour != null && behaviour.GetType().Name.Contains("Light"))
					behaviour.enabled = isOn;
			}
		}
	}
}