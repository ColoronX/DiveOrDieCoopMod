using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BepInEx.Logging;
using DiveOrDieCoop.Networking.LAN;
using DiveOrDieCoop.Networking.Protocol;
#if STEAMWORKS_NET
using DiveOrDieCoop.Networking.Steam;
#endif
using DRS.Patterns;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.Networking
{
	public enum NetworkTransportKind { None, Lan, Steam }

	public sealed class NetworkManager : MonoBehaviour
	{
		public static NetworkManager Instance { get; private set; }

		public ConnectionState State => this._transport?.State ?? ConnectionState.Disconnected;
		public bool IsHost => this._transport?.IsHost ?? false;
		public int LocalPeerId => this._transport?.LocalPeerId ?? PeerInfo.HostPeerId;
		public bool IsConnected => this.State == ConnectionState.Connected || this.State == ConnectionState.Hosting;
		public NetworkTransportKind TransportKind { get; private set; } = NetworkTransportKind.None;
		public bool IsMultiplayerActive { get; private set; }
		public IReadOnlyDictionary<int, PeerInfo> ConnectedPeers => this._connectedPeers;
		public bool IsReconnecting { get; private set; }
		public int ReconnectAttempt { get; private set; }

		public const int MaxReconnectAttempts = 5;
		public const float ReconnectRetryDelaySeconds = 3f;

		public event Action<PeerInfo> PeerConnected;
		public event Action<int, string> PeerDisconnected;
		public event Action<int, NetMessage> MessageReceived;
		public event Action<RejectMessage> ConnectionRejected;
		public event Action<string> StatusChanged;
		public event Action ReconnectSucceeded;
		public event Action ReconnectExhausted;

		public string LocalPlayerName = "Diver";
		public Guid LocalPlayerId = Guid.NewGuid();
		public int Port = 27511;
		public int DiscoveryPort = LanHostDiscovery.DefaultDiscoveryPort;
		public int MaxPlayers = 4;

		private ITransport _transport;
		private LanHostDiscovery _discovery;
		private ManualLogSource _log;
		private string _lastConnectAddress;
		private int _lastConnectPort;
		private float _reconnectTimer;
		private bool _loggedFirstUpdate;
		private bool _userInitiatedDisconnect;
		private bool _isKickingToMenu;
		private readonly ConcurrentQueue<Action> _inbox = new ConcurrentQueue<Action>();
		private readonly Dictionary<int, PeerInfo> _connectedPeers = new Dictionary<int, PeerInfo>();

		private void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
			Instance = this;
			this.gameObject.hideFlags = HideFlags.HideAndDontSave;
			DontDestroyOnLoad(this.gameObject);
			this._log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.NAME);
			SceneManager.sceneLoaded += this.OnSceneLoaded;
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name != "_MainMenu") return;
			this.IsMultiplayerActive = false;
			this._isKickingToMenu = false;
		}

		public void HostLan()
		{
			this.StopCurrentSession(false, false);
			this.IsMultiplayerActive = true;
			this.TransportKind = NetworkTransportKind.Lan;
			this.SetTransport(new LanTransport());
			this._discovery = new LanHostDiscovery();
			this._discovery.StartResponder(this.DiscoveryPort, () => new DiscoveryInfo
			{
				HostPlayerName = this.LocalPlayerName,
				TcpPort = this.Port,
				CurrentPlayers = this._connectedPeers.Count,
				MaxPlayers = this.MaxPlayers
			});

			this._transport.StartHost(new HostStartParams
			{
				Port = this.Port,
				MaxPlayers = this.MaxPlayers,
				LocalPlayerId = this.LocalPlayerId,
				LocalPlayerName = this.LocalPlayerName
			});

			this.RaiseStatus($"Hosting on port {this.Port} (max {this.MaxPlayers} players).");
		}

		public void ConnectLan(string address, int port)
		{
			this.StopCurrentSession(false, false);
			this.IsMultiplayerActive = true;
			this.TransportKind = NetworkTransportKind.Lan;
			this.SetTransport(new LanTransport());
			this._lastConnectAddress = address;
			this._lastConnectPort = port;

			this._transport.Connect(new ClientConnectParams
			{
				Address = address,
				Port = port,
				LocalPlayerId = this.LocalPlayerId,
				LocalPlayerName = this.LocalPlayerName
			});

			this.RaiseStatus($"Connecting to {address}:{port}...");
		}

#if STEAMWORKS_NET
		public void HostSteam()
		{
			if (!SteamRuntime.IsInitialized)
			{
				this.RaiseStatus("Steam is not running or initialized.");
				return;
			}

			this.StopCurrentSession(false, true);
			this.IsMultiplayerActive = true;
			this.TransportKind = NetworkTransportKind.Steam;
			this.SetTransport(new SteamTransport());

			this._transport.StartHost(new HostStartParams
			{
				Port = 0,
				MaxPlayers = this.MaxPlayers,
				LocalPlayerId = this.LocalPlayerId,
				LocalPlayerName = this.LocalPlayerName
			});

			SteamLobbyManager.Instance?.CreateHostLobby(this.MaxPlayers, this.LocalPlayerName);
			this.RaiseStatus($"Hosting through Steam (max {this.MaxPlayers} players).");
		}

		public void ConnectSteam(string hostSteamId)
		{
			if (!SteamRuntime.IsInitialized || string.IsNullOrWhiteSpace(hostSteamId))
				return;

			if (this._transport != null &&
				!this.IsHost &&
				this._lastConnectAddress == hostSteamId &&
				(this._transport.State == ConnectionState.Connecting ||
				 this._transport.State == ConnectionState.Connected ||
				 this.IsReconnecting))
			{
				this._log.LogInfo($"ConnectSteam({hostSteamId}) ignored: already connecting/connected.");
				return;
			}

			this._userInitiatedDisconnect = false;
			this.CancelReconnect();
			this.DetachTransport();
			this._connectedPeers.Clear();
			this.IsMultiplayerActive = true;
			this.TransportKind = NetworkTransportKind.Steam;
			this.SetTransport(new SteamTransport());
			this._lastConnectAddress = hostSteamId;
			this._lastConnectPort = 0;

			this._transport.Connect(new ClientConnectParams
			{
				Address = hostSteamId,
				Port = 0,
				LocalPlayerId = this.LocalPlayerId,
				LocalPlayerName = this.LocalPlayerName
			});

			this.RaiseStatus($"Connecting through Steam to {hostSteamId}...");
		}

		public void PumpSteamTransport()
		{
			if (this._transport is SteamTransport steamTransport)
				steamTransport.Pump();
		}
#endif

		public void KickPlayer(int peerId, string reason = "Kicked by host")
		{
			if (!this.IsHost) return;

#if STEAMWORKS_NET
			if (this._transport is SteamTransport steamTransport)
				steamTransport.KickPeer(peerId, reason);
#endif
			if (this._transport is LanTransport lanTransport)
				lanTransport.KickPeer(peerId, reason);

			this._connectedPeers.Remove(peerId);
			this.PeerDisconnected?.Invoke(peerId, reason);
			this.RaiseStatus($"Kicked peer {peerId} ({reason}).");
		}

		public void Disconnect()
		{
			string scene = SceneManager.GetActiveScene().name;
			bool wasActive = this.IsMultiplayerActive;
			bool wasSteam = this.TransportKind == NetworkTransportKind.Steam;

			if (!this.IsHost && this._transport != null && this.State == ConnectionState.Connected)
			{
				try { this._transport.SendToHost(new NetMessage(MessageType.Disconnect, Array.Empty<byte>())); } catch { }
			}

			this.StopCurrentSession(true, wasSteam);

			if (wasActive && (scene == "_Hub" || scene == "_Diving"))
				this.ReturnToMainMenu();
		}

		private void StopCurrentSession(bool userInitiated, bool leaveSteamLobby)
		{
			this._userInitiatedDisconnect = userInitiated;
			this.IsMultiplayerActive = false;
			this.TransportKind = NetworkTransportKind.None;
			this.CancelReconnect();

#if STEAMWORKS_NET
			if (leaveSteamLobby)
				SteamLobbyManager.Instance?.LeaveCurrentLobby();
#endif

			this._transport?.Disconnect();
			this.DetachTransport();
			this._discovery?.StopResponder();
			this._discovery = null;
			this._connectedPeers.Clear();
			this.RaiseStatus("Disconnected.");
		}

		public void CancelReconnect()
		{
			this.IsReconnecting = false;
			this.ReconnectAttempt = 0;
			this._reconnectTimer = 0f;
		}

		public void ReconnectNow()
		{
			if (!this.IsReconnecting || this.IsHost || this.TransportKind != NetworkTransportKind.Steam || string.IsNullOrEmpty(this._lastConnectAddress))
				return;

			if (this._transport == null)
				return;

			if (this._transport.State != ConnectionState.Disconnected)
				return;

			this._transport.Connect(new ClientConnectParams
			{
				Address = this._lastConnectAddress,
				Port = 0,
				LocalPlayerId = this.LocalPlayerId,
				LocalPlayerName = this.LocalPlayerName
			});

			this.RaiseStatus($"Reconnecting to {this._lastConnectAddress} (attempt {this.ReconnectAttempt}/{MaxReconnectAttempts})...");
		}

		public void SendToHost(NetMessage message) => this._transport?.SendToHost(message);
		public void SendToPeer(int peerId, NetMessage message) => this._transport?.Send(peerId, message);

		public void Broadcast(NetMessage message, int excludePeerId = -1)
		{
			if (this._transport == null || !this.IsHost) return;

			foreach (int peerId in this._connectedPeers.Keys)
				if (peerId != excludePeerId)
					this._transport.Send(peerId, message);
		}

		private void SetTransport(ITransport transport)
		{
			this.DetachTransport();
			this._transport = transport;
			this._transport.PeerConnected += peer => this._inbox.Enqueue(() => this.OnPeerConnected(peer));
			this._transport.PeerDisconnected += (id, reason) => this._inbox.Enqueue(() => this.OnPeerDisconnected(id, reason));
			this._transport.MessageReceived += (id, msg) => this._inbox.Enqueue(() => this.OnMessageReceived(id, msg));
			this._transport.ConnectionRejected += reject => this._inbox.Enqueue(() => this.OnConnectionRejected(reject));
			this._transport.TransportError += ex => this._inbox.Enqueue(() => this.OnTransportError(ex));
		}

		private void DetachTransport()
		{
			this._transport?.Dispose();
			this._transport = null;
		}

		private void OnMessageReceived(int peerId, NetMessage message)
		{
			if (this.IsHost && message?.Type == MessageType.Disconnect)
			{
#if STEAMWORKS_NET
				if (this._transport is SteamTransport steamTransport)
					steamTransport.RemovePeer(peerId, "client disconnected");
#endif
				this.RemovePeer(peerId, "client disconnected");
				return;
			}

			this.MessageReceived?.Invoke(peerId, message);
		}

		private void OnPeerConnected(PeerInfo peer)
		{
			this._userInitiatedDisconnect = false;
			this._connectedPeers[peer.PeerId] = peer;
			this._log.LogInfo($"Peer connected: {peer.PlayerName} (peerId={peer.PeerId}, slot={peer.SlotIndex})");
			this.RaiseStatus($"Connected: {peer.PlayerName}");

			if (this.IsReconnecting && !this.IsHost && peer.PeerId == PeerInfo.HostPeerId)
			{
				this.CancelReconnect();
				this.ReconnectSucceeded?.Invoke();
			}

			this.PeerConnected?.Invoke(peer);
		}

		private void OnPeerDisconnected(int peerId, string reason)
		{
			this.RemovePeer(peerId, reason);

			bool isKick = !string.IsNullOrEmpty(reason) &&
				(reason.IndexOf("kick", StringComparison.OrdinalIgnoreCase) >= 0 ||
				 reason.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0);

			bool unexpectedHostDrop =
				!this.IsHost &&
				peerId == PeerInfo.HostPeerId &&
				!this._userInitiatedDisconnect &&
				!isKick &&
				this.TransportKind == NetworkTransportKind.Steam;

			this._userInitiatedDisconnect = false;

			if (unexpectedHostDrop && !string.IsNullOrEmpty(this._lastConnectAddress))
			{
				this.ReconnectAttempt = 0;
				this._reconnectTimer = 0f;
				this.IsReconnecting = true;
				this.RaiseStatus("Connection lost. Steam lobby retained.");
			}
			else if (isKick)
			{
				this.CancelReconnect();
				this.Disconnect();
			}
		}

		private void RemovePeer(int peerId, string reason)
		{
			bool existed = this._connectedPeers.Remove(peerId);
			if (existed)
				this.RaiseStatus($"Peer {peerId} disconnected ({reason}).");

			this.PeerDisconnected?.Invoke(peerId, reason);
		}

		private void OnConnectionRejected(RejectMessage reject)
		{
			this._log.LogWarning($"Connection rejected/kicked: {reject.Reason} - {reject.Detail}");
			this.RaiseStatus($"Disconnected: {reject.Detail}");
			this._userInitiatedDisconnect = true;
			this.CancelReconnect();
			this.ConnectionRejected?.Invoke(reject);
			this.StopCurrentSession(true, this.TransportKind == NetworkTransportKind.Steam);
			this.ReturnToMainMenu();
		}

		private void OnTransportError(Exception ex)
		{
			this._log.LogError($"Transport error: {ex}");
			this.RaiseStatus($"Network error: {ex.Message}");
		}

		private void RaiseStatus(string status)
		{
			this._log.LogInfo(status);
			this.StatusChanged?.Invoke(status);
		}

		private void Update()
		{
			this.TickReconnect();

			int processed = 0;
			while (processed < 100 && this._inbox.TryDequeue(out Action action))
			{
				processed++;
				try { action(); }
				catch (Exception ex) { this._log.LogError($"Error handling queued network event: {ex}"); }
			}

			if (!this._loggedFirstUpdate)
			{
				this._loggedFirstUpdate = true;
				this._log.LogInfo($"NetworkManager.Update first ran at frame {Time.frameCount}.");
			}

			if (this.IsMultiplayerActive && this.State == ConnectionState.Disconnected && !this.IsReconnecting && !this._isKickingToMenu)
			{
				string scene = SceneManager.GetActiveScene().name;
				if (scene == "_Hub" || scene == "_Diving")
				{
					this._isKickingToMenu = true;
					this.ReturnToMainMenu();
				}
			}
			else if (this.State != ConnectionState.Disconnected)
			{
				this._isKickingToMenu = false;
			}
		}

		private void TickReconnect()
		{
			if (!this.IsReconnecting || this._transport == null || this._transport.State != ConnectionState.Disconnected)
				return;

			this._reconnectTimer += Time.deltaTime;
			if (this._reconnectTimer < ReconnectRetryDelaySeconds)
				return;

			this._reconnectTimer = 0f;
			this.ReconnectAttempt++;

			if (this.ReconnectAttempt > MaxReconnectAttempts)
			{
				this.IsReconnecting = false;
				this.ReconnectAttempt = 0;
				this.RaiseStatus("Could not reconnect to host. Please rejoin manually.");
				this.ReconnectExhausted?.Invoke();
				return;
			}

			this.ReconnectNow();
		}

		private void ReturnToMainMenu()
		{
			if (this._isKickingToMenu) return;
			this._isKickingToMenu = true;
			this.IsMultiplayerActive = false;

			if (Singleton<GameFlowManager>.HasInstance())
				Singleton<GameFlowManager>.Instance.ReleaseAllLockLevels();

			if (Singleton<CursorManager>.HasInstance())
				Singleton<CursorManager>.Instance.SetCursorState(true);

			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			Time.timeScale = 1f;

			if (Singleton<SceneLoader>.HasInstance() && Enum.TryParse("MainMenu", out SceneLoader.eScene mainMenuScene))
			{
				Singleton<SceneLoader>.Instance.LoadScene(mainMenuScene, false, true, false);
				return;
			}

			SceneManager.LoadScene("_MainMenu");
		}

		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= this.OnSceneLoaded;
			this.DetachTransport();
			this._discovery?.Dispose();
			if (Instance == this) Instance = null;
		}
	}
}