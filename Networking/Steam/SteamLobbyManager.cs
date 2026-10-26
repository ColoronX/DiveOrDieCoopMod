#if STEAMWORKS_NET
using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace DiveOrDieCoop.Networking.Steam
{
	public sealed class SteamLobbyManager : MonoBehaviour
	{
		public static SteamLobbyManager Instance { get; private set; }

		public struct LobbyEntry
		{
			public CSteamID LobbyId;
			public string Name;
			public int Players;
			public int MaxPlayers;
			public CSteamID Owner;
		}

		public bool IsReady => _initialized;
		public bool IsHostLobby => _currentLobby != CSteamID.Nil && SteamRuntime.IsInitialized && SteamMatchmaking.GetLobbyOwner(_currentLobby) == SteamUser.GetSteamID();
		public CSteamID CurrentLobby => _currentLobby;
		public ELobbyType CurrentLobbyType { get; private set; } = ELobbyType.k_ELobbyTypePublic;
		public IReadOnlyList<LobbyEntry> Lobbies => _lobbies;
		public bool IsRefreshing { get; private set; }
		public string Status { get; private set; } = string.Empty;

		private readonly List<LobbyEntry> _lobbies = new List<LobbyEntry>();
		private CSteamID _currentLobby = CSteamID.Nil;
		private int _pendingMaxPlayers = 4;
		private string _pendingHostName = "Diver";
		private bool _initialized;
		private bool _joinAfterLobbyEnter;

		private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequested;
		private Callback<LobbyEnter_t> _lobbyEnter;
		private CallResult<LobbyCreated_t> _lobbyCreated;
		private CallResult<LobbyMatchList_t> _lobbyMatchList;

		private const string LobbyTypeKey = "diveordiecoop";
		private const string LobbyVersionKey = "version";
		private const string LobbyNameKey = "name";
		private const string LobbyHostKey = "host_steamid";
		private const string LobbyMaxPlayersKey = "max_players";

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}

			Instance = this;
			DontDestroyOnLoad(gameObject);
		}

		public void Initialize()
		{
			if (_initialized) return;
			if (!SteamRuntime.IsInitialized)
			{
				Status = "Steam is not initialized.";
				return;
			}

			_gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
			_lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
			_lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
			_lobbyMatchList = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
			_initialized = true;

			ProcessLaunchCommandLine();
		}

		public void CreateHostLobby(int maxPlayers, string hostPlayerName, ELobbyType lobbyType = ELobbyType.k_ELobbyTypePublic)
		{
			Initialize();
			if (!_initialized || !SteamRuntime.IsInitialized) return;

			LeaveCurrentLobby();
			_pendingMaxPlayers = Mathf.Clamp(maxPlayers, 2, 12);
			_pendingHostName = string.IsNullOrEmpty(hostPlayerName) ? "Diver" : hostPlayerName;
			CurrentLobbyType = lobbyType;
			Status = "Creating Steam lobby...";

			SteamAPICall_t call = SteamMatchmaking.CreateLobby(lobbyType, _pendingMaxPlayers);
			_lobbyCreated.Set(call);
		}

		public void SetLobbyType(ELobbyType lobbyType)
		{
			if (!IsHostLobby || _currentLobby == CSteamID.Nil || !SteamRuntime.IsInitialized) return;

			CurrentLobbyType = lobbyType;
			SteamMatchmaking.SetLobbyType(_currentLobby, lobbyType);
			Status = $"Lobby privacy set to: {lobbyType}";
		}

		public void RefreshLobbies()
		{
			Initialize();
			if (!_initialized || !SteamRuntime.IsInitialized) return;

			_lobbies.Clear();
			IsRefreshing = true;
			Status = "Searching for Steam lobbies...";

			SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyTypeKey, PluginInfo.GUID, ELobbyComparison.k_ELobbyComparisonEqual);
			SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyVersionKey, PluginInfo.VERSION, ELobbyComparison.k_ELobbyComparisonEqual);
			SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
			SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
			SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);

			SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
			_lobbyMatchList.Set(call);
		}

		public void JoinLobby(CSteamID lobbyId)
		{
			Initialize();
			if (!_initialized || lobbyId == CSteamID.Nil || !SteamRuntime.IsInitialized) return;

			CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
			if (owner == SteamUser.GetSteamID()) return;

			NetworkManager net = NetworkManager.Instance;
			if (net != null && net.IsConnected) return;

			if (_currentLobby != CSteamID.Nil && _currentLobby != lobbyId)
				LeaveCurrentLobby();

			_joinAfterLobbyEnter = true;
			Status = "Joining Steam lobby...";
			SteamMatchmaking.JoinLobby(lobbyId);
		}

		public void LeaveCurrentLobby()
		{
			CSteamID lobby = _currentLobby;
			_currentLobby = CSteamID.Nil;
			_joinAfterLobbyEnter = false;

			if (lobby == CSteamID.Nil || !SteamRuntime.IsInitialized) return;

			try { SteamMatchmaking.LeaveLobby(lobby); } catch { }
		}

		public void InviteFriend(CSteamID friend)
		{
			if (!_initialized || _currentLobby == CSteamID.Nil || !SteamRuntime.IsInitialized) return;
			SteamMatchmaking.InviteUserToLobby(_currentLobby, friend);
		}

		private void OnLobbyCreated(LobbyCreated_t callback, bool ioFailure)
		{
			if (ioFailure || callback.m_eResult != EResult.k_EResultOK)
			{
				Status = $"Steam lobby creation failed: {callback.m_eResult}";
				return;
			}

			_currentLobby = new CSteamID(callback.m_ulSteamIDLobby);

			SteamMatchmaking.SetLobbyData(_currentLobby, LobbyTypeKey, PluginInfo.GUID);
			SteamMatchmaking.SetLobbyData(_currentLobby, LobbyVersionKey, PluginInfo.VERSION);
			SteamMatchmaking.SetLobbyData(_currentLobby, LobbyNameKey, $"{_pendingHostName}'s Expedition");
			SteamMatchmaking.SetLobbyData(_currentLobby, LobbyHostKey, SteamUser.GetSteamID().m_SteamID.ToString());
			SteamMatchmaking.SetLobbyData(_currentLobby, LobbyMaxPlayersKey, _pendingMaxPlayers.ToString());
			SteamMatchmaking.SetLobbyJoinable(_currentLobby, true);

			Status = $"Steam lobby created: {_currentLobby}";
		}

		private void OnLobbyMatchList(LobbyMatchList_t callback, bool ioFailure)
		{
			IsRefreshing = false;
			_lobbies.Clear();

			if (ioFailure)
			{
				Status = "Steam lobby search failed (I/O Failure).";
				return;
			}

			for (int i = 0; i < callback.m_nLobbiesMatching; i++)
			{
				CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
				int players = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
				int maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
				CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
				string name = SteamMatchmaking.GetLobbyData(lobbyId, LobbyNameKey);

				_lobbies.Add(new LobbyEntry
				{
					LobbyId = lobbyId,
					Name = string.IsNullOrEmpty(name) ? "Diver Expedition" : name,
					Players = players,
					MaxPlayers = maxPlayers,
					Owner = owner
				});
			}

			Status = $"Found {_lobbies.Count} open lobby/lobbies.";
		}

		private void OnLobbyEnter(LobbyEnter_t callback)
		{
			if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
			{
				_joinAfterLobbyEnter = false;
				Status = $"Failed to enter lobby: {callback.m_EChatRoomEnterResponse}";
				return;
			}

			_currentLobby = new CSteamID(callback.m_ulSteamIDLobby);
			CSteamID owner = SteamMatchmaking.GetLobbyOwner(_currentLobby);

			if (_joinAfterLobbyEnter && owner != SteamUser.GetSteamID())
			{
				_joinAfterLobbyEnter = false;
				Status = "Lobby joined. Connecting to host...";
				NetworkManager.Instance?.ConnectSteam(owner.m_SteamID.ToString());
			}
			else
			{
				_joinAfterLobbyEnter = false;
			}

			Status = $"Entered lobby {_currentLobby}.";
		}

		private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
		{
			JoinLobby(callback.m_steamIDLobby);
		}

		private void ProcessLaunchCommandLine()
		{
			try
			{
				if (SteamApps.GetLaunchCommandLine(out string commandLine, 1024) > 0 && !string.IsNullOrEmpty(commandLine))
				{
					const string prefix = "+connect_lobby ";
					int idx = commandLine.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
					if (idx >= 0)
					{
						string value = commandLine.Substring(idx + prefix.Length).Trim().Split(' ')[0];
						if (ulong.TryParse(value, out ulong lobbyId))
							JoinLobby(new CSteamID(lobbyId));
					}
				}
			}
			catch { }
		}

		private void OnDestroy()
		{
			if (_currentLobby != CSteamID.Nil && SteamRuntime.IsInitialized)
				LeaveCurrentLobby();

			try
			{
				_gameLobbyJoinRequested?.Unregister();
				_lobbyEnter?.Unregister();
				_lobbyCreated?.Cancel();
				_lobbyMatchList?.Cancel();
			}
			catch { }

			if (Instance == this) Instance = null;
		}
	}
}
#endif