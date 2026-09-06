using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Decides when a spawned item is a Switcheroo.
///
/// An earlier version added the item to each pool's spawn-chance array. That array is read in one
/// place to actually pick an item, and in seven places by the lobby's rules screen — several of
/// which map an item to its slider through an array sized to the game's own item list, and threw
/// on a modded type. One of those runs every frame while the item-probability tab is open.
///
/// So the pools are left exactly as the game ships them, and the single draw point is intercepted
/// instead. The lobby UI never sees the Switcheroo, the pool hashes stay untouched, and the item
/// still turns up from crates and hand-outs, since both come through here.
/// </summary>
[HarmonyPatch(typeof(ItemPool), nameof(ItemPool.GetWeightedRandomItem))]
internal static class ItemPoolDrawPatch
{
    private static bool Prefix(ref ItemType __result)
    {
        if (!Plugin.Enabled.Value)
        {
            return true;
        }

        if (Random.value >= Mathf.Clamp01(Plugin.SpawnChance.Value))
        {
            return true;
        }

        __result = Switcheroo.Type;
        return false;
    }
}
