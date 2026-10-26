using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DiveOrDieCoop.Networking.Protocol;

namespace DiveOrDieCoop.Networking.LAN
{
	public sealed class LanTransport : ITransport
	{
		public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
		public bool IsHost { get; private set; }
		public int LocalPeerId { get; private set; } = PeerInfo.HostPeerId;

		public event Action<PeerInfo> PeerConnected;
		public event Action<int, string> PeerDisconnected;
		public event Action<int, NetMessage> MessageReceived;
		public event Action<RejectMessage> ConnectionRejected;
		public event Action<Exception> TransportError;

		public void StartHost(HostStartParams startParams)
		{
			if (this.State != ConnectionState.Disconnected)
				throw new InvalidOperationException($"Cannot StartHost while transport is {this.State}.");

			this._hostParams = startParams ?? throw new ArgumentNullException(nameof(startParams));
			this.IsHost = true;
			this.LocalPeerId = PeerInfo.HostPeerId;
			this.State = ConnectionState.Hosting;
			this._shuttingDown = false;
			this._listener = new TcpListener(IPAddress.Any, startParams.Port);
			this._listener.Start();
			this._acceptThread = new Thread(this.AcceptLoop) { IsBackground = true, Name = "DiveOrDieCoop-Accept" };
			this._acceptThread.Start();
		}

		public void StopHost()
		{
			if (!this.IsHost) return;
			this.Disconnect();
		}

		public void Connect(ClientConnectParams connectParams)
		{
			if (this.State != ConnectionState.Disconnected)
				throw new InvalidOperationException($"Cannot Connect while transport is {this.State}.");

			this._clientParams = connectParams ?? throw new ArgumentNullException(nameof(connectParams));
			this.IsHost = false;
			this.LocalPeerId = -1;
			this.State = ConnectionState.Connecting;
			this._shuttingDown = false;
			this._clientThread = new Thread(this.ClientConnectAndRun) { IsBackground = true, Name = "DiveOrDieCoop-Client" };
			this._clientThread.Start();
		}

		public void Disconnect()
		{
			if (this._shuttingDown) return;
			this._shuttingDown = true;
			try { this._listener?.Stop(); } catch { }
			foreach (KeyValuePair<int, PeerConnection> kvp in this._peers) kvp.Value.Dispose();
			this._peers.Clear();
			this._hostSideConnection?.Dispose();
			this._hostSideConnection = null;
			this.LocalPeerId = PeerInfo.HostPeerId;
			this.State = ConnectionState.Disconnected;
			this.IsHost = false;
		}

		public void Send(int peerId, NetMessage message)
		{
			if (!this.IsHost || message == null) return;
			if (this._peers.TryGetValue(peerId, out PeerConnection peer)) peer.EnqueueMessage(message);
		}

		public void SendToHost(NetMessage message)
		{
			if (this.IsHost || this._hostSideConnection == null || message == null) return;
			this._hostSideConnection.EnqueueMessage(message);
		}

		public void KickPeer(int peerId, string reason = "Kicked by host")
		{
			if (!this.IsHost) return;
			if (this._peers.TryRemove(peerId, out PeerConnection connection))
			{
				try
				{
					byte[] rejectBytes = new RejectMessage { Reason = RejectReason.HostBusy, Detail = reason }.ToNetMessage().Serialize();
					NetworkStream stream = connection.Client.GetStream();
					stream.Write(rejectBytes, 0, rejectBytes.Length);
					stream.Flush();
				}
				catch { }
				connection.Dispose();
				this.PeerDisconnected?.Invoke(peerId, reason);
			}
		}

		public void Dispose() => this.Disconnect();

		private void AcceptLoop()
		{
			try
			{
				while (!this._shuttingDown)
				{
					TcpClient client;
					try { client = this._listener.AcceptTcpClient(); }
					catch (SocketException) { break; }
					catch (ObjectDisposedException) { break; }
					ConfigureSocket(client);
					Thread handshakeThread = new Thread(() => this.HandleIncomingConnection(client)) { IsBackground = true, Name = "DiveOrDieCoop-Handshake" };
					handshakeThread.Start();
				}
			}
			catch (Exception ex)
			{
				if (!this._shuttingDown) this.TransportError?.Invoke(ex);
			}
		}

		private void HandleIncomingConnection(TcpClient client)
		{
			int assignedPeerId = 0;
			PeerConnection connection = null;
			try
			{
				NetworkStream stream = client.GetStream();
				client.ReceiveTimeout = HandshakeTimeoutMs;
				NetMessage first = NetMessage.ReadFrom(stream);
				if (first == null || first.Type != MessageType.Hello) { CloseQuietly(client); return; }
				HelloMessage hello = HelloMessage.FromPayload(first.Payload);

				if (hello.ProtocolVersion != ProtocolVersion.Current)
				{
					SendRejectAndClose(client, RejectReason.ProtocolMismatch, "Protocol version mismatch.");
					return;
				}

				if (this._peers.Count >= this._hostParams.MaxPlayers)
				{
					SendRejectAndClose(client, RejectReason.LobbyFull, "Lobby full.");
					return;
				}

				client.ReceiveTimeout = 0;
				assignedPeerId = Interlocked.Increment(ref this._nextPeerId);
				connection = new PeerConnection(assignedPeerId, client, ex => this.TransportError?.Invoke(ex));
				this._peers[assignedPeerId] = connection;
				connection.EnqueueMessage(new WelcomeMessage
				{
					HostPlayerId = this._hostParams.LocalPlayerId,
					HostPlayerName = this._hostParams.LocalPlayerName,
					AssignedSlotIndex = assignedPeerId,
					MaxPlayers = this._hostParams.MaxPlayers
				}.ToNetMessage());
				this.PeerConnected?.Invoke(new PeerInfo(assignedPeerId, hello.PlayerId, hello.PlayerName, assignedPeerId));
				this.ReadLoop(stream, assignedPeerId);
			}
			catch (Exception ex)
			{
				if (!this._shuttingDown) this.TransportError?.Invoke(ex);
			}
			finally
			{
				if (assignedPeerId != 0 && this._peers.TryRemove(assignedPeerId, out PeerConnection removed))
				{
					removed.Dispose();
					this.PeerDisconnected?.Invoke(assignedPeerId, "connection closed");
				}
				else
				{
					CloseQuietly(client);
				}
			}
		}

		private void ClientConnectAndRun()
		{
			TcpClient client = new TcpClient();
			try
			{
				IAsyncResult result = client.BeginConnect(this._clientParams.Address, this._clientParams.Port, null, null);
				bool connected = result.AsyncWaitHandle.WaitOne(ConnectTimeoutMs);
				if (!connected || !client.Connected) throw new SocketException((int)SocketError.TimedOut);
				client.EndConnect(result);
				ConfigureSocket(client);
				NetworkStream stream = client.GetStream();
				this._hostSideConnection = new PeerConnection(PeerInfo.HostPeerId, client, ex => this.TransportError?.Invoke(ex));
				this._hostSideConnection.EnqueueMessage(new HelloMessage
				{
					ProtocolVersion = ProtocolVersion.Current,
					PlayerId = this._clientParams.LocalPlayerId,
					PlayerName = this._clientParams.LocalPlayerName
				}.ToNetMessage());
				client.ReceiveTimeout = HandshakeTimeoutMs;
				NetMessage response = NetMessage.ReadFrom(stream);
				if (response == null || response.Type != MessageType.Welcome)
				{
					if (response != null && response.Type == MessageType.Reject) this.ConnectionRejected?.Invoke(RejectMessage.FromPayload(response.Payload));
					this.State = ConnectionState.Disconnected;
					this._hostSideConnection.Dispose();
					return;
				}
				client.ReceiveTimeout = 0;
				WelcomeMessage welcome = WelcomeMessage.FromPayload(response.Payload);
				this.LocalPeerId = welcome.AssignedSlotIndex;
				this.State = ConnectionState.Connected;
				this.PeerConnected?.Invoke(new PeerInfo(PeerInfo.HostPeerId, welcome.HostPlayerId, welcome.HostPlayerName, 0));
				this.ReadLoop(stream, PeerInfo.HostPeerId);
			}
			catch (Exception ex)
			{
				if (!this._shuttingDown) this.TransportError?.Invoke(ex);
			}
			finally
			{
				bool wasConnected = this.State == ConnectionState.Connected;
				this.State = ConnectionState.Disconnected;
				this._hostSideConnection?.Dispose();
				this._hostSideConnection = null;
				if (wasConnected) this.PeerDisconnected?.Invoke(PeerInfo.HostPeerId, "connection closed");
				this.LocalPeerId = PeerInfo.HostPeerId;
			}
		}

		private void ReadLoop(NetworkStream stream, int peerId)
		{
			while (!this._shuttingDown)
			{
				NetMessage message;
				try { message = NetMessage.ReadFrom(stream); }
				catch (SocketException) { break; }
				catch (IOException) { break; }
				catch (ObjectDisposedException) { break; }
				catch (Exception ex)
				{
					if (!this._shuttingDown) this.TransportError?.Invoke(new Exception($"ReadLoop error on peer {peerId}: {ex.Message}", ex));
					break;
				}
				if (message == null) break;
				if (message.Type == MessageType.Reject) { this.ConnectionRejected?.Invoke(RejectMessage.FromPayload(message.Payload)); break; }
				this.MessageReceived?.Invoke(peerId, message);
			}
		}

		private static void ConfigureSocket(TcpClient client)
		{
			client.NoDelay = true;
			client.SendBufferSize = 65536;
			client.ReceiveBufferSize = 65536;
			client.ReceiveTimeout = 0;
			client.SendTimeout = 0;
		}

		private static void SendRejectAndClose(TcpClient client, RejectReason reason, string detail)
		{
			try
			{
				byte[] bytes = new RejectMessage { Reason = reason, Detail = detail }.ToNetMessage().Serialize();
				NetworkStream stream = client.GetStream();
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush();
			}
			catch { }
			CloseQuietly(client);
		}

		private static void CloseQuietly(TcpClient client) { try { client?.Close(); } catch { } }

		private sealed class PeerConnection : IDisposable
		{
			public readonly int PeerId;
			public readonly TcpClient Client;
			private readonly NetworkStream _stream;
			private readonly BlockingCollection<NetMessage> _sendQueue = new BlockingCollection<NetMessage>(new ConcurrentQueue<NetMessage>());
			private readonly Thread _writeThread;
			private readonly Action<Exception> _onError;
			private volatile bool _disposed;

			public PeerConnection(int peerId, TcpClient client, Action<Exception> onError)
			{
				this.PeerId = peerId;
				this.Client = client;
				this._onError = onError;
				this._stream = client.GetStream();
				this._writeThread = new Thread(this.WriteLoop) { IsBackground = true, Name = $"DiveOrDieCoop-Writer-{peerId}" };
				this._writeThread.Start();
			}

			public void EnqueueMessage(NetMessage msg)
			{
				if (this._disposed || this._sendQueue.IsAddingCompleted) return;
				try { this._sendQueue.Add(msg); } catch (InvalidOperationException) { }
			}

			private void WriteLoop()
			{
				try
				{
					foreach (NetMessage message in this._sendQueue.GetConsumingEnumerable())
					{
						byte[] bytes = message.Serialize();
						this._stream.Write(bytes, 0, bytes.Length);
						this._stream.Flush();
					}
				}
				catch (Exception ex)
				{
					if (!this._disposed) this._onError?.Invoke(new Exception($"WriteLoop failed on peer {this.PeerId}: {ex.Message}", ex));
				}
				finally { this.Dispose(); }
			}

			public void Dispose()
			{
				if (this._disposed) return;
				this._disposed = true;
				try { this._sendQueue.CompleteAdding(); } catch { }
				CloseQuietly(this.Client);
			}
		}

		private const int HandshakeTimeoutMs = 5000;
		private const int ConnectTimeoutMs = 8000;
		private TcpListener _listener;
		private Thread _acceptThread;
		private Thread _clientThread;
		private HostStartParams _hostParams;
		private ClientConnectParams _clientParams;
		private PeerConnection _hostSideConnection;
		private volatile bool _shuttingDown;
		private int _nextPeerId;
		private readonly ConcurrentDictionary<int, PeerConnection> _peers = new ConcurrentDictionary<int, PeerConnection>();
	}
}
