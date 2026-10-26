using System;
using DiveOrDieCoop.Networking;
using DRS.Patterns;
using ED.Basics;
using ED.Diving;
using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public sealed class CombatSyncManager : MonoBehaviour
	{
		public static CombatSyncManager Instance { get; private set; }

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
		}

		public void BroadcastEntityDefeated(Transform targetTransform) { }
		public void SendMonsterDamageToPlayer(int targetPeerId, int value, EntityAttributeID attribute, string sourceName, Vector2 knockbackDir = default, float knockbackIntensity = 0f) { }

		public static bool AreAllDiversDead()
		{
			PlayerController localPlayer = Singleton<PlayerController>.Instance;
			if (localPlayer != null && !localPlayer.IsDead) return false;

			if (DiveSyncManager.Instance != null)
			{
				foreach (var remote in DiveSyncManager.Instance.GetAllRemoteDivers())
				{
					if (remote != null && remote.IsAlive) return false;
				}
			}
			return true;
		}

		public static Transform FindNearestLivingDiver(Vector3 origin)
		{
			PlayerController localPlayer = Singleton<PlayerController>.Instance;
			return localPlayer != null && !localPlayer.IsDead ? localPlayer.transform : null;
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}
	}
}

