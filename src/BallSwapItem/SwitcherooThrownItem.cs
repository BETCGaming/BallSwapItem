using System;
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
