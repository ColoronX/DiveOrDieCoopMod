#if STEAMWORKS_NET
using System;
using System.Collections.Generic;
using System.IO;
using DiveOrDieCoop.Networking.Protocol;
using Steamworks;

namespace DiveOrDieCoop.Networking.Steam
{
	public sealed class SteamTransport : ITransport
	{
		public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
		public bool IsHost { get; private set; }
		public int LocalPeerId { get; private set; } = PeerInfo.HostPeerId;

		public event Action<PeerInfo> PeerConnected;
		public event Action<int, string> PeerDisconnected;
		public event Action<int, NetMessage> MessageReceived;
		public event Action<RejectMessage> ConnectionRejected;
		public event Action<Exception> TransportError;

		private const int P2PChannel = 0;

		private sealed class SteamPeer
		{
			public int PeerId;
			public CSteamID SteamId;
			public bool HelloReceived;
			public Guid PlayerId;
			public string PlayerName = "Diver";
		}

		private readonly Dictionary<int, SteamPeer> _peersById = new Dictionary<int, SteamPeer>();
		private readonly Dictionary<ulong, SteamPeer> _peersBySteamId = new Dictionary<ulong, SteamPeer>();

		private Callback<P2PSessionRequest_t> _sessionRequestCallback;
		private Callback<P2PSessionConnectFail_t> _sessionConnectFailCallback;

		private HostStartParams _hostParams;
		private ClientConnectParams _clientParams;
		private CSteamID _hostSteamId = CSteamID.Nil;
		private int _nextPeerId = 1;
		private bool _disposed;
		private bool _clientPeerRaised;

		public SteamTransport()
		{
			_sessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
			_sessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
		}

		public void StartHost(HostStartParams startParams)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SteamTransport));
			if (State != ConnectionState.Disconnected) throw new InvalidOperationException($"Cannot StartHost while transport is {State}.");

			_hostParams = startParams ?? throw new ArgumentNullException(nameof(startParams));
			IsHost = true;
			LocalPeerId = PeerInfo.HostPeerId;
			State = ConnectionState.Hosting;
			_nextPeerId = 1;
			_peersById.Clear();
			_peersBySteamId.Clear();
		}

		public void StopHost() => Disconnect();

		public void Connect(ClientConnectParams connectParams)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(SteamTransport));
			if (State != ConnectionState.Disconnected) throw new InvalidOperationException($"Cannot Connect while transport is {State}.");
			if (connectParams == null) throw new ArgumentNullException(nameof(connectParams));
			if (!ulong.TryParse(connectParams.Address, out ulong steamIdValue) || steamIdValue == 0) throw new ArgumentException("Steam client Address must contain a valid 64-bit SteamID.", nameof(connectParams));

			_clientParams = connectParams;
			_hostSteamId = new CSteamID(steamIdValue);
			IsHost = false;
			LocalPeerId = -1;
			State = ConnectionState.Connecting;
			_clientPeerRaised = false;

			if (!SteamRuntime.IsInitialized) return;

			SteamNetworking.AcceptP2PSessionWithUser(_hostSteamId);

			byte[] helloBytes = new HelloMessage
			{
				ProtocolVersion = ProtocolVersion.Current,
				PlayerId = _clientParams.LocalPlayerId,
				PlayerName = _clientParams.LocalPlayerName
			}.ToNetMessage().Serialize();

			if (!SteamNetworking.SendP2PPacket(_hostSteamId, helloBytes, (uint)helloBytes.Length, EP2PSend.k_EP2PSendReliable, P2PChannel))
			{
				State = ConnectionState.Disconnected;
				TransportError?.Invoke(new Exception("Failed to send Steam handshake."));
			}
		}

		public void Send(int peerId, NetMessage message)
		{
			if (message == null || !SteamRuntime.IsInitialized) return;

			byte[] payload = message.Serialize();

			if (IsHost)
			{
				if (!_peersById.TryGetValue(peerId, out SteamPeer peer)) return;
				SteamNetworking.SendP2PPacket(peer.SteamId, payload, (uint)payload.Length, EP2PSend.k_EP2PSendReliable, P2PChannel);
			}
			else
			{
				if (peerId != PeerInfo.HostPeerId || _hostSteamId == CSteamID.Nil) return;
				SteamNetworking.SendP2PPacket(_hostSteamId, payload, (uint)payload.Length, EP2PSend.k_EP2PSendReliable, P2PChannel);
			}
		}

		public void SendToHost(NetMessage message) => Send(PeerInfo.HostPeerId, message);

		public void Pump()
		{
			if (_disposed || State == ConnectionState.Disconnected || !SteamRuntime.IsInitialized) return;

			try
			{
				while (SteamNetworking.IsP2PPacketAvailable(out uint msgSize, P2PChannel))
				{
					byte[] buffer = new byte[msgSize];
					if (!SteamNetworking.ReadP2PPacket(buffer, msgSize, out uint bytesRead, out CSteamID remoteSteamId, P2PChannel)) break;

					NetMessage message;
					using (MemoryStream ms = new MemoryStream(buffer, 0, (int)bytesRead, false))
						message = NetMessage.ReadFrom(ms);

					if (message != null) ProcessIncomingMessage(remoteSteamId, message);
				}
			}
			catch (Exception ex)
			{
				if (!_disposed) TransportError?.Invoke(ex);
			}
		}

		private void ProcessIncomingMessage(CSteamID remoteSteamId, NetMessage message)
		{
			if (IsHost)
			{
				ulong key = remoteSteamId.m_SteamID;

				if (message.Type == MessageType.Hello)
				{
					HelloMessage hello = HelloMessage.FromPayload(message.Payload);

					if (hello.ProtocolVersion != ProtocolVersion.Current)
					{
						SendRawToSteamId(remoteSteamId, new RejectMessage
						{
							Reason = RejectReason.ProtocolMismatch,
							Detail = "Protocol version mismatch."
						}.ToNetMessage());
						try { SteamNetworking.CloseP2PSessionWithUser(remoteSteamId); } catch { }
						return;
					}

					bool existing = _peersBySteamId.ContainsKey(key);
					if (_peersById.Count >= _hostParams.MaxPlayers && !existing)
					{
						SendRawToSteamId(remoteSteamId, new RejectMessage
						{
							Reason = RejectReason.LobbyFull,
							Detail = "Lobby is full."
						}.ToNetMessage());
						try { SteamNetworking.CloseP2PSessionWithUser(remoteSteamId); } catch { }
						return;
					}

					if (!_peersBySteamId.TryGetValue(key, out SteamPeer peer))
					{
						int assignedId = _nextPeerId++;
						peer = new SteamPeer
						{
							PeerId = assignedId,
							SteamId = remoteSteamId,
							PlayerId = hello.PlayerId,
							PlayerName = hello.PlayerName,
							HelloReceived = true
						};

						_peersById[assignedId] = peer;
						_peersBySteamId[key] = peer;

						SendRawToSteamId(remoteSteamId, new WelcomeMessage
						{
							HostPlayerId = _hostParams.LocalPlayerId,
							HostPlayerName = _hostParams.LocalPlayerName,
							AssignedSlotIndex = assignedId,
							MaxPlayers = _hostParams.MaxPlayers
						}.ToNetMessage());

						PeerConnected?.Invoke(new PeerInfo(assignedId, hello.PlayerId, hello.PlayerName, assignedId));
					}
					else
					{
						peer.HelloReceived = true;
						peer.PlayerId = hello.PlayerId;
						peer.PlayerName = hello.PlayerName;

						SendRawToSteamId(remoteSteamId, new WelcomeMessage
						{
							HostPlayerId = _hostParams.LocalPlayerId,
							HostPlayerName = _hostParams.LocalPlayerName,
							AssignedSlotIndex = peer.PeerId,
							MaxPlayers = _hostParams.MaxPlayers
						}.ToNetMessage());
					}

					return;
				}

				if (message.Type == MessageType.Disconnect)
				{
					if (_peersBySteamId.TryGetValue(key, out SteamPeer peer))
						RemovePeer(peer.PeerId, "client disconnected");

					return;
				}

				if (_peersBySteamId.TryGetValue(key, out SteamPeer existingPeer) && existingPeer.HelloReceived)
					MessageReceived?.Invoke(existingPeer.PeerId, message);

				return;
			}

			if (remoteSteamId != _hostSteamId) return;

			if (message.Type == MessageType.Welcome)
			{
				if (_clientPeerRaised) return;

				WelcomeMessage welcome = WelcomeMessage.FromPayload(message.Payload);
				LocalPeerId = welcome.AssignedSlotIndex;
				_clientPeerRaised = true;
				State = ConnectionState.Connected;

				PeerConnected?.Invoke(new PeerInfo(
					PeerInfo.HostPeerId,
					welcome.HostPlayerId,
					welcome.HostPlayerName,
					0));

				return;
			}

			if (message.Type == MessageType.Reject)
			{
				State = ConnectionState.Disconnected;
				ConnectionRejected?.Invoke(RejectMessage.FromPayload(message.Payload));
				return;
			}

			MessageReceived?.Invoke(PeerInfo.HostPeerId, message);
		}

		private void SendRawToSteamId(CSteamID target, NetMessage message)
		{
			if (!SteamRuntime.IsInitialized || target == CSteamID.Nil || message == null) return;
			byte[] payload = message.Serialize();
			SteamNetworking.SendP2PPacket(target, payload, (uint)payload.Length, EP2PSend.k_EP2PSendReliable, P2PChannel);
		}

		private void OnP2PSessionRequest(P2PSessionRequest_t callback)
		{
			if (!SteamRuntime.IsInitialized) return;

			if (IsHost)
			{
				SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
			}
			else if (_hostSteamId != CSteamID.Nil && callback.m_steamIDRemote == _hostSteamId)
			{
				SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
			}
		}

		private void OnP2PSessionConnectFail(P2PSessionConnectFail_t callback)
		{
			if (_disposed) return;

			ulong key = callback.m_steamIDRemote.m_SteamID;

			if (IsHost)
			{
				if (_peersBySteamId.TryGetValue(key, out SteamPeer peer))
					RemovePeer(peer.PeerId, $"Session connect fail (error {callback.m_eP2PSessionError})");

				return;
			}

			if (callback.m_steamIDRemote != _hostSteamId) return;

			State = ConnectionState.Disconnected;
			_clientPeerRaised = false;

			if (SteamRuntime.IsInitialized)
			{
				try { SteamNetworking.CloseP2PSessionWithUser(_hostSteamId); } catch { }
			}

			PeerDisconnected?.Invoke(PeerInfo.HostPeerId, $"Lost connection to host (error {callback.m_eP2PSessionError})");
		}

		public void RemovePeer(int peerId, string reason = "Peer disconnected")
		{
			if (!_peersById.TryGetValue(peerId, out SteamPeer peer)) return;

			_peersById.Remove(peerId);
			_peersBySteamId.Remove(peer.SteamId.m_SteamID);

			if (SteamRuntime.IsInitialized)
			{
				try { SteamNetworking.CloseP2PSessionWithUser(peer.SteamId); } catch { }
			}

			PeerDisconnected?.Invoke(peerId, reason);
		}

		public void KickPeer(int peerId, string reason = "Kicked by host")
		{
			if (!IsHost) return;

			if (_peersById.TryGetValue(peerId, out SteamPeer peer))
			{
				SendRawToSteamId(peer.SteamId, new RejectMessage
				{
					Reason = RejectReason.HostBusy,
					Detail = reason
				}.ToNetMessage());

				RemovePeer(peerId, reason);
			}
		}

		public void Disconnect()
		{
			if (SteamRuntime.IsInitialized)
			{
				if (IsHost)
				{
					foreach (SteamPeer peer in new List<SteamPeer>(_peersById.Values))
					{
						try { SteamNetworking.CloseP2PSessionWithUser(peer.SteamId); } catch { }
					}
				}
				else if (_hostSteamId != CSteamID.Nil)
				{
					try { SteamNetworking.CloseP2PSessionWithUser(_hostSteamId); } catch { }
				}
			}

			_peersById.Clear();
			_peersBySteamId.Clear();
			_hostSteamId = CSteamID.Nil;
			_clientPeerRaised = false;
			LocalPeerId = PeerInfo.HostPeerId;
			State = ConnectionState.Disconnected;
			IsHost = false;
		}

		public void Dispose()
		{
			if (_disposed) return;

			Disconnect();
			_disposed = true;

			try
			{
				_sessionRequestCallback?.Unregister();
				_sessionRequestCallback = null;
				_sessionConnectFailCallback?.Unregister();
				_sessionConnectFailCallback = null;
			}
			catch { }
		}
	}
}
#endif