using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Collections;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using ED.Hub;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Hub
{
	public sealed class HubActionSyncManager : MonoBehaviour
	{
		public static HubActionSyncManager Instance { get; private set; }
		public bool AllowClientCampControl { get; set; } = true;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this.gameObject);
				return;
			}
			Instance = this;
			this.gameObject.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(this.gameObject);
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".HubActionSync");
		}

		private void Update()
		{
			NetworkManager net = NetworkManager.Instance;
			if (net != null && !ReferenceEquals(net, this._subscribedNetworkManager))
			{
				if (this._subscribedNetworkManager != null)
				{
					this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
				}
				this._subscribedNetworkManager = net;
				this._subscribedNetworkManager.MessageReceived += this.OnMessageReceived;
			}
		}

		public void OnLocalAction(HubActionType type, string arg1 = "", string arg2 = "", string arg3 = "", int intArg = 0)
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || net.IsHost || HubSyncManager.IsApplyingSync)
			{
				return;
			}

			net.SendToHost(new HubActionMessage
			{
				ActionType = type,
				Arg1 = arg1 ?? string.Empty,
				Arg2 = arg2 ?? string.Empty,
				Arg3 = arg3 ?? string.Empty,
				IntArg = intArg
			}.ToNetMessage());

			this._log.LogInfo($"[Client] Replicated Hub Action '{type}' ({arg1}, {arg2}) to host.");
		}

		public void SyncClientCampStateToHost()
		{
			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected || net.IsHost || HubSyncManager.IsApplyingSync)
			{
				return;
			}

			GameState gs = Singleton<GameState>.Instance;
			if (gs == null || gs.CampState == null) return;

			try
			{
				string campJson = JsonConvert.SerializeObject(gs.CampState, Formatting.None, HubSyncManager.JsonSettings);
				string poolJson = gs.PoolState != null ? JsonConvert.SerializeObject(gs.PoolState, Formatting.None, HubSyncManager.JsonSettings) : string.Empty;

				net.SendToHost(new HubStateSyncMessage
				{
					CampStateJson = campJson,
					ProgressionStateJson = string.Empty,
					PoolStateJson = poolJson
				}.ToNetMessage());

				this._log.LogInfo("[Client] CampState synced to Host.");
			}
			catch (Exception ex)
			{
				this._log.LogWarning($"[Client] Failed to push CampState to host: {ex.Message}");
			}
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			NetworkManager net = NetworkManager.Instance;
			if (message.Type != MessageType.HubAction || net == null || !net.IsHost)
			{
				return;
			}

			HubActionMessage action = HubActionMessage.FromPayload(message.Payload);
			CampState camp = Singleton<GameState>.Instance?.CampState;
			if (camp == null) return;

			if (!this.AllowClientCampControl)
			{
				HubSyncManager.Instance?.SendFullStateTo(peerId);
				return;
			}

			this._log.LogInfo($"[Host] Authoritatively executing Hub Action '{action.ActionType}' from peer {peerId}.");

			switch (action.ActionType)
			{
				case HubActionType.SetManningSurvivor:
					camp.SetManningSurvivor(action.Arg1, action.Arg2, false);
					break;

				case HubActionType.RemoveManningSurvivor:
					camp.RemoveManningSurvivor(action.Arg1);
					break;

				case HubActionType.UpgradeStructure:
					this.ExecuteHostUpgradeStructure(camp, action.Arg1, action.IntArg);
					break;

				case HubActionType.AssignBed:
					this.ExecuteHostAssignBed(camp, action.Arg1, action.IntArg);
					break;

				case HubActionType.RemoveBed:
					this.ExecuteHostRemoveBed(camp, action.Arg1);
					break;

				case HubActionType.StartTreatment:
					this.ExecuteHostStartTreatment(camp, action.Arg1, action.Arg2, action.Arg3);
					break;

				case HubActionType.CancelTreatment:
					this.ExecuteHostCancelTreatment(camp, action.Arg1, action.IntArg);
					break;

				case HubActionType.StartExpedition:
					this.ExecuteHostStartExpedition(camp, action.Arg1, action.Arg2, action.IntArg);
					break;

				case HubActionType.CancelExpedition:
					camp.RemoveExpedition(action.IntArg);
					break;

				case HubActionType.RecruitSurvivor:
					this.ExecuteHostRecruitSurvivor(camp, action.Arg1);
					break;

				case HubActionType.DismissSurvivor:
					SurvivorData toDismiss = camp.GetSurvivor(action.Arg1);
					if (toDismiss != null)
					{
						toDismiss.AddCondition(Singleton<GameDatabase>.Instance?.GetClonedData<SurvivorConditionData>("Dismissed"));
						camp.RemoveSurvivor(toDismiss);
						camp.VisitingSurvivors.Add(toDismiss);
						toDismiss.RemoveAllEquipment();
					}
					break;

				case HubActionType.ThrowAtSea:
					this.ExecuteHostSacrificeSurvivor(camp, action.Arg1);
					break;

				case HubActionType.RenameSurvivor:
					SurvivorData toRename = camp.GetSurvivor(action.Arg1);
					if (toRename != null && !string.IsNullOrEmpty(action.Arg2))
					{
						toRename.OverrideName(action.Arg2);
					}
					break;

				case HubActionType.ToggleLockSurvivor:
					if (camp.LockedVisitingSurvivors.Contains(action.Arg1))
						camp.LockedVisitingSurvivors.Remove(action.Arg1);
					else
						camp.LockedVisitingSurvivors.Add(action.Arg1);
					break;

				case HubActionType.EquipItem:
					this.ExecuteHostEquipItem(camp, action.Arg1, action.Arg2);
					break;

				case HubActionType.UnequipItem:
					SurvivorData targetSurvivor = camp.GetSurvivor(action.Arg1);
					if (targetSurvivor != null && Enum.TryParse<EquipmentType>(action.Arg2, out EquipmentType eqType))
					{
						targetSurvivor.RemoveEquipment(eqType);
					}
					break;

				case HubActionType.SetSurvivorUsable:
					SurvivorData target = camp.GetSurvivor(action.Arg1);
					if (target != null && !string.IsNullOrEmpty(action.Arg2))
					{
						GameDatabase db = Singleton<GameDatabase>.Instance;
						if (db != null)
						{
							if (db.HasData<SurvivorTraitData>(action.Arg2))
								target.AddTrait(db.GetClonedData<SurvivorTraitData>(action.Arg2));
							else if (db.HasData<SurvivorConditionData>(action.Arg2))
								target.AddCondition(db.GetClonedData<SurvivorConditionData>(action.Arg2));
						}
					}
					break;

				case HubActionType.LevelUpSurvivor:
					this.ExecuteHostLevelUpSurvivor(camp, action.Arg1, action.Arg2);
					break;
			}

			Singleton<GameState>.Instance?.Save();
			HubSyncManager.Instance?.RefreshHubUI();
			HubSyncManager.Instance?.ForceSendSync();
		}

		private void ExecuteHostUpgradeStructure(CampState camp, string structureId, int targetLevel)
		{
			if (string.IsNullOrEmpty(structureId)) return;

			string upgradeDataId = $"{structureId}_{Mathf.Max(1, targetLevel)}";
			StructureUpgradeData upgradeData = Singleton<GameDatabase>.Instance?.GetClonedData<StructureUpgradeData>(upgradeDataId);

			if (upgradeData != null && upgradeData.MaterialNeeded != null)
			{
				foreach (UpgradeMaterialNeeded mat in upgradeData.MaterialNeeded)
				{
					camp.RemoveItem(mat.ItemType, mat.Id, mat.Amount);
				}
			}

			if (camp.StructureStates.ContainsKey(structureId))
			{
				camp.StructureStates[structureId] = (StructureState)targetLevel;
			}
			else
			{
				camp.StructureStates.Add(structureId, (StructureState)targetLevel);
			}

			Singleton<GameState>.Instance?.ProgressionState?.NotifyObjectives(ObjectiveGoal.BUILD_STRUCTURE_1, structureId);
		}

		private void ExecuteHostAssignBed(CampState camp, string survivorId, int bedIndex)
		{
			SurvivorData survivor = camp.GetSurvivor(survivorId);
			if (survivor == null) return;

			string manned = camp.GetMannedStructure(survivor.Id);
			if (!string.IsNullOrEmpty(manned))
			{
				camp.RemoveManningSurvivor(manned, survivor);
			}

			while (camp.SurvivorInBeds.Count <= bedIndex)
			{
				camp.SurvivorInBeds.Add(string.Empty);
			}

			camp.SurvivorInBeds[bedIndex] = survivorId;
			if (!survivor.HasCondition("InInfirmary"))
			{
				SurvivorConditionData cond = Singleton<GameDatabase>.Instance?.GetClonedData<SurvivorConditionData>("InInfirmary");
				if (cond != null) survivor.AddCondition(cond);
			}
		}

		private void ExecuteHostRemoveBed(CampState camp, string survivorId)
		{
			camp.RemoveSurvivorFromBed(survivorId);
			SurvivorData sBed = camp.GetSurvivor(survivorId);
			if (sBed != null)
			{
				if (sBed.InfirmaryTreatments != null)
				{
					foreach (InfirmaryTreatment t in sBed.InfirmaryTreatments)
					{
						this.RefundTreatmentMaterials(camp, sBed, t);
					}
					sBed.InfirmaryTreatments.Clear();
				}
				sBed.RemoveCondition("InInfirmary");
			}
		}

		private void ExecuteHostStartTreatment(CampState camp, string survivorId, string treatmentJson, string fallbackCondition)
		{
			SurvivorData survivor = camp.GetSurvivor(survivorId);
			if (survivor == null) return;

			InfirmaryTreatment treatment = null;
			if (!string.IsNullOrEmpty(treatmentJson) && treatmentJson.StartsWith("{"))
			{
				try
				{
					treatment = JsonConvert.DeserializeObject<InfirmaryTreatment>(treatmentJson, HubSyncManager.JsonSettings);
				}
				catch { }
			}

			if (treatment == null)
			{
				if (treatmentJson == "Cure" && !string.IsNullOrEmpty(fallbackCondition))
				{
					SurvivorConditionData cond = survivor.ConditionDatas.Find(x => x.Id == fallbackCondition);
					int cureTime = cond != null && cond.CureTime > 0 ? cond.CureTime : 1;
					treatment = new InfirmaryTreatmentCure(survivor.Id, cureTime, fallbackCondition);
				}
				else if (treatmentJson == "HealHealth")
				{
					treatment = new InfirmaryTreatmentHeal(survivor.Id, 1, new List<ConditionMaterialNeeded>(), EntityAttributeID.HEALTH);
				}
				else if (treatmentJson == "HealSanity")
				{
					treatment = new InfirmaryTreatmentHeal(survivor.Id, 1, new List<ConditionMaterialNeeded>(), EntityAttributeID.SANITY);
				}
				else if (treatmentJson == "HealAll")
				{
					treatment = new InfirmaryTreatmentHealAll(survivor.Id, 1, new List<ConditionMaterialNeeded>());
				}
			}

			if (treatment == null) return;

			if (treatment is InfirmaryTreatmentCure cure)
			{
				SurvivorConditionData cond = survivor.ConditionDatas.Find(x => x.Id == cure.ConditionId);
				if (cond != null && cond.MaterialNeeded != null)
				{
					foreach (ConditionMaterialNeeded mat in cond.MaterialNeeded)
					{
						camp.RemoveItem(mat.ItemType, mat.Id, mat.Amount);
					}
				}
			}
			else if (treatment.MaterialsNeeded != null)
			{
				foreach (ConditionMaterialNeeded mat in treatment.MaterialsNeeded)
				{
					camp.RemoveItem(mat.ItemType, mat.Id, mat.Amount);
				}
			}

			if (survivor.HasCondition("MendingSpirit"))
			{
				int duration = treatment.DurationLeft;
				for (int i = 0; i < duration; i++)
				{
					treatment.PassDay();
				}
				survivor.RemoveCondition("MendingSpirit");
			}
			else
			{
				survivor.InfirmaryTreatments.Add(treatment);
			}
		}

		private void ExecuteHostCancelTreatment(CampState camp, string survivorId, int treatmentIndex)
		{
			SurvivorData survivor = camp.GetSurvivor(survivorId);
			if (survivor != null && survivor.InfirmaryTreatments != null && survivor.InfirmaryTreatments.Count > 0)
			{
				int idx = (treatmentIndex >= 0 && treatmentIndex < survivor.InfirmaryTreatments.Count) ? treatmentIndex : 0;
				InfirmaryTreatment treatment = survivor.InfirmaryTreatments[idx];
				this.RefundTreatmentMaterials(camp, survivor, treatment);
				survivor.InfirmaryTreatments.RemoveAt(idx);
			}
		}

		private void RefundTreatmentMaterials(CampState camp, SurvivorData survivor, InfirmaryTreatment treatment)
		{
			if (treatment == null || treatment.DurationLeft == 0) return;

			if (treatment is InfirmaryTreatmentCure cure)
			{
				SurvivorConditionData cond = survivor.ConditionDatas.Find(x => x.Id == cure.ConditionId);
				if (cond != null && cond.MaterialNeeded != null)
				{
					foreach (ConditionMaterialNeeded mat in cond.MaterialNeeded)
					{
						Item item = new Item(Singleton<GameDatabase>.Instance?.GetClonedData<CollectibleData>(mat.Id), mat.Amount);
						camp.AddItem(item);
					}
				}
			}
			else if (treatment.MaterialsNeeded != null)
			{
				foreach (ConditionMaterialNeeded mat in treatment.MaterialsNeeded)
				{
					Item item = new Item(Singleton<GameDatabase>.Instance?.GetClonedData<CollectibleData>(mat.Id), mat.Amount);
					camp.AddItem(item);
				}
			}
		}

		private void ExecuteHostSacrificeSurvivor(CampState camp, string survivorId)
		{
			SurvivorData toSacrifice = camp.GetSurvivor(survivorId);
			if (toSacrifice != null)
			{
				camp.KillSurvivor(toSacrifice, "thrown at sea");
				camp.ChangeMoraleValue(10, 0);
				foreach (SurvivorData survivor in camp.Survivors)
				{
					if (survivor.Id != toSacrifice.Id && !survivor.HasAbsentCondition())
					{
						survivor.Hurt(10, EntityAttributeID.SANITY, null, false);
					}
				}
			}
		}

		private void ExecuteHostStartExpedition(CampState camp, string destinationId, string participantsJson, int slotIndex)
		{
			ExpeditionData data = Singleton<GameDatabase>.Instance?.GetData<ExpeditionData>(destinationId);
			if (data == null) return;

			List<string> participants = JsonConvert.DeserializeObject<List<string>>(participantsJson);
			if (participants == null || participants.Count == 0) return;

			foreach (string pId in participants)
			{
				SurvivorData s = camp.GetSurvivor(pId);
				if (s != null)
				{
					SurvivorConditionData cond = Singleton<GameDatabase>.Instance?.GetClonedData<SurvivorConditionData>("OnExpedition");
					if (cond != null) s.AddCondition(cond);
				}
			}

			Expedition exp = new Expedition(data, data.Days, participants, false, new List<Item>(), new List<SurvivorData>(), false);
			exp.SetSuccessChance(MenuLighthouse.CalculateExpeditionChance(exp));
			camp.AddExpedition(slotIndex, exp);
		}

		private void ExecuteHostRecruitSurvivor(CampState camp, string survivorId)
		{
			SurvivorData survivor = camp.VisitingSurvivors.Find(x => x.Id == survivorId) 
			                     ?? camp.ExpeditionSurvivors.Find(x => x.Id == survivorId);
			if (survivor != null)
			{
				camp.VisitingSurvivors.Remove(survivor);
				camp.ExpeditionSurvivors.Remove(survivor);
				camp.LockedVisitingSurvivors.Remove(survivor.Id);
				camp.AddSurvivor(survivor);
				survivor.EquipDefault(true);
			}
		}

		private void ExecuteHostEquipItem(CampState camp, string survivorId, string itemId)
		{
			SurvivorData survivor = camp.GetSurvivor(survivorId);
			ItemData itemData = HubLootSyncManager.ResolveItemData(itemId);
			if (survivor != null && itemData is EquipmentItemData eqData)
			{
				survivor.ReplaceEquipment(new Item(eqData, 1));
			}
		}

		private void ExecuteHostLevelUpSurvivor(CampState camp, string survivorId, string resultJson)
		{
			SurvivorData survivor = camp.GetSurvivor(survivorId);
			if (survivor == null) return;

			try
			{
				SurvivorLevelUpResult result = JsonConvert.DeserializeObject<SurvivorLevelUpResult>(resultJson);
				if (result == null) return;

				if (result.RemovedTraitAndSkills != null)
				{
					foreach (string remId in result.RemovedTraitAndSkills)
					{
						survivor.RemoveTrait(remId);
					}
				}

				if (result.NewTraitsAndSkills != null)
				{
					foreach (string traitId in result.NewTraitsAndSkills)
					{
						SurvivorTraitData cloned = Singleton<GameDatabase>.Instance?.GetClonedData<SurvivorTraitData>(traitId);
						if (cloned != null)
						{
							SurvivorTraitData existingGroupTrait = survivor.GetTraitWithTraitGroup(cloned.TraitGroup);
							if (existingGroupTrait != null)
							{
								survivor.RemoveTrait(existingGroupTrait.Id);
							}
							survivor.AddTrait(cloned);
						}
					}
				}

				if (result.PointsDistribution != null)
				{
					foreach (var kvp in result.PointsDistribution)
					{
						if (kvp.Value > 0 && survivor.Attributes.ContainsKey(kvp.Key))
						{
							EntityAttributeGenerationData genData = Singleton<GameDatabase>.Instance?.GetData<EntityAttributeGenerationData>(kvp.Key.ToString());
							float pointMult = genData != null ? genData.PointMultiplier : 1f;
							int addVal = Mathf.RoundToInt((float)kvp.Value * Const.LEVEL_UP_POINT_MULT[kvp.Key] * pointMult);

							survivor.Attributes[kvp.Key].AddAdditiveModifier(
								"levelUp_" + survivor.Level.ToString(),
								addVal,
								ED.Basics.Attribute.RatioHandling.KeepRatio,
								ED.Basics.Attribute.AttributeMethod.HIGHEST_VALUE
							);
						}
					}
				}

				survivor.ConfirmLevelUp(result);
				survivor.UpdateStatRelatedConditions();
			}
			catch (Exception ex)
			{
				this._log.LogWarning($"Level up apply error: {ex.Message}");
			}
		}

		private void OnDestroy()
		{
			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
			}
			if (Instance == this) Instance = null;
		}

		private ManualLogSource _log;
		private NetworkManager _subscribedNetworkManager;
	}


	[HarmonyPatch(typeof(SurvivorData), nameof(SurvivorData.AddCondition))]
	internal static class SurvivorData_AddCondition_Infirmary_Patch
	{
		private static void Postfix(SurvivorData __instance, [HarmonyArgument(0)] SurvivorConditionData survivorConditionData)
		{
			if (!HubSyncManager.IsApplyingSync && survivorConditionData != null && survivorConditionData.Id == "InInfirmary")
			{
				int bedIndex = Singleton<GameState>.Instance?.CampState?.SurvivorInBeds?.IndexOf(__instance.Id) ?? -1;
				if (bedIndex >= 0)
				{
					HubActionSyncManager.Instance?.OnLocalAction(HubActionType.AssignBed, __instance.Id, "", "", bedIndex);
					HubActionSyncManager.Instance?.SyncClientCampStateToHost();
				}
			}
		}
	}

	[HarmonyPatch(typeof(MenuInfirmaryTreatmentPanel), nameof(MenuInfirmaryTreatmentPanel.StartTreatment))]
	internal static class MenuInfirmaryTreatmentPanel_StartTreatment_Patch
	{
		private static void Postfix(MenuInfirmaryTreatmentPanel __instance, [HarmonyArgument(0)] InfirmaryTreatmentOptionPanel treatmentOption)
		{
			if (!HubSyncManager.IsApplyingSync && treatmentOption?.InfirmaryTreatment != null)
			{
				try
				{
					string treatmentJson = JsonConvert.SerializeObject(treatmentOption.InfirmaryTreatment, Formatting.None, HubSyncManager.JsonSettings);
					HubActionSyncManager.Instance?.OnLocalAction(HubActionType.StartTreatment, treatmentOption.InfirmaryTreatment.SurvivorID, treatmentJson);
					HubActionSyncManager.Instance?.SyncClientCampStateToHost();
				}
				catch { }
			}
		}
	}

	[HarmonyPatch(typeof(MenuInfirmaryTreatmentPanel), nameof(MenuInfirmaryTreatmentPanel.ConfirmCancelTreatment))]
	internal static class MenuInfirmaryTreatmentPanel_ConfirmCancelTreatment_Patch
	{
		private static void Prefix(MenuInfirmaryTreatmentPanel __instance, [HarmonyArgument(0)] InfirmaryOngoingTreatmentPanel treatmentOption)
		{
			if (HubSyncManager.IsApplyingSync) return;
			SurvivorData survivor = AccessTools.Field(typeof(MenuInfirmaryTreatmentPanel), "_survivorData")?.GetValue(__instance) as SurvivorData;
			if (survivor != null && treatmentOption?.InfirmaryTreatment != null)
			{
				int idx = survivor.InfirmaryTreatments.IndexOf(treatmentOption.InfirmaryTreatment);
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.CancelTreatment, survivor.Id, "", "", idx);
			}
		}

		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(MenuInfirmaryTreatmentPanel), nameof(MenuInfirmaryTreatmentPanel.ConfirmCancelPatientStay))]
	internal static class MenuInfirmaryTreatmentPanel_ConfirmCancelPatientStay_Patch
	{
		private static void Postfix(MenuInfirmaryTreatmentPanel __instance)
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(MenuLighthouse), nameof(MenuLighthouse.RecruitSelectedSurvivor))]
	internal static class MenuLighthouse_RecruitSelectedSurvivor_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(MenuLighthouse), nameof(MenuLighthouse.LockSelectedSurvivor))]
	internal static class MenuLighthouse_LockSelectedSurvivor_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.AddExpedition), new Type[] { typeof(int), typeof(Expedition) })]
	internal static class CampState_AddExpedition_Patch
	{
		private static void Postfix([HarmonyArgument(0)] int index, [HarmonyArgument(1)] Expedition expedition)
		{
			if (!HubSyncManager.IsApplyingSync && expedition != null && expedition.Data != null)
			{
				string participantsJson = JsonConvert.SerializeObject(expedition.Participants);
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.StartExpedition, expedition.Data.Id, participantsJson, "", index);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.RemoveExpedition), new Type[] { typeof(int) })]
	internal static class CampState_RemoveExpedition_Patch
	{
		private static void Postfix([HarmonyArgument(0)] int index)
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.CancelExpedition, "", "", "", index);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(MenuAltar), nameof(MenuAltar.ToggleIdol))]
	[HarmonyPatch(typeof(MenuAltar), nameof(MenuAltar.PerformLifeTribute))]
	[HarmonyPatch(typeof(MenuAltar), nameof(MenuAltar.PerformMindTribute))]
	[HarmonyPatch(typeof(MenuAltar), nameof(MenuAltar.PerformDevotionTribute))]
	[HarmonyPatch(typeof(MenuAltar), nameof(MenuAltar.ConfirmBloodTributeSelection))]
	[HarmonyPatch(typeof(MenuMemorial), nameof(MenuMemorial.OnBurnButtonClicked))]
	internal static class Altar_Memorial_NativeActions_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch]
	internal static class MenuFishingHut_Interactions_Patch
	{
		[HarmonyTargetMethods]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			List<MethodBase> list = new List<MethodBase>();
			Type t = typeof(MenuFishingHut);
			if (t != null)
			{
				foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if (m.IsGenericMethod) continue;
					string name = m.Name.ToLowerInvariant();
					if (name.Contains("collect") || name.Contains("net") || name.Contains("harvest") || 
					    name.Contains("fish") || name.Contains("transfer") || name.Contains("click") || 
					    name.Contains("empty") || name.Contains("button"))
					{
						list.Add(m);
					}
				}
			}
			return list;
		}

		[HarmonyPostfix]
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.SetManningSurvivor))]
	internal static class CampState_SetManningSurvivor_Patch
	{
		private static void Postfix([HarmonyArgument(0)] string structure, [HarmonyArgument(1)] string survivorId)
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.SetManningSurvivor, structure, survivorId);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.RemoveManningSurvivor), new Type[] { typeof(string) })]
	internal static class CampState_RemoveManningSurvivor_Patch
	{
		private static void Postfix([HarmonyArgument(0)] string structure)
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.RemoveManningSurvivor, structure);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.RemoveSurvivorFromBed))]
	internal static class CampState_RemoveSurvivorFromBed_Patch
	{
		private static void Postfix([HarmonyArgument(0)] string survivorId)
		{
			if (!HubSyncManager.IsApplyingSync)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.RemoveBed, survivorId);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.KillSurvivor))]
	internal static class CampState_KillSurvivor_Patch
	{
		private static void Postfix([HarmonyArgument(0)] SurvivorData survivor, [HarmonyArgument(1)] string reason)
		{
			if (!HubSyncManager.IsApplyingSync && survivor != null)
			{
				string r = (reason ?? string.Empty).ToLowerInvariant();
				if (r.Contains("sea") || r.Contains("altar") || r.Contains("sacrifice") || r.Contains("tribute"))
				{
					HubActionSyncManager.Instance?.OnLocalAction(HubActionType.ThrowAtSea, survivor.Id, reason);
					HubActionSyncManager.Instance?.SyncClientCampStateToHost();
				}
			}
		}
	}

	[HarmonyPatch]
	internal static class UniversalBuildingSyncPatch
	{
		[HarmonyTargetMethods]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			List<MethodBase> list = new List<MethodBase>();
			Type[] menuTypes = new Type[]
			{
				typeof(MenuInfirmary),
				typeof(MenuLighthouse),
				typeof(MenuAltar),
				typeof(MenuLibrary),
				typeof(MenuFishingHut),
				typeof(MenuMemorial),
				typeof(MenuWorkshop),
				typeof(MenuTownhall)
			};

			foreach (Type t in menuTypes)
			{
				foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if (m.Name.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0 ||
					    m.Name.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						if (m.GetParameters().Length == 0 && !m.IsGenericMethod)
						{
							list.Add(m);
						}
					}
				}
			}
			return list;
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance)
		{
			if (!HubSyncManager.IsApplyingSync && __instance is Menu menu && !string.IsNullOrEmpty(menu.MenuId))
			{
				int nextTier = 1;
				if (Singleton<GameState>.Instance?.CampState?.StructureStates != null &&
				    Singleton<GameState>.Instance.CampState.StructureStates.TryGetValue(menu.MenuId, out var curState))
				{
					nextTier = (int)curState + 1;
				}

				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.UpgradeStructure, menu.MenuId, "", "", nextTier);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(MenuTownhall), "OnRenameSurvivorConfirmed")]
	internal static class MenuTownhall_OnRenameSurvivorConfirmed_Patch
	{
		private static void Postfix(MenuTownhall __instance, [HarmonyArgument(0)] string newName)
		{
			SurvivorTownhallCard card = AccessTools.Field(typeof(MenuTownhall), "_selectedSurvivorCard")?.GetValue(__instance) as SurvivorTownhallCard;
			SurvivorData data = card?.GetData();
			if (data != null)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.RenameSurvivor, data.Id, newName);
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(SurvivorData), nameof(SurvivorData.ConfirmLevelUp))]
	internal static class SurvivorData_ConfirmLevelUp_Patch
	{
		private static void Postfix(SurvivorData __instance, [HarmonyArgument(0)] SurvivorLevelUpResult result)
		{
			if (!HubSyncManager.IsApplyingSync && NetworkManager.Instance != null && NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost)
			{
				try
				{
					string resultJson = JsonConvert.SerializeObject(result);
					HubActionSyncManager.Instance?.OnLocalAction(HubActionType.LevelUpSurvivor, __instance.Id, resultJson);
					HubActionSyncManager.Instance?.SyncClientCampStateToHost();
				}
				catch { }
			}
		}
	}

	[HarmonyPatch(typeof(MenuWorkshopCraftingInventory), nameof(MenuWorkshopCraftingInventory.UpdateCraftablesList))]
	internal static class MenuWorkshopCraftingInventory_UpdateCraftablesList_Patch
	{
		private static bool Prefix()
		{
			Singleton<GameState>.Instance?.CampState?.UpdateAvailableCraftingRecipes();
			return true;
		}

		private static Exception Finalizer(Exception __exception) => null;
	}

	[HarmonyPatch(typeof(MenuWorkshopSelectedPanel), "ButtonCraftConfirmed")]
	internal static class MenuWorkshopSelectedPanel_ButtonCraftConfirmed_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}

	[HarmonyPatch(typeof(SurvivorData), nameof(SurvivorData.AddTrait), new Type[] { typeof(SurvivorTraitData), typeof(bool) })]
	internal static class SurvivorData_AddTrait_RPC_Patch
	{
		private static void Postfix(SurvivorData __instance, SurvivorTraitData traitData, bool allowQuickLearnerBump)
		{
			if (!HubSyncManager.IsApplyingSync && traitData != null && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.SetSurvivorUsable, __instance.Id, traitData.Id);
			}
		}
	}

	[HarmonyPatch(typeof(SurvivorData), nameof(SurvivorData.AddCondition), new Type[] { typeof(SurvivorConditionData) })]
	internal static class SurvivorData_AddCondition_RPC_Patch
	{
		private static void Postfix(SurvivorData __instance, SurvivorConditionData survivorConditionData)
		{
			if (!HubSyncManager.IsApplyingSync && survivorConditionData != null && survivorConditionData.Id != "InInfirmary" && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				HubActionSyncManager.Instance?.OnLocalAction(HubActionType.SetSurvivorUsable, __instance.Id, survivorConditionData.Id);
			}
		}
	}

	[HarmonyPatch(typeof(CampState), nameof(CampState.RemoveItem), new Type[] { typeof(ItemType), typeof(string), typeof(int) })]
	internal static class CampState_RemoveItem_Sync_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				if (SceneManager.GetActiveScene().name == "_Hub" && !(DiveTransitionManager.Instance?.IsPerformingFinalOutro ?? false))
				{
					HubActionSyncManager.Instance?.SyncClientCampStateToHost();
				}
			}
		}
	}
	

	[HarmonyPatch(typeof(MenuLighthouse), "LaunchExpedition")]
	internal static class MenuLighthouse_LaunchExpedition_Patch
	{
		private static void Postfix()
		{
			if (!HubSyncManager.IsApplyingSync && NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
			{
				HubActionSyncManager.Instance?.SyncClientCampStateToHost();
			}
		}
	}
}

