using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.Protocol;
using DRS.Patterns;
using ED.Diving;
using ED.Hub;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Diving
{
	public sealed class DiveSyncManager : MonoBehaviour
	{
		public static DiveSyncManager Instance { get; private set; }
		public const float SendIntervalSeconds = 1f / 30f;

		private NetworkManager _subscribedNetworkManager;
		private DivingOrchestrator _currentOrchestrator;
		private ManualLogSource _log;
		private bool _localDiverReady;
		private float _sendTimer;
		private string _localDeathAnimation = string.Empty;

		private readonly Dictionary<int, GameObject> _remoteDivers = new Dictionary<int, GameObject>();

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
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME + ".DiveSync");
			SceneManager.sceneLoaded += this.OnSceneLoaded;
		}

		public string LocalDeathAnimation => this._localDeathAnimation;

		public void SetLocalDeathAnimation(string animationName)
		{
			this._localDeathAnimation = animationName ?? string.Empty;
		}

		public Transform GetFirstRemoteDiverTransform()
		{
			foreach (var kvp in this._remoteDivers)
			{
				if (kvp.Value == null)
					continue;

				RemoteDiverController remote = kvp.Value.GetComponent<RemoteDiverController>();

				if (remote != null && remote.IsAlive)
					return remote.transform;
			}

			return null;
		}

		public List<RemoteDiverController> GetAllRemoteDivers()
		{
			List<RemoteDiverController> list = new List<RemoteDiverController>();

			foreach (var kvp in this._remoteDivers)
			{
				if (kvp.Value != null && kvp.Value.TryGetComponent<RemoteDiverController>(out var remote))
					list.Add(remote);
			}

			return list;
		}

		public List<RemoteDiverController> GetAliveRemoteDivers()
		{
			List<RemoteDiverController> list = new List<RemoteDiverController>();

			foreach (var kvp in this._remoteDivers)
			{
				if (kvp.Value == null)
					continue;

				RemoteDiverController remote = kvp.Value.GetComponent<RemoteDiverController>();

				if (remote != null && remote.IsAlive)
					list.Add(remote);
			}

			list.Sort((a, b) => a.PeerId.CompareTo(b.PeerId));
			return list;
		}

		public RemoteDiverController GetRemoteDiver(int peerId)
		{
			if (this._remoteDivers.TryGetValue(peerId, out GameObject go) && go != null)
				return go.GetComponent<RemoteDiverController>();

			return null;
		}

		private void Update()
		{
			if (NetworkManager.Instance != null && !ReferenceEquals(NetworkManager.Instance, this._subscribedNetworkManager))
				this.SubscribeToNetworkManager(NetworkManager.Instance);

			if (this._currentOrchestrator == null || !this._localDiverReady || this._subscribedNetworkManager == null)
				return;

			this._sendTimer += Time.deltaTime;

			if (this._sendTimer < SendIntervalSeconds)
				return;

			this._sendTimer = 0f;
			this.SendLocalSnapshot();
		}

		public void SendImmediateLocalSnapshot()
		{
			if (!this._localDiverReady || this._subscribedNetworkManager == null)
				return;

			this.SendLocalSnapshot();
		}

		private void SubscribeToNetworkManager(NetworkManager manager)
		{
			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
				this._subscribedNetworkManager.PeerDisconnected -= this.OnPeerDisconnected;
			}

			this._subscribedNetworkManager = manager;
			manager.MessageReceived += this.OnMessageReceived;
			manager.PeerDisconnected += this.OnPeerDisconnected;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			DivingOrchestrator orchestrator = FindObjectOfType<DivingOrchestrator>();

			if (orchestrator != null)
			{
				if (!ReferenceEquals(orchestrator, this._currentOrchestrator))
				{
					this._currentOrchestrator = orchestrator;
					this._localDiverReady = false;

					if (orchestrator.IsPlayerInitialized)
						this.OnLocalDiverReady();
					else
						orchestrator.PlayerInitialized += this.OnLocalDiverReady;
				}

				return;
			}

			if (this._currentOrchestrator != null)
			{
				this._currentOrchestrator.PlayerInitialized -= this.OnLocalDiverReady;
				this._currentOrchestrator = null;
				this.OnLocalDiveEnded();
			}
		}

		private void OnLocalDiverReady()
		{
			this._localDiverReady = true;
			this._sendTimer = 0f;
			this._localDeathAnimation = string.Empty;
		}

		private void OnLocalDiveEnded()
		{
			this._localDiverReady = false;
			this._localDeathAnimation = string.Empty;

			foreach (GameObject remote in this._remoteDivers.Values)
			{
				if (remote != null)
					Destroy(remote);
			}

			this._remoteDivers.Clear();
		}

		private void SendLocalSnapshot()
		{
			PlayerController player = Singleton<PlayerController>.Instance;

			if (player == null || player.Entity == null || player.RigidBody == null)
				return;

			Vector2 position = player.RigidBody.position;
			Vector2 velocity = player.RigidBody.velocity;

			DiverSnapshotMessage snapshot = new DiverSnapshotMessage
			{
				SourcePeerId = PeerInfo.HostPeerId,
				PosX = position.x,
				PosY = position.y,
				VelX = velocity.x,
				VelY = velocity.y,
				RotZ = player.RigidBody.rotation,
				ScaleY = player.transform.localScale.y,
				Health = player.Entity.GetAttributeValue(EntityAttributeID.HEALTH),
				Oxygen = player.Entity.GetAttributeValue(EntityAttributeID.OXYGEN),
				IsDead = player.IsDead,
				DeathAnimation = player.IsDead ? this._localDeathAnimation : string.Empty,
				Anim0 = player.GetAnimationName(0),
				Anim1 = player.GetAnimationName(1),
				PlayerName = NetworkManager.Instance?.LocalPlayerName ?? "Diver"
			};

			NetMessage message = snapshot.ToNetMessage();

			if (this._subscribedNetworkManager.IsHost)
				this._subscribedNetworkManager.Broadcast(message);
			else
				this._subscribedNetworkManager.SendToHost(message);
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			if (message.Type != MessageType.DiverSnapshot)
				return;

			if (this._currentOrchestrator == null || !this._localDiverReady)
				return;

			DiverSnapshotMessage snapshot = DiverSnapshotMessage.FromPayload(message.Payload);

			if (this._subscribedNetworkManager.IsHost)
			{
				snapshot.SourcePeerId = peerId;

				this.GetOrCreateRemoteDiver(snapshot.SourcePeerId).ApplySnapshot(snapshot);

				NetMessage relay = snapshot.ToNetMessage();
				this._subscribedNetworkManager.Broadcast(relay, snapshot.SourcePeerId);
			}
			else
			{
				this.GetOrCreateRemoteDiver(snapshot.SourcePeerId).ApplySnapshot(snapshot);
			}
		}

		private void OnPeerDisconnected(int peerId, string reason)
		{
			if (this._remoteDivers.TryGetValue(peerId, out GameObject remote))
			{
				if (remote != null)
					Destroy(remote);

				this._remoteDivers.Remove(peerId);
			}
		}

		private RemoteDiverController GetOrCreateRemoteDiver(int peerId)
		{
			if (this._remoteDivers.TryGetValue(peerId, out GameObject existing) && existing != null)
				return existing.GetComponent<RemoteDiverController>();

			GameObject clone = this.SpawnRemoteDiverVisual(peerId);
			this._remoteDivers[peerId] = clone;

			return clone.GetComponent<RemoteDiverController>();
		}

		private GameObject SpawnRemoteDiverVisual(int peerId)
		{
			GameObject template = Singleton<PlayerController>.Instance.gameObject;
			bool wasActive = template.activeSelf;

			template.SetActive(false);

			GameObject clone = Instantiate(template, template.transform.position, template.transform.rotation);

			template.SetActive(wasActive);

			clone.name = $"DiveOrDieCoop.RemoteDiver[{peerId}]";
			clone.layer = 2;

			DestroyImmediate(clone.GetComponent<PlayerController>());
			DestroyImmediate(clone.GetComponent<UnityEngine.InputSystem.PlayerInput>());

			foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (mb == null)
					continue;

				string typeName = mb.GetType().Name;

				if (typeName.StartsWith("Player") ||
					typeName.StartsWith("DiverIntro") ||
					typeName.Contains("Camera") ||
					typeName.Contains("Audio"))
				{
					DestroyImmediate(mb);
				}
			}

			// Remote divers only need the visual scooter attachment.
			// The real scooter component updates the shared toolbar UI
			// and would reset the local player's charge to 0.
			foreach (UsableItem_UnderwaterScooter scooter in clone.GetComponentsInChildren<UsableItem_UnderwaterScooter>(true))
				DestroyImmediate(scooter);

			Rigidbody2D rb = clone.GetComponent<Rigidbody2D>() ?? clone.AddComponent<Rigidbody2D>();
			rb.bodyType = RigidbodyType2D.Kinematic;
			rb.simulated = true;

			clone.SetActive(true);

			RemoteDiverController remote = clone.AddComponent<RemoteDiverController>();
			remote.Initialize(peerId);

			if (DiveTransitionManager.Instance != null &&
				DiveTransitionManager.Instance.PeerSurvivorClaims.TryGetValue(peerId, out string claimPayload))
			{
				string[] parts = claimPayload.Split('|');
				string skinId = parts.Length > 1 ? parts[1] : string.Empty;

				if (!string.IsNullOrEmpty(skinId))
				{
					remote.SetSkin(skinId);
				}
				else
				{
					SurvivorData claimed = Singleton<GameState>.Instance?.CampState?.GetSurvivor(parts[0]);

					if (claimed?.GraphicsData != null)
						remote.SetSkin(claimed.GraphicsData.SkinId);
				}
			}

			return clone;
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= this.OnSceneLoaded;

			if (this._subscribedNetworkManager != null)
			{
				this._subscribedNetworkManager.MessageReceived -= this.OnMessageReceived;
				this._subscribedNetworkManager.PeerDisconnected -= this.OnPeerDisconnected;
			}

			if (this._currentOrchestrator != null)
				this._currentOrchestrator.PlayerInitialized -= this.OnLocalDiverReady;

			if (Instance == this)
				Instance = null;
		}
	}
}