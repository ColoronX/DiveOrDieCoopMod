using System;

namespace DiveOrDieCoop.Networking
{
	public readonly struct PeerInfo
	{
		public readonly int PeerId;
		public readonly Guid PlayerId;
		public readonly string PlayerName;
		public readonly int SlotIndex;

		public PeerInfo(int peerId, Guid playerId, string playerName, int slotIndex)
		{
			this.PeerId = peerId;
			this.PlayerId = playerId;
			this.PlayerName = playerName;
			this.SlotIndex = slotIndex;
		}

		public const int HostPeerId = 0;
	}
}


