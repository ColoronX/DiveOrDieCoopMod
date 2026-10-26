using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Attribute = ED.Basics.Attribute;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SharedIdolPowersPatch
	{
		public static bool IsApplyingRemoteIdol;

		[HarmonyTargetMethods]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			List<MethodBase> methods = new List<MethodBase>();
			Type[] types =
			{
				typeof(IdolBlessing_Respite),
				typeof(IdolBlessing_Severance),
				typeof(IdolBlessing_Ruin),
				typeof(IdolBlessing_Betrayal),
				typeof(IdolBlessing_Judgement),
				typeof(IdolBlessing_Loss),
				typeof(IdolBlessing_Worship),
				typeof(IdolBlessing_Drowning)
			};

			foreach (Type type in types)
			{
				MethodInfo method = AccessTools.DeclaredMethod(type, "ApplyBlessing");
				if (method != null)
					methods.Add(method);
			}

			return methods;
		}

		[HarmonyPostfix]
		private static void ApplyBlessing_Postfix(IdolBlessing __instance)
		{
			if (IsApplyingRemoteIdol || __instance == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				StringArg = __instance.IdolId
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.IdolPowerSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.IdolPowerSync));
		}

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);
			ApplyRemoteIdolPower(msg.StringArg);

			if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
				NetworkManager.Instance.Broadcast(message, peerId);
		}

		public static void ApplyRemoteIdolPower(string idolId)
		{
			if (!Singleton<PlayerController>.HasInstance() || Singleton<PlayerController>.Instance == null)
				return;

			PlayerController player = Singleton<PlayerController>.Instance;
			IsApplyingRemoteIdol = true;

			try
			{
				switch (idolId)
				{
					case "IdolOfRespite":
						player.Entity.AddAttributeValue(EntityAttributeID.OXYGEN, Mathf.FloorToInt(player.Entity.GetAttribute(EntityAttributeID.OXYGEN).Max * 0.35f));
						Singleton<DivingParticles>.Instance?.EnableParticles("Haven", true);
						break;

					case "IdolOfSeverance":
						player.Entity.GetAttribute(EntityAttributeID.SWIM_SPEED).AddMultiplicativeModifier("Severance_Coop", 2f, Attribute.RatioHandling.KeepRatio, Attribute.AttributeMethod.HIGHEST_VALUE);
						Singleton<DivingParticles>.Instance?.EnableParticles("Farewell", true);
						player.StartCoroutine(RemoveSpeedBoost(player));
						break;

					case "IdolOfRuin":
						player.KillAllLeeches();
						Singleton<DivingParticles>.Instance?.EnableParticles("Ruin", true);
						break;

					case "IdolOfBetrayal":
						Singleton<DivingParticles>.Instance?.EnableParticles("Pacify", true);
						break;
				}
			}
			catch { }
			finally
			{
				IsApplyingRemoteIdol = false;
			}
		}

		private static IEnumerator RemoveSpeedBoost(PlayerController player)
		{
			yield return new WaitForSeconds(10f);

			if (player != null && player.Entity != null)
			{
				player.Entity.GetAttribute(EntityAttributeID.SWIM_SPEED).RemoveMultiplicativeModifier("Severance_Coop", Attribute.RatioHandling.KeepRatio);
				Singleton<DivingParticles>.Instance?.DisableParticles("Farewell", true);
			}
		}
	}
}