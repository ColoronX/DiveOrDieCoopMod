using ED.Diving;
using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public sealed class WorldSyncManager : MonoBehaviour
	{
		public static WorldSyncManager Instance { get; private set; }

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

		public void OnLocalEntityRegistered(Entity entity) { }
		public void OnLocalEntityUnregistered(Entity entity) { }

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}
	}
}

