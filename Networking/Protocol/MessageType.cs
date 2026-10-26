namespace DiveOrDieCoop.Networking.Protocol
{
	public enum MessageType : byte
	{
		Hello = 1,
		Welcome = 2,
		Reject = 3,
		Disconnect = 4,
		Ping = 5,
		Pong = 6,

		DiverSnapshot = 10,
		EntitySyncBatch = 11,
		EntityDefeated = 12,
		WorldSyncRequest = 13,
		InventoryContribution = 14,

		HubStateSync = 15,
		HubSyncRequest = 16,
		DynamicSpawn = 17,
		DiveStateSync = 18,
		HubAction = 19,

		PlayerDiveStatus = 20,
		FinalDiveEnded = 21,
		DiveSeed = 22,
		CollectibleSpawn = 23,
		ReportButtonClose = 24,
		DiverDied = 25,
		DiveReadyStatus = 26,
		DiveCancelStaging = 27,

		EntityDamage = 30,
		RemotePlayerDamaged = 31,
		WebTouched = 32,
		ApplyFoam = 33,
		FlareSync = 34,

		ObstacleSync = 35,
		IdolPowerSync = 36,
		FlashlightSync = 37,
		ScooterSync = 38,
		BeaconSync = 39,
		ExtractorSync = 40,
		BloodGateSync = 41,
		LockerSync = 42
	}
}