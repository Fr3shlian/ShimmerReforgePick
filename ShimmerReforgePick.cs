using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ShimmerReforgePick {
	public class ShimmerReforgePick : Mod {
		public override void HandlePacket(BinaryReader reader, int whoAmI) {
			if (Main.netMode == NetmodeID.Server) {
				byte playerIndex = reader.ReadByte();
				byte desiredPrefix = reader.ReadByte();

				Player player = Main.player[playerIndex];
				Item item = player.inventory[58];

				item.Prefix(desiredPrefix);

				NetMessage.SendData(MessageID.SyncEquipment, -1, whoAmI, null, playerIndex, 58);
			}
		}
	}
}
