using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Drives what happens when a player presses use while holding a Switcheroo.
///
/// The game dispatches item use through GetItemUseRoutine, a local function inside TryUseItem
/// whose switch throws on an unrecognised ItemType. Rather than patch a compiler-generated
/// local function, this intercepts TryUseItem itself and runs our own routine.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryUseItem))]
internal static class SwitcherooUsePatch
{
    private static bool Prefix(
        PlayerInventory __instance,
        bool isAirhornReaction,
        ref bool shouldEatInput,
        ref bool __result)
    {
        shouldEatInput = true;

        if (!__instance.isLocalPlayer)
        {
            return true;
        }

        if (__instance.GetEffectivelyEquippedItem() != Switcheroo.Type)
        {
            return true;
        }

        // Reuse the game's own gate so holstering, stuns, cooldowns and the rest still apply.
        if (!__instance.CanUseEquippedItem(
                altUse: false,
                isAirhornReaction,
                out InventorySlot _,
                out ItemData _,
                out shouldEatInput,
                out bool _))
        {
            __result = false;
            return false;
        }

        __instance.ItemUseTimestamp = Time.timeAsDouble;
        __instance.CancelItemUse();
        __instance.itemUseRoutine = __instance.StartCoroutine(SwitcherooRoutine(__instance));
        __instance.PlayerInfo.CancelEmote(canHideEmoteMenu: false);
        __instance.CancelItemFlourish();

        __result = true;
        return false;
    }

    private static IEnumerator SwitcherooRoutine(PlayerInventory inventory)
    {
        inventory.SetCurrentItemUse(ItemUseType.Regular);

        SwitcherooUi.BeginCountdown(Plugin.WindUpSeconds.Value);
        yield return new WaitForSeconds(Plugin.WindUpSeconds.Value);

        // Ask before consuming: the server validates against its own slot list, which still
        // holds the Switcheroo until the decrement command behind us lands.
        SwitcherooNetwork.RequestSwap();

        int index = inventory.EquippedItemIndex;
        inventory.DecrementUseFromSlotAt(index);
        inventory.SetCurrentItemUse(ItemUseType.None);
        inventory.RemoveIfOutOfUses(index, dueToFinishedItemUse: true);
    }
}
