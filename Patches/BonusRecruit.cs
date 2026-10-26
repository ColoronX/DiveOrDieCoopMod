using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DiveOrDieCoop.Networking;
using DRS.Patterns;
using ED.Hub;
using HarmonyLib;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch(typeof(HubOrchestrator), "UpdateVisitingSurvivors")]
	public static class BonusRecruitsPatch
	{
		private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".Recruits");

		private static void Postfix()
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || !net.IsHost)
			{
				return;
			}

			CampState camp = Singleton<GameState>.Instance?.CampState;
			if (camp == null || !Singleton<SurvivorGenerator>.HasInstance())
			{
				return;
			}

			int connectedPeers = net.ConnectedPeers.Count;
			if (connectedPeers <= 0)
			{
				return;
			}

			SurvivorData manningSurvivor = camp.GetManningSurvivor("LIGHTHOUSE");
			int num = 0;
			int num2 = 0;

			Type constType = AccessTools.TypeByName("Const") ?? typeof(SurvivorData).Assembly.GetType("Const");
			int[] levelExp = constType != null ? AccessTools.Field(constType, "LEVEL_EXP")?.GetValue(null) as int[] : null;
			int[] idolJudgementBonus = constType != null ? AccessTools.Field(constType, "IDOL_JUDGEMENT_BONUS_LEVELS")?.GetValue(null) as int[] : null;
			MethodInfo addExpMethod = AccessTools.Method(typeof(SurvivorData), "AddExperience", new Type[] { typeof(int), typeof(bool) });

			for (int i = 0; i < connectedPeers; i++)
			{
				int num3 = UnityEngine.Random.Range(0, 100 + num + num2 + 1);
				int num4 = 0;
				if (num3 > 100 && num3 <= 100 + num) num4 = 1;
				else if (num3 > 100 + num) num4 = 2;

				SurvivorData survivorData = Singleton<SurvivorGenerator>.Instance.GenerateSurvivor(num4, null, null);
				if (survivorData == null) continue;

				if (manningSurvivor != null && manningSurvivor.HasTrait("BlessedHost"))
				{
					foreach (SurvivorTraitData survivorTraitData in survivorData.TraitDatas.Where((SurvivorTraitData x) => x.Type == SurvivorTraitData.TraitType.Negative).ToArray())
					{
						survivorData.RemoveTrait(survivorTraitData);
					}
				}

				int num5 = camp.GetAttributeValue(CampAttributeID.RECRUITS_BONUS_LEVEL);
				if (camp.GetAttributeValue(CampAttributeID.DARK_ARTS_LEVEL) > 0 && 
				    camp.ActiveIdols != null && camp.ActiveIdols.Contains("IdolOfJudgment") && 
				    idolJudgementBonus != null)
				{
					int darkArtsIdx = camp.GetAttributeValue(CampAttributeID.DARK_ARTS_LEVEL) - 1;
					if (darkArtsIdx >= 0 && darkArtsIdx < idolJudgementBonus.Length)
					{
						num5 += idolJudgementBonus[darkArtsIdx];
					}
				}

				if (num5 > 0 && levelExp != null && addExpMethod != null)
				{
					for (int l = 0; l < num5; l++)
					{
						int expIndex = survivorData.Level + l - 1;
						if (expIndex >= 0 && expIndex < levelExp.Length)
						{
							addExpMethod.Invoke(survivorData, new object[] { levelExp[expIndex], false });
						}
					}
				}

				camp.VisitingSurvivors.Add(survivorData);
			}

			Log.LogInfo($"[BonusRecruits] Generated {connectedPeers} extra daily visiting survivors via exact 1:1 Native Pipeline.");
		}
	}
}

