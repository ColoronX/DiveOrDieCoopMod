using ED.Hub;
using HarmonyLib;

namespace DiveOrDieCoop.Patches
{
	[HarmonyPatch(typeof(MenuDivingDeckSurvivorLoadout), "OnSelectionInventoryConfirm")]
	internal static class SyncSuitVisualsPatch
	{
		private static bool Prefix(MenuDivingDeckSurvivorLoadout __instance, Item item)
		{
			if (item?.Data is EquipmentItemData eqData && eqData.EquipmentType == EquipmentType.SUIT)
			{
				SurvivorData survivor = AccessTools.Field(typeof(MenuDivingDeckSurvivorLoadout), "_survivorData")?.GetValue(__instance) as SurvivorData;
				MenuDivingDeck deck = AccessTools.Field(typeof(MenuDivingDeckSurvivorLoadout), "_divingDeckMenu")?.GetValue(__instance) as MenuDivingDeck;

				if (survivor != null)
				{
					survivor.ReplaceEquipment(new Item(item.Data, 1));

					__instance.SetData(survivor);
					deck?.UpdateSelectedSurvivorStats();

					return false;
				}
			}
			return true;
		}
	}
}

