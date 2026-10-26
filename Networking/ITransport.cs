using System;
using DiveOrDieCoop.Networking.Protocol;

namespace DiveOrDieCoop.Networking
{
	public sealed class HostStartParams
	{
		public int Port { get; set; }
		public int MaxPlayers { get; set; }
		public Guid LocalPlayerId { get; set; }
		public string LocalPlayerName { get; set; }
	}

	public sealed class ClientConnectParams
	{
		public string Address { get; set; }
		public int Port { get; set; }
		public Guid LocalPlayerId { get; set; }
		public string LocalPlayerName { get; set; }
	}

	public interface ITransport : IDisposable
	{
		ConnectionState State { get; }
		bool IsHost { get; }
		int LocalPeerId { get; }
		event Action<PeerInfo> PeerConnected;
		event Action<int, string> PeerDisconnected;
		event Action<int, NetMessage> MessageReceived;
		event Action<RejectMessage> ConnectionRejected;
		event Action<Exception> TransportError;
		void StartHost(HostStartParams startParams);
		void StopHost();
		void Connect(ClientConnectParams connectParams);
		void Disconnect();
		void Send(int peerId, NetMessage message);
		void SendToHost(NetMessage message);
	}
}