using System;
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

        // Someone else's swap is already counting down. A second one cannot do anything until
        // the first lands, and the host would turn it down, so it is stopped here rather than
        // being spent for nothing.
        if (SwitcherooNetwork.SwapInProgress)
        {
            Trace.Log("use refused: a swap is already counting down");
            Refuse("SWAP IN PROGRESS");
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

        // Nothing is spent until the host says the use is going ahead. Two players pressing
        // within a network round-trip of each other both used to lose their Switcheroo here:
        // the host could only honour the first, and never told the second.
        for (float waited = 0f;
             SwitcherooNetwork.RequestState == SwapRequestState.Waiting;
             waited += Time.deltaTime)
        {
            if (waited >= SwitcherooNetwork.ReplyTimeoutSeconds)
            {
                SwitcherooNetwork.TimeOutRequest();
                break;
            }

            yield return null;
        }

        if (SwitcherooNetwork.RequestState != SwapRequestState.Accepted)
        {
            // Keep the item: an unanswered use is far more likely to be a rule refusing it than
            // a swap that silently went ahead.
            Trace.Log($"routine: use denied ({SwitcherooNetwork.DenialText}), keeping the item");
            Refuse(SwitcherooNetwork.DenialText);
            inventory.SetCurrentItemUse(ItemUseType.None);
            yield break;
        }

        Trace.Log("routine: host accepted, spending the item");
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
                // The swap is already committed by this point and the toss is cosmetic, so a
                // throw that fails must not abort the routine: everything after this loop is
                // what clears the use state and removes the spent item. Letting an exception
                // out here is what left the player mid-use with the Switcheroo still in hand.
                try
                {
                    SwitcherooThrownItem.EnsureRegistered();
                    inventory.ThrowUsedItemForAllClients(SwitcherooThrownItem.Type);
                    inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Could not throw the spent Switcheroo: {e}");
                }

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
