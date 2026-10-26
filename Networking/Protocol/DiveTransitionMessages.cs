using System.Collections.Generic;
using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public enum DiveStatus : byte
	{
		Active = 0,
		Surfaced = 1,
		Dead = 2,
		InHub = 3,
		Staging = 4
	}

	public sealed class DiveReadyStatusMessage
	{
		public int PeerId;
		public bool IsReady;
		public string SelectedSurvivorId;
		public string SelectedSurfaceSpotId;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.PeerId);
				writer.Write(this.IsReady);
				EntitySyncBatchMessage.WriteString(writer, this.SelectedSurvivorId);
				EntitySyncBatchMessage.WriteString(writer, this.SelectedSurfaceSpotId);

				return new NetMessage(
					MessageType.DiveReadyStatus,
					stream.ToArray());
			}
		}

		public static DiveReadyStatusMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				DiveReadyStatusMessage message = new DiveReadyStatusMessage
				{
					PeerId = reader.ReadInt32(),
					IsReady = reader.ReadBoolean(),
					SelectedSurvivorId = EntitySyncBatchMessage.ReadString(reader),
					SelectedSurfaceSpotId = EntitySyncBatchMessage.ReadString(reader)
				};

				return message;
			}
		}
	}

	public sealed class DiveCancelStagingMessage
	{
		public NetMessage ToNetMessage()
		{
			return new NetMessage(
				MessageType.DiveCancelStaging,
				System.Array.Empty<byte>());
		}
	}

	public sealed class PlayerDiveStatusMessage
	{
		public int PeerId;
		public DiveStatus Status;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.PeerId);
				writer.Write((byte)this.Status);

				return new NetMessage(
					MessageType.PlayerDiveStatus,
					stream.ToArray());
			}
		}

		public static PlayerDiveStatusMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new PlayerDiveStatusMessage
				{
					PeerId = reader.ReadInt32(),
					Status = (DiveStatus)reader.ReadByte()
				};
			}
		}
	}

	public sealed class FinalDiveEndedMessage
	{
		public NetMessage ToNetMessage()
		{
			return new NetMessage(
				MessageType.FinalDiveEnded,
				System.Array.Empty<byte>());
		}
	}

	public sealed class DiveSeedMessage
	{
		public int Seed;
		public string SurfaceSpotId;
		public Dictionary<int, string> PeerSurvivorClaims =
			new Dictionary<int, string>();

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.Seed);

				EntitySyncBatchMessage.WriteString(
					writer,
					this.SurfaceSpotId);

				writer.Write(this.PeerSurvivorClaims.Count);

				foreach (var kvp in this.PeerSurvivorClaims)
				{
					writer.Write(kvp.Key);

					EntitySyncBatchMessage.WriteString(
						writer,
						kvp.Value);
				}

				return new NetMessage(
					MessageType.DiveSeed,
					stream.ToArray());
			}
		}

		public static DiveSeedMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				DiveSeedMessage message = new DiveSeedMessage
				{
					Seed = reader.ReadInt32(),
					SurfaceSpotId =
						EntitySyncBatchMessage.ReadString(reader)
				};

				int count = reader.ReadInt32();

				for (int i = 0; i < count; i++)
				{
					int peerId = reader.ReadInt32();

					string claim =
						EntitySyncBatchMessage.ReadString(reader);

					message.PeerSurvivorClaims[peerId] = claim;
				}

				return message;
			}
		}
	}

	public sealed class DiverDiedMessage
	{
		public int PeerId;
		public string SurvivorId;
		public string DeathSource;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.PeerId);
				EntitySyncBatchMessage.WriteString(writer, this.SurvivorId);
				EntitySyncBatchMessage.WriteString(writer, this.DeathSource);

				return new NetMessage(
					MessageType.DiverDied,
					stream.ToArray());
			}
		}

		public static DiverDiedMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new DiverDiedMessage
				{
					PeerId = reader.ReadInt32(),
					SurvivorId = EntitySyncBatchMessage.ReadString(reader),
					DeathSource = EntitySyncBatchMessage.ReadString(reader)
				};
			}
		}
	}

	public sealed class CollectibleSpawnMessage
	{
		public string ItemId;
		public float PosX;
		public float PosY;
		public float PosZ;
		public int NetId;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				EntitySyncBatchMessage.WriteString(writer, this.ItemId);
				writer.Write(this.PosX);
				writer.Write(this.PosY);
				writer.Write(this.PosZ);
				writer.Write(this.NetId);

				return new NetMessage(
					MessageType.CollectibleSpawn,
					stream.ToArray());
			}
		}

		public static CollectibleSpawnMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new CollectibleSpawnMessage
				{
					ItemId = EntitySyncBatchMessage.ReadString(reader),
					PosX = reader.ReadSingle(),
					PosY = reader.ReadSingle(),
					PosZ = reader.ReadSingle(),
					NetId = reader.ReadInt32()
				};
			}
		}
	}

	public sealed class FlareSyncMessage
	{
		public int SourcePeerId;
		public int FlareId;
		public bool Destroyed;
		public float PosX;
		public float PosY;
		public float VelX;
		public float VelY;
		public float RemainingDuration;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(this.SourcePeerId);
				writer.Write(this.FlareId);
				writer.Write(this.Destroyed);
				writer.Write(this.PosX);
				writer.Write(this.PosY);
				writer.Write(this.VelX);
				writer.Write(this.VelY);
				writer.Write(this.RemainingDuration);

				return new NetMessage(
					MessageType.FlareSync,
					stream.ToArray());
			}
		}

		public static FlareSyncMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new FlareSyncMessage
				{
					SourcePeerId = reader.ReadInt32(),
					FlareId = reader.ReadInt32(),
					Destroyed = reader.ReadBoolean(),
					PosX = reader.ReadSingle(),
					PosY = reader.ReadSingle(),
					VelX = reader.ReadSingle(),
					VelY = reader.ReadSingle(),
					RemainingDuration = reader.ReadSingle()
				};
			}
		}
	}
}