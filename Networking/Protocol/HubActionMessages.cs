using System.IO;

namespace DiveOrDieCoop.Networking.Protocol
{
	public enum HubActionType : byte
	{
		SetManningSurvivor = 1,
		RemoveManningSurvivor = 2,
		RemoveItem = 3,
		CraftItem = 4,
		UpgradeStructure = 5,
		AssignBed = 6,
		RemoveBed = 7,
		StartTreatment = 8,
		CancelTreatment = 9,
		StartExpedition = 10,
		CancelExpedition = 11,
		RecruitSurvivor = 12,
		DismissSurvivor = 13,
		ThrowAtSea = 14,
		RenameSurvivor = 15,
		ToggleLockSurvivor = 16,
		EquipItem = 17,
		UnequipItem = 18,
		SetSurvivorUsable = 19,
		SetPinnedRecipe = 20,
		SetMapMarker = 21,
		RemoveMapMarker = 22,
		AltarWorship = 23,
		LevelUpSurvivor = 24,
		SkipDay = 25
	}

	public sealed class HubActionMessage
	{
		public HubActionType ActionType;
		public string Arg1;
		public string Arg2;
		public string Arg3;
		public int IntArg;

		public NetMessage ToNetMessage()
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write((byte)this.ActionType);
				EntitySyncBatchMessage.WriteString(writer, this.Arg1);
				EntitySyncBatchMessage.WriteString(writer, this.Arg2);
				EntitySyncBatchMessage.WriteString(writer, this.Arg3);
				writer.Write(this.IntArg);
				return new NetMessage(MessageType.HubAction, stream.ToArray());
			}
		}

		public static HubActionMessage FromPayload(byte[] payload)
		{
			using (MemoryStream stream = new MemoryStream(payload))
			using (BinaryReader reader = new BinaryReader(stream))
			{
				return new HubActionMessage
				{
					ActionType = (HubActionType)reader.ReadByte(),
					Arg1 = EntitySyncBatchMessage.ReadString(reader),
					Arg2 = EntitySyncBatchMessage.ReadString(reader),
					Arg3 = EntitySyncBatchMessage.ReadString(reader),
					IntArg = reader.ReadInt32()
				};
			}
		}
	}
}

