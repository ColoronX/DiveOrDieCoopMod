using System;
using System.IO;
using System.Text;

namespace DiveOrDieCoop.Networking.Protocol
{
	public static class ProtocolVersion
	{
		public const int Current = 1;
	}

	public sealed class HelloMessage
	{
		public int ProtocolVersion;
		public Guid PlayerId;
		public string PlayerName;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.ProtocolVersion);
				writer.Write(this.PlayerId.ToByteArray());
				HandshakeIO.WriteString(writer, this.PlayerName);
				return new NetMessage(MessageType.Hello, stream.ToArray());
			}
		}

		public static HelloMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new HelloMessage
				{
					ProtocolVersion = reader.ReadInt32(),
					PlayerId = new Guid(reader.ReadBytes(16)),
					PlayerName = HandshakeIO.ReadString(reader),
				};
			}
		}
	}

	public sealed class WelcomeMessage
	{
		public Guid HostPlayerId;
		public string HostPlayerName;
		public int AssignedSlotIndex;
		public int MaxPlayers;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.HostPlayerId.ToByteArray());
				HandshakeIO.WriteString(writer, this.HostPlayerName);
				writer.Write(this.AssignedSlotIndex);
				writer.Write(this.MaxPlayers);
				return new NetMessage(MessageType.Welcome, stream.ToArray());
			}
		}

		public static WelcomeMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new WelcomeMessage
				{
					HostPlayerId = new Guid(reader.ReadBytes(16)),
					HostPlayerName = HandshakeIO.ReadString(reader),
					AssignedSlotIndex = reader.ReadInt32(),
					MaxPlayers = reader.ReadInt32(),
				};
			}
		}
	}

	public enum RejectReason : byte
	{
		ProtocolMismatch = 1,
		LobbyFull = 2,
		HostBusy = 3,
	}

	public sealed class RejectMessage
	{
		public RejectReason Reason;
		public string Detail;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write((byte)this.Reason);
				HandshakeIO.WriteString(writer, this.Detail);
				return new NetMessage(MessageType.Reject, stream.ToArray());
			}
		}

		public static RejectMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new RejectMessage
				{
					Reason = (RejectReason)reader.ReadByte(),
					Detail = HandshakeIO.ReadString(reader),
				};
			}
		}
	}

	internal static class HandshakeIO
	{
		public static void WriteString(BinaryWriter writer, string value)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
			writer.Write(bytes.Length);
			writer.Write(bytes);
		}

		public static string ReadString(BinaryReader reader)
		{
			int length = reader.ReadInt32();
			byte[] bytes = reader.ReadBytes(length);
			return Encoding.UTF8.GetString(bytes);
		}
	}
}


