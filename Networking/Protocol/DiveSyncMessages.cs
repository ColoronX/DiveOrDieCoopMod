using System;
using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public sealed class DiverSnapshotMessage
	{
		public int SourcePeerId;

		public float PosX;
		public float PosY;
		public float VelX;
		public float VelY;
		public float RotZ;
		public float ScaleY;
		public int Health;
		public int Oxygen;

		public bool IsDead;
		public string DeathAnimation;

		public string Anim0;
		public string Anim1;
		public string PlayerName = "Diver";

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.SourcePeerId);

				writer.Write(this.PosX);
				writer.Write(this.PosY);
				writer.Write(this.VelX);
				writer.Write(this.VelY);
				writer.Write(this.RotZ);
				writer.Write(this.ScaleY);

				writer.Write(this.Health);
				writer.Write(this.Oxygen);

				writer.Write(this.IsDead);
				EntitySyncBatchMessage.WriteString(writer, this.DeathAnimation);

				EntitySyncBatchMessage.WriteString(writer, this.Anim0);
				EntitySyncBatchMessage.WriteString(writer, this.Anim1);
				EntitySyncBatchMessage.WriteString(writer, this.PlayerName);

				return new NetMessage(
					MessageType.DiverSnapshot,
					stream.ToArray()
				);
			}
		}

		public static DiverSnapshotMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				DiverSnapshotMessage msg = new DiverSnapshotMessage
				{
					SourcePeerId = reader.ReadInt32(),

					PosX = reader.ReadSingle(),
					PosY = reader.ReadSingle(),
					VelX = reader.ReadSingle(),
					VelY = reader.ReadSingle(),
					RotZ = reader.ReadSingle(),
					ScaleY = reader.ReadSingle(),

					Health = reader.ReadInt32(),
					Oxygen = reader.ReadInt32(),

					IsDead = reader.ReadBoolean(),
					DeathAnimation = EntitySyncBatchMessage.ReadString(reader),

					Anim0 = EntitySyncBatchMessage.ReadString(reader),
					Anim1 = EntitySyncBatchMessage.ReadString(reader)
				};

				if (stream.Position < stream.Length)
				{
					msg.PlayerName =
						EntitySyncBatchMessage.ReadString(reader);
				}

				return msg;
			}
		}
	}
}