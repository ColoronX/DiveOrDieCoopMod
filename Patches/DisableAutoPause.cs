using DRS.Patterns;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class DisableAutoPausePatch
	{
		[HarmonyPatch(typeof(GameFlowManager), "Start")]
		[HarmonyPostfix]
		private static void GameFlowManager_Start_Postfix()
		{
			Application.runInBackground = true;
		}

		[HarmonyPatch(typeof(PauseMenu), nameof(PauseMenu.Display))]
		[HarmonyPrefix]
		private static bool PauseMenu_Display_Prefix()
		{
			if (!Application.isFocused)
			{
				PlayerController pc = Singleton<PlayerController>.HasInstance() ? Singleton<PlayerController>.Instance : null;
				bool isManualPausePressed = false;

				try
				{
					if (pc != null && pc.PlayerInput != null && pc.PlayerInput.actions != null)
					{
						isManualPausePressed = pc.PlayerInput.actions["Pause"].WasPressedThisFrame();
					}
				}
				catch { }

				if (!isManualPausePressed)
				{
					return false;
				}
			}

			return true;
		}
	}
}

