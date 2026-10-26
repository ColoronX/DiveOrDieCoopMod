using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public enum DiveStateAction : byte
	{
		AddCollectedItem,
		AddExtractedItem,
		AddNewEncounter,
		AddEventCount,
		AddExperiencePoints,
		DestroyCollectible
	}

	public sealed class DiveStateSyncMessage
	{
		public DiveStateAction Action;
		public string StringArg;
		public int IntArg1;
		public int IntArg2;
		public float PosX;
		public float PosY;

		public NetMessage ToNetMessage()
		{
			return this.ToNetMessage(MessageType.DiveStateSync);
		}

		public NetMessage ToNetMessage(MessageType messageType)
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write((byte)this.Action);
				EntitySyncBatchMessage.WriteString(writer, this.StringArg);
				writer.Write(this.IntArg1);
				writer.Write(this.IntArg2);
				writer.Write(this.PosX);
				writer.Write(this.PosY);
				return new NetMessage(messageType, stream.ToArray());
			}
		}

		public static DiveStateSyncMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new DiveStateSyncMessage
				{
					Action = (DiveStateAction)reader.ReadByte(),
					StringArg = EntitySyncBatchMessage.ReadString(reader),
					IntArg1 = reader.ReadInt32(),
					IntArg2 = reader.ReadInt32(),
					PosX = reader.ReadSingle(),
					PosY = reader.ReadSingle()
				};
			}
		}
	}
}