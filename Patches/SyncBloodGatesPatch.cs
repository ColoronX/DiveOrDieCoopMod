using System;
using System.Collections.Generic;
using System.Globalization;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Basics;
using DRS.Patterns;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncBloodGatesPatch
	{
		private enum BloodGateAction
		{
			Create = 1,
			Remove = 2
		}

		public static bool IsApplyingRemoteBloodGates;
		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

		public static void HandleMessage(int peerId, NetMessage message)
		{
			DiveStateSyncMessage msg = DiveStateSyncMessage.FromPayload(message.Payload);

			switch ((BloodGateAction)msg.IntArg1)
			{
				case BloodGateAction.Create:
					if (NetworkManager.Instance.IsHost)
					{
						if (TryUnpackPosition(msg.StringArg, out Vector2 gate2Pos))
						{
							ApplyCreate(msg.IntArg2, new Vector2(msg.PosX, msg.PosY), gate2Pos);

							NetworkManager.Instance.Broadcast(
								message,
								peerId
							);
						}
					}
					else if (TryUnpackPosition(msg.StringArg, out Vector2 clientGate2Pos))
					{
						ApplyCreate(msg.IntArg2, new Vector2(msg.PosX, msg.PosY), clientGate2Pos);
					}
					break;

				case BloodGateAction.Remove:
					ApplyRemove(msg.IntArg2);

					if (NetworkManager.Instance.IsHost)
						NetworkManager.Instance.Broadcast(message, peerId);
					break;
			}
		}

		private static void ApplyCreate(int gateId, Vector2 gate1Pos, Vector2 gate2Pos)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return;

			if (gs.PoolState.PersistentBloodGates.TryGetValue(gateId, out BloodGatesData existing))
			{
				IsApplyingRemoteBloodGates = true;

				try
				{
					if (existing.BloodGate1Instance != null)
						UnityEngine.Object.Destroy(existing.BloodGate1Instance);

					if (existing.BloodGate2Instance != null)
						UnityEngine.Object.Destroy(existing.BloodGate2Instance);

					gs.PoolState.RemovePersistentBloodGates(gateId);
				}
				finally
				{
					IsApplyingRemoteBloodGates = false;
				}
			}

			IsApplyingRemoteBloodGates = true;

			try
			{
				Singleton<PersistentPoolItemManager>.Instance.SpawnAndRegisterBloodGates(
					gateId,
					gate1Pos,
					gate2Pos
				);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[DiveOrDieCoop] Failed to create remote blood gates: " + ex.Message);
			}
			finally
			{
				IsApplyingRemoteBloodGates = false;
			}
		}

		private static void ApplyRemove(int gateId)
		{
			GameState gs = Singleton<GameState>.Instance;
			if (gs?.PoolState == null)
				return;

			if (!gs.PoolState.PersistentBloodGates.TryGetValue(gateId, out BloodGatesData data))
				return;

			IsApplyingRemoteBloodGates = true;

			try
			{
				if (data.BloodGate1Instance != null)
					UnityEngine.Object.Destroy(data.BloodGate1Instance);

				if (data.BloodGate2Instance != null)
					UnityEngine.Object.Destroy(data.BloodGate2Instance);

				gs.PoolState.RemovePersistentBloodGates(gateId);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[DiveOrDieCoop] Failed to remove remote blood gates: " + ex.Message);
			}
			finally
			{
				IsApplyingRemoteBloodGates = false;
			}
		}

		private static string PackPosition(Vector2 position)
		{
			return position.x.ToString("R", Invariant) + "|" + position.y.ToString("R", Invariant);
		}

		private static bool TryUnpackPosition(string value, out Vector2 position)
		{
			position = Vector2.zero;

			if (string.IsNullOrEmpty(value))
				return false;

			string[] parts = value.Split('|');

			if (parts.Length != 2)
				return false;

			if (!float.TryParse(parts[0], NumberStyles.Float, Invariant, out float x))
				return false;

			if (!float.TryParse(parts[1], NumberStyles.Float, Invariant, out float y))
				return false;

			position = new Vector2(x, y);
			return true;
		}

		[HarmonyPatch(typeof(PersistentPoolItemManager), nameof(PersistentPoolItemManager.SpawnAndRegisterBloodGates))]
		[HarmonyPostfix]
		private static void SpawnAndRegisterBloodGates_Postfix(object[] __args)
		{
			if (IsApplyingRemoteBloodGates)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			if (!TryGetSpawnArguments(__args, out int gateId, out Vector2 gate1Pos, out Vector2 gate2Pos))
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BloodGateAction.Create,
				IntArg2 = gateId,
				PosX = gate1Pos.x,
				PosY = gate1Pos.y,
				StringArg = PackPosition(gate2Pos)
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.BloodGateSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.BloodGateSync));
		}

		[HarmonyPatch(typeof(PoolState), nameof(PoolState.RemovePersistentBloodGates))]
		[HarmonyPrefix]
		private static void RemovePersistentBloodGates_Prefix(int id)
		{
			if (IsApplyingRemoteBloodGates)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			DiveStateSyncMessage msg = new DiveStateSyncMessage
			{
				IntArg1 = (int)BloodGateAction.Remove,
				IntArg2 = id
			};

			if (net.IsHost)
				net.Broadcast(msg.ToNetMessage(MessageType.BloodGateSync));
			else
				net.SendToHost(msg.ToNetMessage(MessageType.BloodGateSync));
		}

		private static bool TryGetSpawnArguments(object[] args, out int gateId, out Vector2 gate1Pos, out Vector2 gate2Pos)
		{
			gateId = 0;
			gate1Pos = Vector2.zero;
			gate2Pos = Vector2.zero;

			if (args == null)
				return false;

			bool foundId = false;
			int vectorCount = 0;

			foreach (object arg in args)
			{
				if (arg is int intValue)
				{
					gateId = intValue;
					foundId = true;
				}
				else if (arg is Vector2 vector2)
				{
					if (vectorCount == 0)
						gate1Pos = vector2;
					else if (vectorCount == 1)
						gate2Pos = vector2;

					vectorCount++;
				}
				else if (arg is Vector3 vector3)
				{
					Vector2 vector = new Vector2(vector3.x, vector3.y);

					if (vectorCount == 0)
						gate1Pos = vector;
					else if (vectorCount == 1)
						gate2Pos = vector;

					vectorCount++;
				}
			}

			return foundId && vectorCount >= 2;
		}
	}
}