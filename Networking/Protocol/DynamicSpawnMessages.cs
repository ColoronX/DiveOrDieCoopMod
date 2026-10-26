using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	
	public sealed class DynamicSpawnMessage
	{
		public string EventName;
		public string NetId;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				EntitySyncBatchMessage.WriteString(writer, this.EventName);
				EntitySyncBatchMessage.WriteString(writer, this.NetId);
				return new NetMessage(MessageType.DynamicSpawn, stream.ToArray());
			}
		}

		public static DynamicSpawnMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new DynamicSpawnMessage
				{
					EventName = EntitySyncBatchMessage.ReadString(reader),
					NetId = EntitySyncBatchMessage.ReadString(reader)
				};
			}
		}
	}
}

