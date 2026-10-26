using System;
using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public sealed class NetMessage
	{
		public MessageType Type { get; }
		public byte[] Payload { get; }

		private const int MaxBodyLength = 32 * 1024 * 1024;

		public NetMessage(MessageType type, byte[] payload)
		{
			this.Type = type;
			this.Payload = payload ?? Array.Empty<byte>();
		}

		public byte[] Serialize()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				int bodyLength = 1 + this.Payload.Length;
				writer.Write(bodyLength);
				writer.Write((byte)this.Type);
				writer.Write(this.Payload);
				return stream.ToArray();
			}
		}

		public static NetMessage ReadFrom(Stream stream)
		{
			byte[] lengthBytes = ReadExact(stream, 4);
			if (lengthBytes == null)
			{
				return null;
			}

			int bodyLength = BitConverter.ToInt32(lengthBytes, 0);
			if (bodyLength < 1 || bodyLength > MaxBodyLength)
			{
				throw new InvalidDataException($"NetMessage body length {bodyLength} out of range (max {MaxBodyLength}). Peer is likely running an incompatible build.");
			}

			byte[] body = ReadExact(stream, bodyLength);
			if (body == null)
			{
				return null;
			}

			MessageType type = (MessageType)body[0];
			byte[] payload = new byte[bodyLength - 1];
			Buffer.BlockCopy(body, 1, payload, 0, payload.Length);
			return new NetMessage(type, payload);
		}

		private static byte[] ReadExact(Stream stream, int count)
		{
			byte[] buffer = new byte[count];
			int offset = 0;
			while (offset < count)
			{
				int read = stream.Read(buffer, offset, count - offset);
				if (read <= 0)
				{
					return null;
				}
				offset += read;
			}
			return buffer;
		}
	}
}

