using System.Collections.Generic;
using DRS.Collections;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class GrabTeleportFix
	{
		[HarmonyPatch(typeof(SpiderChasingState), "UpdateState")]
		[HarmonyPrefix]
		private static void Spider_Chasing_GrabCheck_Prefix(SpiderChasingState __instance)
		{
			if (!Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null) return;

			EntityTriggerDetectionBehaviour grabTrigger = AccessTools.Field(typeof(SpiderChasingState), "_grabTrigger")?.GetValue(__instance) as EntityTriggerDetectionBehaviour;
			if (grabTrigger != null && grabTrigger.Entities.Count > 0)
			{
				grabTrigger.Entities.RemoveAll(t => t == null || t.name.StartsWith("DiveOrDieCoop.RemoteDiver"));
			}
		}

		[HarmonyPatch(typeof(SpiderLatchState), "EnterState")]
		[HarmonyPrefix]
		private static bool Spider_Latch_EnterState_Prefix(SpiderLatchState __instance, ref bool __result)
		{
			if (!Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null)
			{
				__result = false;
				return false;
			}

			float dist = Vector3.Distance(__instance.transform.position, Singleton<PlayerController>.Instance.transform.position);
			if (dist > 2.0f)
			{
				SpiderController spider = AccessTools.Field(typeof(SpiderLatchState), "_spiderController")?.GetValue(__instance) as SpiderController;
				spider?.ChangeState(AIEntityControllerStateType.ROAM);
				__result = false;
				return false;
			}

			return true;
		}

		[HarmonyPatch(typeof(SpiderLatchState), "UpdateState")]
		[HarmonyPrefix]
		private static bool Spider_Latch_UpdateState_Prefix(SpiderLatchState __instance)
		{
			if (!Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null) return false;

			float dist = Vector3.Distance(__instance.transform.position, Singleton<PlayerController>.Instance.transform.position);
			if (dist > 3.0f)
			{
				return false;
			}

			return true;
		}
	}
}

