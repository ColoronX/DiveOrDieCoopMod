using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Networking;
using DRS.Patterns;
using ED.Diving;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class GravekeeperSpectatorPatch
	{
		[HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), new System.Type[] { typeof(SceneLoader.eScene), typeof(bool), typeof(bool), typeof(bool) })]
		[HarmonyPrefix]
		private static bool SceneLoader_LoadScene_Prefix(SceneLoader.eScene scene)
		{
			if (scene == SceneLoader.eScene.Hub && SceneManager.GetActiveScene().name == "_Diving")
			{
				NetworkManager net = NetworkManager.Instance;
				if (net != null && net.IsConnected && net.ConnectedPeers.Count > 0)
				{
					if (DiveTransitionManager.Instance != null && !DiveTransitionManager.Instance.IsPerformingFinalOutro)
					{
						DiveTransitionManager.Instance.OnLocalPlayerDied();
						return false;
					}
				}
			}
			return true;
		}
	}
}

