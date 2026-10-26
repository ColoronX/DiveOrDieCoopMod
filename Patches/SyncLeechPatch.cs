using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class LeechPatch
	{
		[HarmonyPatch(typeof(LeechLatchState), "EnterState")]
		[HarmonyPrefix]
		private static bool Leech_EnterState_Safe_Prefix(LeechLatchState __instance, ref bool __result)
		{
			AIEntityController controller = AccessTools.Field(typeof(AIEntityControllerState), "_controller")?.GetValue(__instance) as AIEntityController;
			if (controller == null || controller.EntityDetection == null || controller.EntityDetection.Entities.Count == 0)
			{
				controller?.ChangeState(AIEntityControllerStateType.CHASE);
				__result = false;
				return false;
			}

			Transform target = controller.EntityDetection.Entities[0];
			if (target == null)
			{
				controller.ChangeState(AIEntityControllerStateType.CHASE);
				__result = false;
				return false;
			}

			if (target.name.StartsWith("DiveOrDieCoop.RemoteDiver"))
			{
				controller.ChangeState(AIEntityControllerStateType.ROAM);
				__result = false;
				return false;
			}

			PlayerController pc = target.GetComponentInParent<PlayerController>();
			if (pc == null)
			{
				controller.ChangeState(AIEntityControllerStateType.CHASE);
				__result = false;
				return false;
			}

			AccessTools.Field(typeof(LeechLatchState), "_playerController")?.SetValue(__instance, pc);
			return true;
		}
	}
}

