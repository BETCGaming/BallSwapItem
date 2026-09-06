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

        // Already spent this round: refuse without consuming the item, and say so.
        if (SwitcherooNetwork.LockedThisRound)
        {
            Refuse("ONCE PER ROUND");
            __result = false;
            return false;
        }

        // Nothing to swap: refuse rather than spend the item on an empty gesture. A swap needs
        // two balls to trade, and there are none at all in the lobby's driving range.
        int eligible = SwapService.CountEligible();
        if (eligible < 2)
        {
            Trace.Log($"use refused: only {eligible} eligible ball(s)");
            Refuse("NO BALLS TO SWAP");
            __result = false;
            return false;
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

        Trace.Log($"use accepted, currentItemUse={__instance.CurrentItemUse}, slot={__instance.EquippedItemIndex}");

        __instance.ItemUseTimestamp = Time.timeAsDouble;
        __instance.CancelItemUse();
        __instance.itemUseRoutine = __instance.StartCoroutine(SwitcherooRoutine(__instance));
        __instance.PlayerInfo.CancelEmote(canHideEmoteMenu: false);
        __instance.CancelItemFlourish();

        __result = true;
        return false;
    }

    private static void Refuse(string message)
    {
        SwitcherooUi.ShowDenial(message);
        SwitcherooAudio.PlayDenial();
    }

    private static IEnumerator SwitcherooRoutine(PlayerInventory inventory)
    {
        Trace.Log("routine: started");
        inventory.SetCurrentItemUse(ItemUseType.Regular);

        // Hold the activation pose, borrowed from the Orbital Laser this device is built from.
        yield return new WaitForSeconds(GameManager.ItemSettings.OrbitalLaserActivationTime);

        // Ask before consuming: the server validates against its own slot list, which still
        // holds the Switcheroo until the decrement command behind us lands. The wind-up and
        // countdown are then timed by the server so every player sees the same warning.
        Trace.Log("routine: activation wait done, sending request");
        SwitcherooNetwork.RequestSwap();

        int index = inventory.EquippedItemIndex;
        inventory.DecrementUseFromSlotAt(index);

        // Hold the pose out for the full activation, tossing the spent device partway through,
        // exactly as the Orbital Laser does. The timings come from the game's own settings so
        // the throw lands on the same animation frame it was authored for.
        bool thrown = false;
        for (float elapsed = BMath.GetTimeSince(inventory.ItemUseTimestamp);
             elapsed < GameManager.ItemSettings.OrbitalLaserActivationTotalDuration;
             elapsed = BMath.GetTimeSince(inventory.ItemUseTimestamp))
        {
            if (!thrown && elapsed >= GameManager.ItemSettings.OrbitalLaserThrowTime)
            {
                SwitcherooThrownItem.EnsureRegistered();
                inventory.ThrowUsedItemForAllClients(SwitcherooThrownItem.Type);
                inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
                thrown = true;
                Trace.Log($"routine: threw spent device at {elapsed:0.00}s");
            }

            yield return null;
        }

        Trace.Log("routine: finished, clearing use state");
        inventory.SetCurrentItemUse(ItemUseType.None);
        inventory.RemoveIfOutOfUses(index, dueToFinishedItemUse: true);
    }
}
