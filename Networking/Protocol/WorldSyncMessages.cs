using System.Collections.Generic;
using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public struct EntitySyncEntry
	{
		public int NetId;
		public float PosX;
		public float PosY;
		public float RotZ;
		public float ScaleX;
		public float ScaleY;
		public bool Active;
		public int Health;
		public string Anim0;
		public string Anim1;
		public bool Pacified;
		public bool Foamed;

		public const int NoHealthValue = int.MinValue;
	}

	public sealed class EntitySyncBatchMessage
	{
		public List<EntitySyncEntry> Entries = new List<EntitySyncEntry>();

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.Entries.Count);
				foreach (EntitySyncEntry entry in this.Entries)
				{
					writer.Write(entry.NetId);
					writer.Write(entry.PosX);
					writer.Write(entry.PosY);
					writer.Write(entry.RotZ);
					writer.Write(entry.ScaleX);
					writer.Write(entry.ScaleY);
					writer.Write(entry.Active);
					writer.Write(entry.Health);
					WriteString(writer, entry.Anim0);
					WriteString(writer, entry.Anim1);
					writer.Write(entry.Pacified);
					writer.Write(entry.Foamed);
				}
				return new NetMessage(MessageType.EntitySyncBatch, stream.ToArray());
			}
		}

		public static EntitySyncBatchMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				EntitySyncBatchMessage message = new EntitySyncBatchMessage();
				int count = reader.ReadInt32();
				message.Entries.Capacity = count;
				for (int i = 0; i < count; i++)
				{
					message.Entries.Add(new EntitySyncEntry
					{
						NetId = reader.ReadInt32(),
						PosX = reader.ReadSingle(),
						PosY = reader.ReadSingle(),
						RotZ = reader.ReadSingle(),
						ScaleX = reader.ReadSingle(),
						ScaleY = reader.ReadSingle(),
						Active = reader.ReadBoolean(),
						Health = reader.ReadInt32(),
						Anim0 = ReadString(reader),
						Anim1 = ReadString(reader),
						Pacified = reader.ReadBoolean(),
						Foamed = reader.ReadBoolean()
					});
				}
				return message;
			}
		}

		public static void WriteString(BinaryWriter writer, string value)
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
			writer.Write(bytes.Length);
			writer.Write(bytes);
		}

		public static string ReadString(BinaryReader reader)
		{
			int length = reader.ReadInt32();
			byte[] bytes = reader.ReadBytes(length);
			return System.Text.Encoding.UTF8.GetString(bytes);
		}
	}

	public sealed class EntityDefeatedMessage
	{
		public int NetId;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.NetId);
				return new NetMessage(MessageType.EntityDefeated, stream.ToArray());
			}
		}

		public static EntityDefeatedMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new EntityDefeatedMessage { NetId = reader.ReadInt32() };
			}
		}
	}

	public sealed class WorldSyncRequestMessage
	{
		public NetMessage ToNetMessage()
		{
			return new NetMessage(MessageType.WorldSyncRequest, System.Array.Empty<byte>());
		}
	}

	public sealed class InventoryContributionMessage
	{
		public string ItemId;
		public int Count;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				EntitySyncBatchMessage.WriteString(writer, this.ItemId);
				writer.Write(this.Count);
				return new NetMessage(MessageType.InventoryContribution, stream.ToArray());
			}
		}

		public static InventoryContributionMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new InventoryContributionMessage
				{
					ItemId = EntitySyncBatchMessage.ReadString(reader),
					Count = reader.ReadInt32()
				};
			}
		}
	}
}

