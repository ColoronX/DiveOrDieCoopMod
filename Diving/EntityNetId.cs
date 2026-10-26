using UnityEngine;

namespace DiveOrDieCoop.Diving
{
	public static class EntityNetId
	{
		public static void Clear() { }
		public static void Register(int netId, Transform target) { }
		public static void Unregister(Transform target) { }
		public static int GetNetId(Transform t) => 0;
		public static Transform Resolve(int netId) => null;
		public static void InitializeSceneRegistry() { }
	}
}

