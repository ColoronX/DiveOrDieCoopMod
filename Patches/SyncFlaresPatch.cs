using System;
using System.Collections.Generic;
using System.Reflection;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch]
	public static class SyncFlaresPatch
	{
		public static bool IsSpawningRemoteFlare;

		private static GameObject _flarePrefabTemplate;
		private static int _nextFlareId = 1;

		private static readonly HashSet<int> _initializedLocalFlares = new HashSet<int>();
		private static readonly Dictionary<long, FlareObject> _remoteFlares = new Dictionary<long, FlareObject>();
		private static readonly List<FlareSyncMessage> _pendingFlares = new List<FlareSyncMessage>();

		[HarmonyPatch(typeof(FlareObject), "Initialize")]
		[HarmonyPostfix]
		private static void Flare_Initialize_Postfix(FlareObject __instance)
		{
			if (IsSpawningRemoteFlare || __instance == null)
				return;

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			int instanceId = __instance.gameObject.GetInstanceID();
			if (!_initializedLocalFlares.Add(instanceId))
				return;

			int flareId = _nextFlareId++;
			Rigidbody2D rb = __instance.GetComponent<Rigidbody2D>();
			Vector3 pos = __instance.transform.position;
			Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
			float remainingDuration = GetRemainingDuration(__instance);

			__instance.OnFlareDestroyed += delegate
			{
				OnLocalFlareDestroyed(instanceId, flareId);
			};

			SendMessage(net, new FlareSyncMessage
			{
				SourcePeerId = PeerInfo.HostPeerId,
				FlareId = flareId,
				Destroyed = false,
				PosX = pos.x,
				PosY = pos.y,
				VelX = velocity.x,
				VelY = velocity.y,
				RemainingDuration = remainingDuration
			});
		}

		private static void OnLocalFlareDestroyed(int instanceId, int flareId)
		{
			_initializedLocalFlares.Remove(instanceId);

			NetworkManager net = NetworkManager.Instance;
			if (net == null || !net.IsConnected)
				return;

			SendMessage(net, new FlareSyncMessage
			{
				SourcePeerId = PeerInfo.HostPeerId,
				FlareId = flareId,
				Destroyed = true
			});
		}

		private static void SendMessage(NetworkManager net, FlareSyncMessage msg)
		{
			NetMessage message = msg.ToNetMessage();

			if (net.IsHost)
				net.Broadcast(message);
			else
				net.SendToHost(message);
		}

		private static float GetRemainingDuration(FlareObject flare)
		{
			try
			{
				FieldInfo field = AccessTools.Field(typeof(FlareObject), "_remainingFlareDuration");
				return field != null ? (float)field.GetValue(flare) : 60f;
			}
			catch
			{
				return 60f;
			}
		}

		public static void HandleRemoteFlare(FlareSyncMessage msg)
		{
			long key = MakeKey(msg.SourcePeerId, msg.FlareId);

			if (msg.Destroyed)
			{
				_pendingFlares.RemoveAll(x => MakeKey(x.SourcePeerId, x.FlareId) == key);

				if (_remoteFlares.TryGetValue(key, out FlareObject flare))
				{
					_remoteFlares.Remove(key);

					if (flare != null)
						UnityEngine.Object.Destroy(flare.gameObject);
				}

				return;
			}

			if (_remoteFlares.TryGetValue(key, out FlareObject existing))
			{
				if (existing != null)
					return;

				_remoteFlares.Remove(key);
			}

			if (!TrySpawnRemoteFlare(msg, key))
			{
				bool alreadyPending = false;

				for (int i = 0; i < _pendingFlares.Count; i++)
				{
					if (MakeKey(_pendingFlares[i].SourcePeerId, _pendingFlares[i].FlareId) == key)
					{
						alreadyPending = true;
						break;
					}
				}

				if (!alreadyPending)
					_pendingFlares.Add(msg);
			}
		}

		public static void ProcessPending()
		{
			for (int i = _pendingFlares.Count - 1; i >= 0; i--)
			{
				FlareSyncMessage msg = _pendingFlares[i];
				long key = MakeKey(msg.SourcePeerId, msg.FlareId);

				if (_remoteFlares.ContainsKey(key))
				{
					_pendingFlares.RemoveAt(i);
					continue;
				}

				if (TrySpawnRemoteFlare(msg, key))
					_pendingFlares.RemoveAt(i);
			}
		}

		private static bool TrySpawnRemoteFlare(FlareSyncMessage msg, long key)
		{
			if (_flarePrefabTemplate == null)
			{
				foreach (FlareObject flare in Resources.FindObjectsOfTypeAll<FlareObject>())
				{
					if (flare != null && flare.gameObject != null)
					{
						_flarePrefabTemplate = flare.gameObject;
						break;
					}
				}
			}

			if (_flarePrefabTemplate == null)
				return false;

			IsSpawningRemoteFlare = true;

			try
			{
				GameObject clone = UnityEngine.Object.Instantiate(
					_flarePrefabTemplate,
					new Vector3(msg.PosX, msg.PosY, 0f),
					Quaternion.identity
				);

				Rigidbody2D rb = clone.GetComponent<Rigidbody2D>();
				if (rb != null)
					rb.velocity = new Vector2(msg.VelX, msg.VelY);

				FlareObject remoteFlare = clone.GetComponent<FlareObject>();
				if (remoteFlare == null)
				{
					UnityEngine.Object.Destroy(clone);
					return false;
				}

				AccessTools.Method(typeof(FlareObject), "Initialize")?.Invoke(remoteFlare, null);

				FieldInfo durationField = AccessTools.Field(typeof(FlareObject), "_remainingFlareDuration");
				if (durationField != null)
					durationField.SetValue(remoteFlare, Mathf.Max(0f, msg.RemainingDuration));

				_remoteFlares[key] = remoteFlare;
				return true;
			}
			catch (Exception ex)
			{
				BepInEx.Logging.Logger.CreateLogSource("DiveOrDieCoop.Flares")
					.LogWarning($"Failed to spawn remote flare: {ex.Message}");
				return false;
			}
			finally
			{
				IsSpawningRemoteFlare = false;
			}
		}

		private static long MakeKey(int sourcePeerId, int flareId)
		{
			return ((long)sourcePeerId << 32) | (uint)flareId;
		}

		public static void ClearRemoteFlares()
		{
			foreach (FlareObject flare in _remoteFlares.Values)
			{
				if (flare != null)
					UnityEngine.Object.Destroy(flare.gameObject);
			}

			_remoteFlares.Clear();
			_pendingFlares.Clear();
			_initializedLocalFlares.Clear();
		}
	}
}