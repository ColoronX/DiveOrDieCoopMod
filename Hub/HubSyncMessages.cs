using System;
using System.IO;
using System.IO.Compression;

namespace DiveOrDieCoop.Networking.Protocol
{
	public sealed class HubStateSyncMessage
	{
		public string CampStateJson;
		public string ProgressionStateJson;
		public string PoolStateJson;

		public NetMessage ToNetMessage()
		{
			byte[] uncompressedBytes;
			using (MemoryStream rawStream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(rawStream))
			{
				EntitySyncBatchMessage.WriteString(writer, this.CampStateJson);
				EntitySyncBatchMessage.WriteString(writer, this.ProgressionStateJson);
				EntitySyncBatchMessage.WriteString(writer, this.PoolStateJson);
				uncompressedBytes = rawStream.ToArray();
			}

			byte[] compressedPayload = CompressBytes(uncompressedBytes);

			return new NetMessage(MessageType.HubStateSync, compressedPayload);
		}

		public static HubStateSyncMessage FromPayload(byte[] payload)
		{
			byte[] decompressedBytes = DecompressBytes(payload);

			using (MemoryStream rawStream = new MemoryStream(decompressedBytes))
			using (BinaryReader reader = new BinaryReader(rawStream))
			{
				return new HubStateSyncMessage
				{
					CampStateJson = EntitySyncBatchMessage.ReadString(reader),
					ProgressionStateJson = EntitySyncBatchMessage.ReadString(reader),
					PoolStateJson = EntitySyncBatchMessage.ReadString(reader)
				};
			}
		}

		private static byte[] CompressBytes(byte[] rawData)
		{
			if (rawData == null || rawData.Length == 0) return Array.Empty<byte>();

			using (MemoryStream outputStream = new MemoryStream())
			{
				using (DeflateStream deflateStream = new DeflateStream(outputStream, CompressionMode.Compress, true))
				{
					deflateStream.Write(rawData, 0, rawData.Length);
				}
				return outputStream.ToArray();
			}
		}

		private static byte[] DecompressBytes(byte[] compressedData)
		{
			if (compressedData == null || compressedData.Length == 0) return Array.Empty<byte>();

			using (MemoryStream inputStream = new MemoryStream(compressedData))
			using (DeflateStream deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress))
			using (MemoryStream outputStream = new MemoryStream())
			{
				deflateStream.CopyTo(outputStream);
				return outputStream.ToArray();
			}
		}
	}

	public sealed class HubSyncRequestMessage
	{
		public NetMessage ToNetMessage()
		{
			return new NetMessage(MessageType.HubSyncRequest, Array.Empty<byte>());
		}
	}
}

