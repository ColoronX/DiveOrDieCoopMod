using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DiveOrDieCoop.Networking.LAN
{
	public sealed class LanHostDiscovery : IDisposable
	{
		public const int DefaultDiscoveryPort = 27510;

		private const string RequestMagic = "DIVEORDIECOOP_DISCOVER_REQUEST";
		private const string ResponseMagic = "DIVEORDIECOOP_DISCOVER_RESPONSE";

		public event Action<DiscoveredHost> HostFound;

		public void StartResponder(int discoveryPort, Func<DiscoveryInfo> infoProvider)
		{
			this.StopResponder();
			this._infoProvider = infoProvider;

			try
			{
				this._responderSocket = new UdpClient();
				this._responderSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				this._responderSocket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
				this._responderSocket.EnableBroadcast = true;

				this._responderRunning = true;
				this._responderThread = new Thread(this.ResponderLoop) 
				{ 
					IsBackground = true, 
					Name = "DiveOrDieCoop-DiscoveryResponder" 
				};
				this._responderThread.Start();
			}
			catch (Exception)
			{
			}
		}

		public void StopResponder()
		{
			this._responderRunning = false;
			try
			{
				this._responderSocket?.Close();
			}
			catch { }
			this._responderSocket = null;
		}

		public void DiscoverHosts(int discoveryPort, TimeSpan duration, CancellationToken cancellationToken = default)
		{
			UdpClient client = null;
			try
			{
				client = new UdpClient();
				client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
				client.EnableBroadcast = true;
				client.Client.ReceiveTimeout = 200;

				byte[] requestBytes = Encoding.UTF8.GetBytes(RequestMagic);
				IPEndPoint broadcastEp = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
				client.Send(requestBytes, requestBytes.Length, broadcastEp);

				DateTime deadline = DateTime.UtcNow + duration;
				while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
				{
					IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
					byte[] data;
					try
					{
						data = client.Receive(ref remote);
					}
					catch (SocketException)
					{
						continue;
					}

					string text = Encoding.UTF8.GetString(data);
					if (!text.StartsWith(ResponseMagic, StringComparison.Ordinal))
					{
						continue;
					}

					DiscoveredHost host = ParseResponse(text, remote.Address.ToString());
					if (host != null)
					{
						this.HostFound?.Invoke(host);
					}
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				client?.Close();
			}
		}

		private void ResponderLoop()
		{
			try
			{
				while (this._responderRunning)
				{
					IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
					byte[] data;
					try
					{
						data = this._responderSocket.Receive(ref remote);
					}
					catch
					{
						break;
					}

					string text = Encoding.UTF8.GetString(data);
					if (text != RequestMagic)
					{
						continue;
					}

					DiscoveryInfo info = this._infoProvider?.Invoke();
					if (info == null)
					{
						continue;
					}

					string response = $"{ResponseMagic}|{info.GameName}|{info.HostPlayerName}|{info.TcpPort}|{info.CurrentPlayers}|{info.MaxPlayers}";
					byte[] payload = Encoding.UTF8.GetBytes(response);
					this._responderSocket.Send(payload, payload.Length, remote);
				}
			}
			catch
			{
			}
		}

		private static DiscoveredHost ParseResponse(string text, string fromAddress)
		{
			string[] parts = text.Split('|');
			if (parts.Length != 6) return null;

			if (!int.TryParse(parts[3], out int tcpPort) ||
				!int.TryParse(parts[4], out int currentPlayers) ||
				!int.TryParse(parts[5], out int maxPlayers))
			{
				return null;
			}

			return new DiscoveredHost
			{
				Address = fromAddress,
				GameName = parts[1],
				HostPlayerName = parts[2],
				TcpPort = tcpPort,
				CurrentPlayers = currentPlayers,
				MaxPlayers = maxPlayers,
			};
		}

		public void Dispose()
		{
			this.StopResponder();
		}

		private UdpClient _responderSocket;
		private Thread _responderThread;
		private volatile bool _responderRunning;
		private Func<DiscoveryInfo> _infoProvider;
	}
	
		public sealed class DiscoveryInfo
		{
			public string GameName = "DiveOrDieCoop";
			public string HostPlayerName;
			public int TcpPort;
			public int CurrentPlayers;
			public int MaxPlayers;
		}

		public sealed class DiscoveredHost
		{
			public string Address;
			public string GameName;
			public string HostPlayerName;
			public int TcpPort;
			public int CurrentPlayers;
			public int MaxPlayers;
		}
}

