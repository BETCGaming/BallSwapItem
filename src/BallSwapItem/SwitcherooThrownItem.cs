using System;
using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// The spent device the player tosses away once the Switcheroo has been used.
///
/// This is a third model, distinct from the one held in the hand and the pickup lying on the
/// course: the game throws a lightweight, short-lived copy keyed by <see cref="ThrownUsedItemType"/>.
/// Registering our own keeps the discarded device the same colour as the one that was held,
/// instead of turning grey the moment it leaves the hand.
/// </summary>
internal static class SwitcherooThrownItem
{
    /// <summary>Clear of the game's own range (Airhorn..Railgun, 0..17).</summary>
    public const ThrownUsedItemType Type = (ThrownUsedItemType)200;

    private static bool registered;
    private static bool failedOnce;

    /// <summary>
    /// Needed on every client, not just the thrower: the throw is replicated, and each client
    /// builds the discarded model from its own registration.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (registered || failedOnce || !Plugin.Enabled.Value)
        {
            return;
        }

        if (!SingletonBehaviour<ThrownUsedItemManager>.HasInstance)
        {
            return;
        }

        try
        {
            if (ThrownUsedItemManager.prefabPerType.ContainsKey(Type))
            {
                registered = true;
                return;
            }

            if (!ThrownUsedItemManager.prefabPerType.TryGetValue(ThrownUsedItemType.OrbitalLaser, out ThrownUsedItem donor)
                || donor == null)
            {
                return;
            }

            GameObject donorObject = donor.gameObject;
            bool wasActive = donorObject.activeSelf;
            donorObject.SetActive(false);

            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(donorObject);
            }
            finally
            {
                donorObject.SetActive(wasActive);
            }

            clone.name = "SwitcherooThrownItem";
            UnityEngine.Object.DontDestroyOnLoad(clone);
            Switcheroo.Recolour(clone);

            // The clone carries the donor's serialized type, which decides which pool it is
            // returned to when it expires.
            ThrownUsedItem thrown = clone.GetComponent<ThrownUsedItem>();
            thrown.type = Type;

            // Left inactive on purpose: this is a template, and the manager activates the copy
            // it instantiates from it.
            ThrownUsedItemManager.prefabPerType[Type] = thrown;

            registered = true;
            Plugin.Log.LogInfo($"Registered the discarded Switcheroo as ThrownUsedItemType {(int)Type}.");
        }
        catch (Exception e)
        {
            failedOnce = true;
            Plugin.Log.LogError($"Could not register the discarded Switcheroo: {e}");
        }
    }
}

/// <summary>
/// Lets the game throw our discarded device at all.
///
/// ThrowUsedItemInternal opens with a switch over <see cref="ThrownUsedItemType"/> that picks the
/// hand, the throw rotation, the angular velocity and the speed, and its default arm throws
/// SwitchExpressionException. Our type landed there, so the throw died before ever reaching
/// GetUnusedThrownItem — the lookup <see cref="SwitcherooThrownItem"/> feeds — and took the
/// calling coroutine down with it, leaving the spent item in the slot and the player stuck in a
/// use that never finished.
///
/// So we handle our own type and let every other item run the original. This has to work on
/// every client rather than only the thrower, which it does: the receiving end of
/// RpcThrowUsedItem funnels into this same method.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), "ThrowUsedItemInternal")]
internal static class ThrowUsedItemPatch
{
    private static bool Prefix(
        PlayerInventory __instance,
        ThrownUsedItemType thrownItemType,
        bool forcePlayerPosition,
        Vector3 forcedPlayerPosition)
    {
        if (thrownItemType != SwitcherooThrownItem.Type)
        {
            return true;
        }

        try
        {
            SwitcherooThrownItem.EnsureRegistered();
            Throw(__instance, forcePlayerPosition, forcedPlayerPosition);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not throw the spent Switcheroo: {e.Message}");
        }

        // Never fall through: the original holds nothing for our type but the exception.
        return false;
    }

    /// <summary>
    /// The Orbital Laser's arm of the game's switch, followed by the shared tail of
    /// ThrowUsedItemInternal. The spring boot and rocket driver special cases in that tail are
    /// left out because neither can apply to our type.
    /// </summary>
    private static void Throw(PlayerInventory inventory, bool forcePlayerPosition, Vector3 forcedPlayerPosition)
    {
        ThrownUsedItem device = ThrownUsedItemManager.GetUnusedThrownItem(SwitcherooThrownItem.Type);
        if (device == null)
        {
            // GetUnusedThrownItem logs its own error. Losing the discarded model is cosmetic;
            // what matters is that the caller's use routine still runs to the end.
            return;
        }

        PlayerInfo player = inventory.PlayerInfo;
        Quaternion throwRotation = GameManager.ItemSettings.OrbitalLaserThrowDirectionLocalRotation;
        Vector3 localAngularVelocity = GameManager.ItemSettings.OrbitalLaserThrowLocalAngularVelocity;

        player.RightHandEquipmentSwitcher.transform.GetPositionAndRotation(
            out Vector3 position,
            out Quaternion rotation);

        if (forcePlayerPosition)
        {
            position += forcedPlayerPosition - inventory.transform.position;
        }

        Vector3 direction = inventory.transform.TransformDirection(throwRotation * Vector3.forward);
        Vector3 spin = inventory.transform.TransformDirection(throwRotation * localAngularVelocity);
        Vector3 velocity = player.Rigidbody.linearVelocity
            + direction * GameManager.ItemSettings.OrbitalLaserThrowSpeed;
        Vector3 angularVelocity = player.Rigidbody.angularVelocity + spin;

        device.Initialize(position, rotation, velocity, angularVelocity, player.GetEffectiveTeam());
        PhysicsManager.TemporarilyIgnoreCollisionsBetween(player.AsEntity, device.AsEntity, 0.5f);
    }
}
