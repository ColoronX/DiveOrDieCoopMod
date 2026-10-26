using System.Collections.Generic;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class InteractionTriggerPatch
	{
		[HarmonyPatch(typeof(DivingInteractionManager), "Update")]
		[HarmonyPrefix]
		private static void DivingInteractionManager_Update_Prefix(DivingInteractionManager __instance)
		{
			if (!Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null) return;

			Vector3 playerPos = Singleton<PlayerController>.Instance.transform.position;
			List<IInteractable> list = AccessTools.Field(typeof(DivingInteractionManager), "_interactableObjectsInRange")?.GetValue(__instance) as List<IInteractable>;

			if (list != null && list.Count > 0)
			{
				IInteractable closest = null;
				float minDistance = float.MaxValue;

				for (int i = list.Count - 1; i >= 0; i--)
				{
					IInteractable item = list[i];
					if (item == null || (item is UnityEngine.Object uObj && uObj == null))
					{
						list.RemoveAt(i);
						continue;
					}

					if (item is Component comp && comp != null && comp.gameObject.activeInHierarchy)
					{
						float dist = Vector3.Distance(comp.transform.position, playerPos);
						if (dist > 2.2f)
						{
							item.SetOutOfRange();
							list.RemoveAt(i);
							continue;
						}

						if (dist < minDistance)
						{
							minDistance = dist;
							closest = item;
						}
					}
				}

				if (closest != null && closest != __instance.ActiveInteraction)
				{
					if (__instance.ActiveInteraction != null)
					{
						__instance.SetActiveInteractable(__instance.ActiveInteraction, false);
					}
					__instance.SetActiveInteractable(closest, true);
				}
			}
		}

		[HarmonyPatch(typeof(InteractionTrigger), "OnTriggerEnter2D")]
		[HarmonyPrefix]
		private static bool OnTriggerEnter2D_Prefix(Collider2D collision)
		{
			if (collision == null || !Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null) return false;
			return collision.GetComponentInParent<PlayerController>() == Singleton<PlayerController>.Instance;
		}

		[HarmonyPatch(typeof(InteractionTrigger), "OnTriggerExit2D")]
		[HarmonyPrefix]
		private static bool OnTriggerExit2D_Prefix(Collider2D other)
		{
			if (other == null || !Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null) return false;
			return other.GetComponentInParent<PlayerController>() == Singleton<PlayerController>.Instance;
		}
	}
}

