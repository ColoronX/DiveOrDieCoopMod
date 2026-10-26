using System.Collections.Generic;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Networking;
using HarmonyLib;
using TMPro;
using ED.Hub;
using UnityEngine;
using UnityEngine.UI;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch(typeof(MenuDivingDeck))]
	public static class MenuDivingDeckPatches
	{
		[HarmonyPatch("UpdateSurvivorList")]
		[HarmonyPostfix]
		private static void UpdateSurvivorList_Postfix(MenuDivingDeck __instance)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || net.ConnectedPeers.Count == 0)
				return;

			List<SurvivorPortrait> portraits =
				AccessTools.Field(typeof(MenuDivingDeck), "_survivorPortraits")?.GetValue(__instance) as List<SurvivorPortrait>;

			if (portraits == null)
				return;

			foreach (SurvivorPortrait portrait in portraits)
			{
				if (portrait?.GetData() == null)
					continue;

				if (DiveTransitionManager.Instance != null &&
					DiveTransitionManager.Instance.IsSurvivorClaimedByTeammate(portrait.GetData().Id))
				{
					portrait.SetDesatMat(false);
				}
			}
		}

		[HarmonyPatch("UpdateSelectedSurvivor")]
		[HarmonyPostfix]
		private static void UpdateSelectedSurvivor_Postfix(MenuDivingDeck __instance)
		{
			NetworkManager net = NetworkManager.Instance;

			if (net == null || !net.IsConnected || net.ConnectedPeers.Count == 0)
				return;

			Button diveBtn =
				AccessTools.Field(typeof(MenuDivingDeck), "_diveButton")?.GetValue(__instance) as Button;

			GameObject cannotDiveGo =
				AccessTools.Field(typeof(MenuDivingDeck), "_cannotDiveGo")?.GetValue(__instance) as GameObject;

			TextMeshProUGUI cannotDiveTxt =
				AccessTools.Field(typeof(MenuDivingDeck), "_cannotDiveTxt")?.GetValue(__instance) as TextMeshProUGUI;

			SurvivorPortrait selectedPortrait =
				AccessTools.Field(typeof(MenuDivingDeck), "_selectedSurvivorPortrait")?.GetValue(__instance) as SurvivorPortrait;

			if (diveBtn == null || selectedPortrait?.GetData() == null)
				return;

			SurvivorData selectedSurvivor = selectedPortrait.GetData();
			TextMeshProUGUI btnTxt = diveBtn.GetComponentInChildren<TextMeshProUGUI>();

			DiveTransitionManager transition = DiveTransitionManager.Instance;

			if (transition != null &&
				transition.IsSurvivorClaimedByTeammate(selectedSurvivor.Id))
			{
				diveBtn.gameObject.SetActive(false);

				if (cannotDiveGo != null)
					cannotDiveGo.SetActive(true);

				if (cannotDiveTxt != null)
					cannotDiveTxt.text = "CLAIMED BY TEAMMATE";

				return;
			}

			if (!net.IsHost)
			{
				bool isReady = transition != null && transition.IsLocalReady;

				diveBtn.interactable = true;
				diveBtn.gameObject.SetActive(true);

				if (cannotDiveGo != null)
					cannotDiveGo.SetActive(false);

				if (btnTxt != null)
					btnTxt.text = isReady ? "UNREADY" : "READY UP";

				return;
			}

			int readyCount = 1;
			int totalPlayers = net.ConnectedPeers.Count + 1;

			if (transition != null)
			{
				foreach (var peer in net.ConnectedPeers)
				{
					if (transition.PeerReadyStates.TryGetValue(peer.Key, out bool ready) && ready)
						readyCount++;
				}
			}

			bool allReady = readyCount >= totalPlayers;

			diveBtn.interactable = allReady;
			diveBtn.gameObject.SetActive(true);

			if (cannotDiveGo != null)
				cannotDiveGo.SetActive(false);

			if (btnTxt != null)
			{
				btnTxt.text = allReady
					? "START EXPEDITION"
					: $"WAITING ({readyCount}/{totalPlayers} READY)";
			}
		}

		[HarmonyPatch("DiveButton")]
		[HarmonyPrefix]
		private static bool DiveButton_Prefix(MenuDivingDeck __instance)
		{
			NetworkManager net = NetworkManager.Instance;

			if (net == null || !net.IsConnected || net.ConnectedPeers.Count == 0)
				return true;

			SurvivorPortrait selectedPortrait =
				AccessTools.Field(typeof(MenuDivingDeck), "_selectedSurvivorPortrait")?.GetValue(__instance) as SurvivorPortrait;

			if (selectedPortrait?.GetData() == null)
				return false;

			SurvivorData selectedSurvivor = selectedPortrait.GetData();
			DiveTransitionManager transition = DiveTransitionManager.Instance;

			if (transition == null)
				return false;

			if (transition.IsSurvivorClaimedByTeammate(selectedSurvivor.Id))
				return false;

			if (!net.IsHost)
			{
				/*
				 * IMPORTANT:
				 * The client NEVER calls the native DiveButton implementation.
				 *
				 * The current Hub selection remains intact, including the
				 * client's selected surface/location. ToggleClientReady only
				 * transmits staging information. It does NOT load _Diving.
				 */
				transition.LocalClaimedSurvivor = selectedSurvivor;
				transition.ToggleClientReady(selectedSurvivor.Id);

				AccessTools.Method(
					typeof(MenuDivingDeck),
					"UpdateSelectedSurvivor")?.Invoke(__instance, null);

				return false;
			}

			if (!transition.AreAllPeersReady())
				return false;

			transition.LocalClaimedSurvivor = selectedSurvivor;
			transition.ClaimedSurvivorId = selectedSurvivor.Id;
			transition.PeerSurvivorClaims[PeerInfo.HostPeerId] =
				$"{selectedSurvivor.Id}|{selectedSurvivor.GraphicsData?.SkinId ?? string.Empty}";

			/*
			 * Only the HOST gets here.
			 *
			 * The host's currently selected Hub surface/location is used as
			 * the authoritative expedition location.
			 */
			transition.HostLaunchSynchronizedDive();
			return false;
		}
	}
}