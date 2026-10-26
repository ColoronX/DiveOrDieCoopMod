using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Hub;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncSkipDayPatch
	{
		public static bool IsApplyingRemoteSkip = false;

		[HarmonyPatch(typeof(NetworkManager), "Awake")]
		[HarmonyPostfix]
		private static void NetworkManager_Awake_Postfix(NetworkManager __instance)
		{
			__instance.MessageReceived += OnMessageReceived;
		}

		private static void OnMessageReceived(int peerId, NetMessage message)
		{
			if (message.Type != MessageType.DiveStateSync) return;
			var msg = DiveStateSyncMessage.FromPayload(message.Payload);

			if (msg.IntArg1 == -1100)
			{
				if (NetworkManager.Instance.IsHost)
				{
					NetworkManager.Instance.Broadcast(message, peerId);
				}

				IsApplyingRemoteSkip = true;
				try
				{
					
					var camp = Singleton<GameState>.Instance?.CampState;
					if (camp != null)
					{
						foreach (var s in camp.Survivors)
						{
							s?.ResetLastGainedExperience();
						}
					}

					var prog = Singleton<GameState>.Instance?.ProgressionState;
					if (prog != null)
					{
						prog.skippedDay = true;
					}

					if (Singleton<GameFlowManager>.HasInstance())
					{
						Singleton<GameFlowManager>.Instance.ReleaseAllLockLevels();
					}

					Time.timeScale = 1f;

					if (Singleton<SceneLoader>.HasInstance())
					{
						Singleton<SceneLoader>.Instance.LoadScene(SceneLoader.eScene.Hub, false, true, false);
					}
				}
				finally
				{
					IsApplyingRemoteSkip = false;
				}
			}
		}

		[HarmonyPatch(typeof(SkipDay), "ConfirmSkipDay")]
		[HarmonyPrefix]
		private static bool ConfirmSkipDay_Prefix()
		{
			if (IsApplyingRemoteSkip) return true;

			if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
			{
				var msg = new DiveStateSyncMessage
				{
					Action = DiveStateAction.AddEventCount,
					IntArg1 = -1100
				};

				if (!NetworkManager.Instance.IsHost)
				{
					NetworkManager.Instance.SendToHost(msg.ToNetMessage());
					return false; 
				}
				else
				{
					NetworkManager.Instance.Broadcast(msg.ToNetMessage());
					return true;
				}
			}

			return true;
		}
	}
}

