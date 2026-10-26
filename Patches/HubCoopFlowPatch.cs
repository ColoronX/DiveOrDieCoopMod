using System;
using System.Collections;
using System.Reflection;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DRS.Coroutines;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using ED.Hub;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class HubCoopFlowPatch
	{
		[HarmonyPatch(typeof(HubOrchestrator), "AfterDivePreProcessor")]
		[HarmonyPrefix]
		private static bool AfterDivePreProcessor_Prefix(HubOrchestrator __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && net.IsConnected && !net.IsHost)
			{
				var menuManager = AccessTools.Field(typeof(HubOrchestrator), "_menuManager")?.GetValue(__instance);
				menuManager?.GetType().GetMethod("ToggleMenuButtonSelectables")?.Invoke(menuManager, new object[] { false, false });
				
				EventSystem.current.SetSelectedGameObject(null);
				Singleton<SkipDay>.Instance.HideSkipDay();
				
				Action displayCampReport = () => AccessTools.Method(typeof(HubOrchestrator), "DisplayCampReport").Invoke(__instance, null);
				DivingReport report = AccessTools.Field(typeof(HubOrchestrator), "_divingReport")?.GetValue(__instance) as DivingReport;
				report?.Display(displayCampReport);
				
				return false;
			}
			return true;
		}

		[HarmonyPatch(typeof(HubOrchestrator), "AfterDivePreProcessor")]
		[HarmonyPostfix]
		private static void AfterDivePreProcessor_Postfix(HubOrchestrator __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && net.IsConnected && net.IsHost)
			{
				foreach (var claim in DiveTransitionManager.Instance.PeerSurvivorClaims)
				{
					if (claim.Key == PeerInfo.HostPeerId) continue;
					
					string survivorId = claim.Value.Split('|')[0];
					SurvivorData survivor = Singleton<GameState>.Instance.CampState.GetSurvivor(survivorId);
					if (survivor != null && !survivor.HasCondition("Missing") && !survivor.HasCondition("Dead"))
					{
						survivor.DiveCount++;
						if (survivor.HasTrait("MasterDiver"))
						{
							survivor.GetAttribute(EntityAttributeID.OXYGEN).AddAdditiveModifier("MasterDiver", 5000, ED.Basics.Attribute.RatioHandling.KeepRatio, ED.Basics.Attribute.AttributeMethod.STACK);
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(HubOrchestrator), "DisplayCampReport")]
		[HarmonyPrefix]
		private static bool DisplayCampReport_Prefix(HubOrchestrator __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && net.IsConnected && !net.IsHost)
			{
				if (!HubSyncManager.Instance.HasReceivedPostDiveSync)
				{
					Singleton<CoroutineManager>.Instance.StartCoroutine(WaitForSync(__instance));
					return false;
				}
			}
			return true;
		}

		private static IEnumerator WaitForSync(HubOrchestrator orchestrator)
		{
			yield return new WaitUntil(() => HubSyncManager.Instance.HasReceivedPostDiveSync);
			AccessTools.Method(typeof(HubOrchestrator), "DisplayCampReport").Invoke(orchestrator, null);
		}

		[HarmonyPatch(typeof(HubOrchestrator), "SkipDayPreProcessor")]
		[HarmonyPrefix]
		private static bool SkipDayPreProcessor_Prefix(HubOrchestrator __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && net.IsConnected && !net.IsHost)
			{
				var menuManager = AccessTools.Field(typeof(HubOrchestrator), "_menuManager")?.GetValue(__instance);
				menuManager?.GetType().GetMethod("ToggleMenuButtonSelectables")?.Invoke(menuManager, new object[] { false, false });
				
				EventSystem.current.SetSelectedGameObject(null);
				Singleton<SkipDay>.Instance.HideSkipDay();
				
				AccessTools.Method(typeof(HubOrchestrator), "DisplayCampReport").Invoke(__instance, null);
				return false;
			}
			return true;
		}
	}
}

