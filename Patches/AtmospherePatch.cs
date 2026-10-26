using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class AtmospherePatch
	{
		[HarmonyPatch(typeof(AtmosphereAdjuster), "OnTriggerEnter2D")]
		[HarmonyPrefix]
		private static bool OnTriggerEnter2D_Prefix(Collider2D collision)
		{
			if (collision == null) return true;

			if (Singleton<PlayerController>.HasInstance() && Singleton<PlayerController>.Instance != null)
			{
				if (collision.GetComponentInParent<PlayerController>() != Singleton<PlayerController>.Instance)
				{
					return false;
				}
			}
			return true;
		}

		[HarmonyPatch(typeof(AtmosphereAdjuster), "OnTriggerExit2D")]
		[HarmonyPrefix]
		private static bool OnTriggerExit2D_Prefix(Collider2D collision)
		{
			if (collision == null) return true;

			if (Singleton<PlayerController>.HasInstance() && Singleton<PlayerController>.Instance != null)
			{
				if (collision.GetComponentInParent<PlayerController>() != Singleton<PlayerController>.Instance)
				{
					return false;
				}
			}
			return true;
		}
	}
}

