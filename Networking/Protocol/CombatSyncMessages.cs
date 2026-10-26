using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public sealed class EntityDamageMessage
	{
		public int NetId;
		public int DamageValue;
		public int DamagedAttribute;
		public int DamageType;
		public string DamageSourceName;
		public float KnockbackDirX;
		public float KnockbackDirY;
		public float KnockbackIntensity;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.NetId);
				writer.Write(this.DamageValue);
				writer.Write(this.DamagedAttribute);
				writer.Write(this.DamageType);
				EntitySyncBatchMessage.WriteString(writer, this.DamageSourceName);
				writer.Write(this.KnockbackDirX);
				writer.Write(this.KnockbackDirY);
				writer.Write(this.KnockbackIntensity);
				return new NetMessage(MessageType.EntityDamage, stream.ToArray());
			}
		}

		public static EntityDamageMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new EntityDamageMessage
				{
					NetId = reader.ReadInt32(),
					DamageValue = reader.ReadInt32(),
					DamagedAttribute = reader.ReadInt32(),
					DamageType = reader.ReadInt32(),
					DamageSourceName = EntitySyncBatchMessage.ReadString(reader),
					KnockbackDirX = reader.ReadSingle(),
					KnockbackDirY = reader.ReadSingle(),
					KnockbackIntensity = reader.ReadSingle()
				};
			}
		}
	}

	public sealed class RemotePlayerDamagedMessage
	{
		public int TargetPeerId;
		public int DamageValue;
		public int DamagedAttribute;
		public string DamageSourceName;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.TargetPeerId);
				writer.Write(this.DamageValue);
				writer.Write(this.DamagedAttribute);
				EntitySyncBatchMessage.WriteString(writer, this.DamageSourceName);
				return new NetMessage(MessageType.RemotePlayerDamaged, stream.ToArray());
			}
		}

		public static RemotePlayerDamagedMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new RemotePlayerDamagedMessage
				{
					TargetPeerId = reader.ReadInt32(),
					DamageValue = reader.ReadInt32(),
					DamagedAttribute = reader.ReadInt32(),
					DamageSourceName = EntitySyncBatchMessage.ReadString(reader)
				};
			}
		}
	}

	public sealed class WebTouchedMessage
	{
		public float ContactPosX;
		public float ContactPosY;
		public float SpawnPosX;
		public float SpawnPosY;
		public int BlessedSpiderIndex;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.ContactPosX);
				writer.Write(this.ContactPosY);
				writer.Write(this.SpawnPosX);
				writer.Write(this.SpawnPosY);
				writer.Write(this.BlessedSpiderIndex);
				return new NetMessage(MessageType.WebTouched, stream.ToArray());
			}
		}

		public static WebTouchedMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new WebTouchedMessage
				{
					ContactPosX = reader.ReadSingle(),
					ContactPosY = reader.ReadSingle(),
					SpawnPosX = reader.ReadSingle(),
					SpawnPosY = reader.ReadSingle(),
					BlessedSpiderIndex = reader.ReadInt32()
				};
			}
		}
	}

	public sealed class ApplyFoamMessage
	{
		public int NetId;
		public float Duration;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.NetId);
				writer.Write(this.Duration);
				return new NetMessage(MessageType.ApplyFoam, stream.ToArray());
			}
		}

		public static ApplyFoamMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new ApplyFoamMessage
				{
					NetId = reader.ReadInt32(),
					Duration = reader.ReadSingle()
				};
			}
		}
	}
}

