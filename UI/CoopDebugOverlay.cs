using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BepInEx.Logging;
using DiveOrDieCoop.Diving;
using DiveOrDieCoop.Hub;
using DiveOrDieCoop.Networking;
using DiveOrDieCoop.Networking.LAN;
using DiveOrDieCoop.Networking.Protocol;
#if STEAMWORKS_NET
using DiveOrDieCoop.Networking.Steam;
using Steamworks;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DiveOrDieCoop.UI
{
	public sealed class CoopDebugOverlay : MonoBehaviour
	{
		public static CoopDebugOverlay Instance { get; private set; }

		private const string Success = "#55ff55";
		private const string Info = "#55ffff";
		private const string Warning = "#ffaa55";
		private const string Danger = "#ff5555";
		private const string Muted = "#aaaaaa";
		private static readonly Color DangerButtonColor = new Color(1f, 0.4f, 0.4f);

		private int _selectedTab;
		private bool _visible;
		private Vector2 _scrollPos;
		private Rect _windowRect = new Rect(30, 30, 480, 520);
		private string _addressField = "127.0.0.1";
		private string _portField = "27511";
		private string _steamIdField = string.Empty;
		private string _status = string.Empty;
		private ManualLogSource _log;
		private readonly List<string> _logEntries = new List<string>();
		private readonly object _logLock = new object();
		private readonly object _discoveredLock = new object();
		private readonly List<DiscoveredHost> _discovered = new List<DiscoveredHost>();

		private void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
			Instance = this;
			this._log = BepInEx.Logging.Logger.CreateLogSource("DiveOrDieCoop.Overlay");
		}

		private void OnEnable()
		{
			if (NetworkManager.Instance != null) NetworkManager.Instance.StatusChanged += this.OnStatusChanged;
		}

		private void OnDisable()
		{
			if (NetworkManager.Instance != null) NetworkManager.Instance.StatusChanged -= this.OnStatusChanged;
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		public void OpenToSteamTab()
		{
			this._visible = true;
			this._selectedTab = 0;
#if STEAMWORKS_NET
			SteamLobbyManager.Instance?.RefreshLobbies();
#endif
		}

		private void Update()
		{
			bool toggle = false;

			if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
				toggle = true;
			else
			{
				try { if (Input.GetKeyDown(KeyCode.F6)) toggle = true; } catch { }
			}

			if (toggle) this._visible = !this._visible;
		}

		private void OnGUI()
		{
			if (!this._visible) return;

			this._windowRect = GUI.Window(
				99281,
				this._windowRect,
				this.DrawOverlayWindow,
				"Dive or Die CO-OP Config [F6]");
		}

		private void DrawOverlayWindow(int windowId)
		{
			NetworkManager net = NetworkManager.Instance;

			if (net == null)
			{
				GUILayout.Label("NetworkManager not ready.");
				GUI.DragWindow(new Rect(0, 0, 10000, 25));
				return;
			}

			this.DrawHeader(net);
			GUILayout.Space(6);
			this._selectedTab = GUILayout.Toolbar(this._selectedTab, new[] { "Steam", "LAN", "Peers & Session" });
			GUILayout.Space(6);

			this._scrollPos = GUILayout.BeginScrollView(this._scrollPos, GUILayout.Height(420));

			switch (this._selectedTab)
			{
				case 0:
					this.DrawSteamTab(net);
					break;
				case 1:
					this.DrawLanTab(net);
					break;
				case 2:
					this.DrawPeersTab(net);
					break;
			}

			GUILayout.EndScrollView();
			GUILayout.Space(4);
			this.DrawBottomBar(net);
			GUI.DragWindow(new Rect(0, 0, 10000, 25));
		}

		private void DrawHeader(NetworkManager net)
		{
			GUILayout.BeginHorizontal("box");
			GUILayout.Label($"<b>Status:</b> {this.GetStateBadge(net)}", GUILayout.Width(205));
			GUILayout.Label($"<b>Peers:</b> {net.ConnectedPeers.Count}/{net.MaxPlayers}", GUILayout.Width(95));
			GUILayout.Label($"<b>Role:</b> {this.GetRole(net)}");
			GUILayout.EndHorizontal();
		}

		private void DrawSteamTab(NetworkManager net)
		{
#if STEAMWORKS_NET
			bool steamReady = SteamRuntime.IsInitialized;

			GUILayout.Label($"<b>Steam Status:</b> {(steamReady ? $"<color={Success}>INITIALIZED</color>" : $"<color={Danger}>NOT INITIALIZED</color>")}");

			if (!steamReady)
			{
				GUILayout.Label($"<color={Warning}>Steam is offline or not running. LAN is still available.</color>");
				return;
			}

			CSteamID myId = SteamUser.GetSteamID();
			string myName = SteamFriends.GetPersonaName();
			GUILayout.Label($"<b>User:</b> {myName} ({myId.m_SteamID})");

			SteamLobbyManager lobbyMgr = SteamLobbyManager.Instance;

			if (lobbyMgr != null && lobbyMgr.CurrentLobby != CSteamID.Nil)
			{
				this.DrawActiveSteamLobby(lobbyMgr);
				return;
			}

			GUILayout.Space(8);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>Public Steam Lobbies</b>");
			GUILayout.FlexibleSpace();

			bool canBrowse = net.State == ConnectionState.Disconnected && !net.IsReconnecting;

			GUI.enabled = canBrowse;
			if (GUILayout.Button(lobbyMgr != null && lobbyMgr.IsRefreshing ? "SEARCHING..." : "REFRESH", GUILayout.Width(110)))
			{
				lobbyMgr?.RefreshLobbies();
			}
			GUI.enabled = true;

			GUILayout.EndHorizontal();

			if (lobbyMgr != null && !string.IsNullOrEmpty(lobbyMgr.Status))
				GUILayout.Label($"<i>{lobbyMgr.Status}</i>");

			if (lobbyMgr == null)
			{
				GUILayout.Label($"<color={Danger}>SteamLobbyManager unavailable.</color>");
			}
			else if (lobbyMgr.IsRefreshing)
			{
				GUILayout.BeginVertical("box");
				GUILayout.Label("<b>SEARCHING STEAM LOBBIES...</b>");
				GUILayout.Label($"<color={Muted}>Requesting current public CO-OP sessions.</color>");
				GUILayout.EndVertical();
			}
			else if (lobbyMgr.Lobbies.Count == 0)
			{
				GUILayout.BeginVertical("box");
				GUILayout.Label("<b>No public CO-OP lobbies found.</b>");
				GUILayout.Label($"<color={Muted}>Press REFRESH to search again.</color>");
				GUILayout.EndVertical();
			}
			else
			{
				this.DrawSteamLobbyList(lobbyMgr, canBrowse);
			}

			GUILayout.Space(8);
			GUILayout.Label("<b>Direct Join by SteamID64:</b>");
			GUILayout.BeginHorizontal();
			this._steamIdField = GUILayout.TextField(this._steamIdField);

			GUI.enabled = canBrowse && !string.IsNullOrEmpty(this._steamIdField.Trim());
			if (GUILayout.Button("CONNECT", GUILayout.Width(80)))
			{
				string id = this._steamIdField.Trim();
				net.ConnectSteam(id);
				this.AddLog($"Connecting to SteamID {id}.");
			}
			GUI.enabled = true;

			GUILayout.EndHorizontal();
#else
			GUILayout.Label("Steamworks support not enabled at compile time (STEAMWORKS_NET).");
#endif
		}

#if STEAMWORKS_NET
		private void DrawActiveSteamLobby(SteamLobbyManager lobbyMgr)
		{
			GUILayout.Space(8);
			GUILayout.Label("<b>Active Steam Lobby</b>");

			GUILayout.BeginVertical("box");
			GUILayout.Label($"<b>Lobby ID:</b> {lobbyMgr.CurrentLobby.m_SteamID}");
			GUILayout.Label($"<b>Role:</b> {(lobbyMgr.IsHostLobby ? $"<color={Success}>LOBBY OWNER</color>" : $"<color={Info}>MEMBER</color>")}");
			GUILayout.Label($"<b>Type:</b> {lobbyMgr.CurrentLobbyType}");

			if (!string.IsNullOrEmpty(lobbyMgr.Status))
				GUILayout.Label($"<b>Status:</b> {lobbyMgr.Status}");

			GUILayout.Space(6);
			GUILayout.BeginHorizontal();

			if (GUILayout.Button("INVITE FRIENDS"))
				SteamFriends.ActivateGameOverlayInviteDialog(lobbyMgr.CurrentLobby);

			if (GUILayout.Button("COPY ID"))
			{
				GUIUtility.systemCopyBuffer = lobbyMgr.CurrentLobby.m_SteamID.ToString();
				this.SetStatus("Lobby ID copied.");
			}

			GUILayout.EndHorizontal();

			if (lobbyMgr.IsHostLobby)
			{
				GUILayout.Space(4);
				GUILayout.Label("<b>Lobby Privacy</b>");
				GUILayout.BeginHorizontal();
				this.DrawLobbyPrivacyButton(lobbyMgr, ELobbyType.k_ELobbyTypePublic, "PUBLIC");
				this.DrawLobbyPrivacyButton(lobbyMgr, ELobbyType.k_ELobbyTypeFriendsOnly, "FRIENDS");
				this.DrawLobbyPrivacyButton(lobbyMgr, ELobbyType.k_ELobbyTypePrivate, "PRIVATE");
				GUILayout.EndHorizontal();
			}

			GUILayout.Space(6);
			GUI.color = DangerButtonColor;

			if (GUILayout.Button("LEAVE LOBBY", GUILayout.Height(26)))
			{
				this.AddLog("Disconnecting and leaving Steam lobby.");
				NetworkManager.Instance?.Disconnect();
			}

			GUI.color = Color.white;
			GUILayout.EndVertical();
		}

		private void DrawSteamLobbyList(SteamLobbyManager lobbyMgr, bool canBrowse)
		{
			List<SteamLobbyManager.LobbyEntry> lobbies = lobbyMgr.Lobbies
				.OrderByDescending(x => x.Players < x.MaxPlayers)
				.ThenByDescending(x => x.Players)
				.ThenBy(x => x.Name)
				.ToList();

			foreach (SteamLobbyManager.LobbyEntry lobby in lobbies)
			{
				int freeSlots = Mathf.Max(0, lobby.MaxPlayers - lobby.Players);
				bool full = freeSlots <= 0;

				GUILayout.BeginVertical("box");
				GUILayout.BeginHorizontal();

				GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				GUILayout.Label($"<b>{lobby.Name}</b>");

				string ownerName = this.GetLobbyOwnerName(lobby.Owner);
				GUILayout.Label($"<color={Muted}>{ownerName}  •  {lobby.Players}/{lobby.MaxPlayers} players</color>");
				GUILayout.Label($"<color={Muted}>ID: {lobby.LobbyId.m_SteamID}</color>");
				GUILayout.EndVertical();

				GUI.enabled = canBrowse && !full;

				if (full)
				{
					GUI.enabled = false;
					GUILayout.Button("FULL", GUILayout.Width(65), GUILayout.Height(32));
					GUI.enabled = true;
				}
				else if (GUILayout.Button("JOIN", GUILayout.Width(65), GUILayout.Height(32)))
				{
					lobbyMgr.JoinLobby(lobby.LobbyId);
					this.AddLog($"Joining lobby {lobby.LobbyId.m_SteamID}.");
				}

				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
		}

		private string GetLobbyOwnerName(CSteamID owner)
		{
			if (owner == CSteamID.Nil) return "Unknown owner";
			try
			{
				string name = SteamFriends.GetFriendPersonaName(owner);
				return string.IsNullOrEmpty(name) ? owner.m_SteamID.ToString() : name;
			}
			catch
			{
				return owner.m_SteamID.ToString();
			}
		}

		private void DrawLobbyPrivacyButton(SteamLobbyManager lobbyMgr, ELobbyType type, string label)
		{
			GUI.enabled = lobbyMgr.CurrentLobbyType != type;
			if (GUILayout.Button(label)) lobbyMgr.SetLobbyType(type);
			GUI.enabled = true;
		}
#endif

		private void DrawLanTab(NetworkManager net)
		{
			bool idle = net.State == ConnectionState.Disconnected && !net.IsReconnecting;

			GUILayout.Label("<b>LAN Player Name:</b>");
			net.LocalPlayerName = GUILayout.TextField(net.LocalPlayerName);

			GUILayout.Space(6);
			GUI.enabled = idle;
			if (GUILayout.Button("HOST LAN", GUILayout.Height(28)))
			{
				net.HostLan();
				this.AddLog("Started LAN host.");
			}
			GUI.enabled = true;

			GUILayout.Space(8);
			GUILayout.Label("<b>Direct Connect (IP : Port):</b>");
			GUILayout.BeginHorizontal();

			this._addressField = GUILayout.TextField(this._addressField);
			this._portField = GUILayout.TextField(this._portField, GUILayout.Width(70));

			GUI.enabled = idle;
			if (GUILayout.Button("CONNECT", GUILayout.Width(80)))
			{
				if (int.TryParse(this._portField, out int port))
				{
					net.ConnectLan(this._addressField.Trim(), port);
					this.AddLog($"Connecting to {this._addressField.Trim()}:{port}.");
				}
			}
			GUI.enabled = true;

			GUILayout.EndHorizontal();

			GUILayout.Space(8);
			if (GUILayout.Button("SEARCH LAN HOSTS", GUILayout.Height(26)))
				this.StartDiscovery(net.DiscoveryPort);

			List<DiscoveredHost> hosts = this.SnapshotDiscovered();

			if (hosts.Count > 0)
			{
				GUILayout.Space(4);
				GUILayout.Label("<b>Discovered LAN Hosts:</b>");

				foreach (DiscoveredHost host in hosts)
				{
					GUILayout.BeginHorizontal("box");
					GUILayout.Label($"{host.HostPlayerName}\n{host.Address}:{host.TcpPort} ({host.CurrentPlayers}/{host.MaxPlayers})", GUILayout.ExpandWidth(true));

					GUI.enabled = idle;
					if (GUILayout.Button("JOIN", GUILayout.Width(65), GUILayout.Height(32)))
					{
						net.ConnectLan(host.Address, host.TcpPort);
						this.AddLog($"Joining LAN host {host.Address}:{host.TcpPort}.");
					}
					GUI.enabled = true;

					GUILayout.EndHorizontal();
				}
			}
		}

		private void DrawPeersTab(NetworkManager net)
		{
			GUILayout.Label($"<b>Active Scene:</b> {SceneManager.GetActiveScene().name}");

			DiveTransitionManager transition = DiveTransitionManager.Instance;

			if (transition != null)
			{
				GUILayout.Space(6);
				GUILayout.BeginVertical("box");
				GUILayout.Label("<b>Session</b>");
				GUILayout.Label($"Local Peer: {net.LocalPeerId}");
				GUILayout.Label($"Dive Status: {transition.LocalStatus}");
				GUILayout.Label($"Local Survivor: {(string.IsNullOrEmpty(transition.ClaimedSurvivorId) ? "None" : transition.ClaimedSurvivorId)}");
				GUILayout.Label($"Active Surface: {transition.ActiveSurfaceSpotId}");
				GUILayout.EndVertical();
			}

			if (net.IsHost && HubActionSyncManager.Instance != null)
			{
				GUILayout.Space(6);
				HubActionSyncManager.Instance.AllowClientCampControl = GUILayout.Toggle(
					HubActionSyncManager.Instance.AllowClientCampControl,
					" Allow Clients Camp Control");
			}

			GUILayout.Space(8);
			GUILayout.Label("<b>Connected Players:</b>");

			if (net.ConnectedPeers.Count == 0)
			{
				GUILayout.Label($"<color={Muted}>No players connected.</color>");
			}
			else
			{
				int peerToKick = -1;

				foreach (var kvp in net.ConnectedPeers)
				{
					PeerInfo peer = kvp.Value;

					string claim = transition != null && transition.PeerSurvivorClaims.TryGetValue(peer.PeerId, out string c)
						? c
						: "None";

					bool ready = transition != null &&
						transition.PeerReadyStates.TryGetValue(peer.PeerId, out bool r) &&
						r;

					string surface = transition != null &&
						transition.PeerSurfaceSpotSelections.TryGetValue(peer.PeerId, out string s)
						? s
						: "None";

					GUILayout.BeginVertical("box");
					GUILayout.BeginHorizontal();

					GUILayout.Label($"<b>Peer [{peer.PeerId}]:</b> {peer.PlayerName}", GUILayout.ExpandWidth(true));

					if (net.IsHost)
					{
						GUI.color = DangerButtonColor;
						if (GUILayout.Button("KICK", GUILayout.Width(60), GUILayout.Height(22)))
							peerToKick = peer.PeerId;
						GUI.color = Color.white;
					}

					GUILayout.EndHorizontal();

					GUILayout.Label($"Survivor: {claim}");
					GUILayout.Label($"Surface: {surface}");
					GUILayout.Label($"Ready: {(ready ? $"<color={Success}>YES</color>" : $"<color={Warning}>NO</color>")}");

					GUILayout.EndVertical();
				}

				if (peerToKick >= 0)
				{
					net.KickPlayer(peerToKick, "Kicked by host.");
					this.AddLog($"Kicked peer {peerToKick}.");
				}
			}

			if (net.IsHost && HubSyncManager.Instance != null)
			{
				GUILayout.Space(8);
				if (GUILayout.Button("FORCE SYNC HUB STATE", GUILayout.Height(26)))
				{
					HubSyncManager.Instance.ForceSendSync();
					this.SetStatus("Sent full Hub state to all peers.");
				}
			}
		}

		private void DrawBottomBar(NetworkManager net)
		{
			GUILayout.BeginHorizontal();

			if (!string.IsNullOrEmpty(this._status))
				GUILayout.Label($"<i>{this._status}</i>", GUILayout.MaxWidth(300));
			else
				GUILayout.FlexibleSpace();

		#if STEAMWORKS_NET
			bool inSteamLobby = SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.CurrentLobby != CSteamID.Nil;
		#endif

			if (net.IsReconnecting)
			{
				if (GUILayout.Button("CANCEL", GUILayout.Width(70), GUILayout.Height(24)))
				{
					net.CancelReconnect();
					this.AddLog("Reconnect cancelled.");
				}
			}

		#if STEAMWORKS_NET
			if (net.TransportKind == NetworkTransportKind.Steam && inSteamLobby)
			{
				GUI.color = DangerButtonColor;
				if (GUILayout.Button("DISCONNECT & LEAVE", GUILayout.Width(130), GUILayout.Height(24)))
				{
					this.AddLog("Disconnecting and leaving Steam lobby.");
					net.Disconnect();
				}
				GUI.color = Color.white;
			}
			else
		#endif
			if (net.IsConnected || net.State == ConnectionState.Connecting || net.IsReconnecting)
			{
				GUI.color = DangerButtonColor;
				if (GUILayout.Button("DISCONNECT", GUILayout.Width(90), GUILayout.Height(24)))
				{
					this.AddLog("Disconnect requested.");
					net.Disconnect();
				}
				GUI.color = Color.white;
			}

			GUILayout.EndHorizontal();
		}

		private string GetStateBadge(NetworkManager net)
		{
			string suffix = net.TransportKind switch
			{
				NetworkTransportKind.Lan => " (LAN)",
				NetworkTransportKind.Steam => " (STEAM)",
				_ => string.Empty
			};

			switch (net.State)
			{
				case ConnectionState.Connected:
					return $"<color={Success}>CONNECTED{suffix}</color>";
				case ConnectionState.Hosting:
					return $"<color={Info}>HOSTING{suffix}</color>";
				case ConnectionState.Connecting:
					return $"<color={Warning}>CONNECTING{suffix}...</color>";
				default:
					return net.IsReconnecting
						? $"<color={Warning}>RECONNECTING</color>"
						: $"<color={Muted}>DISCONNECTED</color>";
			}
		}

		private string GetRole(NetworkManager net)
		{
			if (net.IsHost) return $"<color={Success}>HOST</color>";
			if (net.IsConnected) return $"<color={Info}>CLIENT</color>";
			return "OFFLINE";
		}

		private void OnStatusChanged(string status)
		{
			this.SetStatus(status);
			this.AddLog(status);
		}

		private void SetStatus(string status)
		{
			this._status = status ?? string.Empty;
		}

		private void AddLog(string message)
		{
			if (string.IsNullOrEmpty(message)) return;

			lock (this._logLock)
			{
				this._logEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
				if (this._logEntries.Count > 100) this._logEntries.RemoveAt(0);
			}
		}

		private void StartDiscovery(int discoveryPort)
		{
			lock (this._discoveredLock) this._discovered.Clear();

			Thread thread = new Thread(() =>
			{
				using (LanHostDiscovery discovery = new LanHostDiscovery())
				{
					discovery.HostFound += host =>
					{
						lock (this._discoveredLock)
						{
							this._discovered.RemoveAll(h => h.Address == host.Address && h.TcpPort == host.TcpPort);
							this._discovered.Add(host);
						}
					};

					discovery.DiscoverHosts(discoveryPort, TimeSpan.FromSeconds(2.5));
				}
			})
			{
				IsBackground = true,
				Name = "DiveOrDieCoop-DiscoverySearch"
			};

			thread.Start();
		}

		private List<DiscoveredHost> SnapshotDiscovered()
		{
			lock (this._discoveredLock) return new List<DiscoveredHost>(this._discovered);
		}

		private void DrawLogTab()
		{
			GUILayout.Label("<b>SESSION LOG</b>");

			lock (this._logLock)
			{
				if (this._logEntries.Count == 0)
				{
					GUILayout.Label($"<color={Muted}>No overlay events.</color>");
					return;
				}

				for (int i = this._logEntries.Count - 1; i >= 0; i--)
					GUILayout.Label(this._logEntries[i]);
			}
		}
	}
}