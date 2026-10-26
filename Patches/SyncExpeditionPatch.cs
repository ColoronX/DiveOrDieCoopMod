using System;
using System.Collections.Generic;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using ED.Hub;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch(typeof(MenuLighthouse))]
	public static class MenuLighthousePatch
	{
		[HarmonyPatch(nameof(MenuLighthouse.CalculateExpeditionChance))]
		[HarmonyPrefix]
		private static bool CalculateExpeditionChance_Prefix(Expedition expedition, ref int __result)
		{
			if (expedition == null || expedition.Participants == null || expedition.Participants.Count == 0)
			{
				__result = 0;
				return false;
			}

			int baseBonus = 0;
			int statBonus = 0;

			var camp = Singleton<GameState>.Instance?.CampState;
			if (camp == null)
			{
				__result = 0;
				return false;
			}

			foreach (string survivorId in expedition.Participants)
			{
				SurvivorData survivorData = camp.Survivors.Find(x => x != null && x.Id == survivorId) 
				                         ?? camp.VisitingSurvivors.Find(x => x != null && x.Id == survivorId)
				                         ?? camp.ExpeditionSurvivors.Find(x => x != null && x.Id == survivorId);

				if (survivorData != null)
				{
					statBonus += survivorData.GetAttribute(EntityAttributeID.SURVIVOR_EXPEDITION_CHANCE_MOD).Value;
					
					if (survivorData.HasCondition("GuidingSpirit"))
					{
						statBonus += 15;
					}

					if (survivorData.HasCondition("Insane"))
					{
						statBonus -= 10;
					}
					else
					{
						baseBonus += survivorData.Level;
					}
				}
			}

			int darkArtsLvl = camp.GetAttributeValue(CampAttributeID.DARK_ARTS_LEVEL);
			int idolBonus = 0;

			if (darkArtsLvl > 0 && camp.ActiveIdols != null && camp.ActiveIdols.Contains("IdolOfLoss"))
			{
				Type constType = AccessTools.TypeByName("Const") ?? AccessTools.TypeByName("ED.Hub.Const") ?? AccessTools.TypeByName("ED.Basics.Const");
				if (constType != null)
				{
					int[] idolLossArr = AccessTools.Field(constType, "IDOL_LOSS_DARK_ART")?.GetValue(null) as int[];
					int[] idolLossMaxArr = AccessTools.Field(constType, "IDOL_LOSS_DARK_ART_MAX")?.GetValue(null) as int[];

					if (idolLossArr != null && idolLossMaxArr != null && darkArtsLvl < idolLossArr.Length && darkArtsLvl < idolLossMaxArr.Length)
					{
						int deadCount = camp.DeadSurvivors != null ? camp.DeadSurvivors.Count : 0;
						idolBonus = Mathf.Clamp(deadCount * idolLossArr[darkArtsLvl], 0, idolLossMaxArr[darkArtsLvl]);
					}
				}
			}

			int globalMod = camp.GetAttributeValue(CampAttributeID.EXPEDITION_CHANCE_MOD);

			__result = Mathf.Clamp(expedition.Data.BaseChance + (baseBonus * expedition.Data.AddedChancePerSurvivorLevel) + statBonus + globalMod + idolBonus, 0, 100);

			return false;
		}
	}
}

