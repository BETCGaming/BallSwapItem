using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Gives the Switcheroo something to hold.
///
/// The model in a player's hands is not <see cref="ItemData.Prefab"/> — that is the physical
/// pickup lying on the course. Held models come from a separate system: an
/// <see cref="EquipmentType"/> resolved through <see cref="EquipmentCollection"/>, chosen by a
/// switch in PlayerInventory that falls through to EquipmentType.None for any item it does not
/// recognise. That fall-through is why the Switcheroo was invisible in hand.
///
/// So we register our own equipment type carrying a hot pink clone of the Orbital Laser's hand
/// model, and teach that switch about it.
/// </summary>
internal static class SwitcherooEquipment
{
    /// <summary>Well clear of the game's own EquipmentType range (None..JumboBurger, 0..23).</summary>
    public const EquipmentType Type = (EquipmentType)200;

    private static EquipmentSettings? settings;
    private static bool failedOnce;

    /// <summary>
    /// The collection initializes when a match loads its assets, which may be after the player
    /// already holds something, so registration is also driven from the point of use.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (!SingletonBehaviour<EquipmentManager>.HasInstance)
        {
            return;
        }

        EquipmentCollection? collection = SingletonBehaviour<EquipmentManager>.Instance.equipmentCollection;
        if (collection != null)
        {
            Register(collection);
        }
    }

    public static void Register(EquipmentCollection collection)
    {
        if (!Plugin.Enabled.Value || failedOnce)
        {
            return;
        }

        try
        {
            settings ??= Build(collection);
            if (settings is null)
            {
                return;
            }

            if (Array.IndexOf(collection.equipment, settings) < 0)
            {
                EquipmentSettings[] all = collection.equipment;
                Array.Resize(ref all, all.Length + 1);
                all[^1] = settings;
                collection.equipment = all;
            }

            collection.equipmentDictionary[Type] = settings;
        }
        catch (Exception e)
        {
            failedOnce = true;
            Plugin.Log.LogError($"Could not register the Switcheroo's held model: {e}");
        }
    }

    private static EquipmentSettings? Build(EquipmentCollection collection)
    {
        if (!collection.TryGetEquipmentSettings(EquipmentType.OrbitalLaser, out EquipmentSettings donor)
            || donor.Prefab == null)
        {
            // Not loaded yet; the next Initialize retries.
            return null;
        }

        // Same reason as the pickup prefab: cloning an active prefab runs the clone's Awake
        // outside the game's own flow.
        GameObject donorObject = donor.Prefab.gameObject;
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

        clone.name = "SwitcherooModel";
        Switcheroo.Recolour(clone);

        // The model hangs off a wrapper rather than being the root itself. EquipmentSwitcher
        // resets whatever it attaches to Quaternion.identity, so a rotation on the root would be
        // discarded; on the wrapper's child it survives. Rotating the root's children directly
        // does not work either, since the model's renderers may sit on the root.
        GameObject root = new("SwitcherooEquipment");
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);

        clone.transform.SetParent(root.transform, worldPositionStays: false);

        // The clone was made while the donor prefab was switched off, so it is inactive.
        // EquipmentManager activates the object it instantiates, which is now the wrapper, so
        // without this the model underneath stays hidden and the hand appears empty.
        clone.SetActive(true);

        clone.transform.localPosition = Vector3.zero;

        // Deliberately not configurable. Every client builds its own copy of the held model, so
        // a per-client setting means two players disagree about how the device is oriented in
        // the same hand — which is exactly what a rotation config produced in testing.
        clone.transform.localRotation = Quaternion.identity;

        Equipment equipment = root.AddComponent<Equipment>();
        equipment.Type = Type;

        // Keep the inner copy consistent so anything resolving Equipment through the children
        // sees our type rather than the Orbital Laser's.
        if (clone.TryGetComponent(out Equipment inner))
        {
            inner.Type = Type;
        }

        EquipmentSettings built = new();
        built.Type = Type;
        built.Prefab = equipment;

        Plugin.Log.LogInfo($"Built the Switcheroo's held model as EquipmentType {(int)Type}.");
        return built;
    }
}

[HarmonyPatch(typeof(EquipmentCollection), nameof(EquipmentCollection.Initialize))]
internal static class EquipmentCollectionInitializePatch
{
    private static void Postfix(EquipmentCollection __instance) => SwitcherooEquipment.Register(__instance);
}

/// <summary>
/// Puts the Switcheroo's model in the player's right hand.
///
/// The game's mapping is a switch expression inside LocalPlayerUpdateEquipmentSwitchers, so
/// there is no seam to extend — we handle our own item and let every other item run the
/// original. The method's visibility rules live in a local function, which the compiler emits
/// as a real method we can call, so holstering, invisibility and thrown-item handling keep
/// working rather than being reimplemented here.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), "LocalPlayerUpdateEquipmentSwitchers")]
internal static class EquipmentSwitcherPatch
{
    private static readonly MethodInfo? CanHoldEquipment = FindCanHoldEquipment();

    private static bool Prefix(PlayerInventory __instance)
    {
        if (__instance.GetEffectivelyEquippedItem() != Switcheroo.Type)
        {
            return true;
        }

        SwitcherooEquipment.EnsureRegistered();

        if (CanHoldEquipment is null)
        {
            // Fall through to the original, which leaves the hands empty. Losing the model is a
            // far better failure than second-guessing when equipment may be shown.
            return true;
        }

        bool canHold = (bool)CanHoldEquipment.Invoke(__instance, null);
        bool rightHandFree = !__instance.thrownItem.HasFlag(PlayerInventory.ThrownItemHand.Right);

        __instance.PlayerInfo.RightHandEquipmentSwitcher.SetEquipment(
            canHold && rightHandFree ? SwitcherooEquipment.Type : EquipmentType.None);
        __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);

        return false;
    }

    /// <summary>
    /// Located by name prefix: the compiler appends an index to local function names that shifts
    /// whenever the surrounding class is edited, so matching the whole name would break on a
    /// game update that touches PlayerInventory at all.
    /// </summary>
    private static MethodInfo? FindCanHoldEquipment()
    {
        MethodInfo? found = AccessTools.GetDeclaredMethods(typeof(PlayerInventory))
            .FirstOrDefault(m =>
                m.Name.StartsWith("<LocalPlayerUpdateEquipmentSwitchers>g__CanHoldEquipment", StringComparison.Ordinal)
                && m.ReturnType == typeof(bool)
                && m.GetParameters().Length == 0);

        if (found is null)
        {
            Plugin.Log.LogWarning(
                "Could not find PlayerInventory's CanHoldEquipment; the Switcheroo will not show in hand.");
        }

        return found;
    }
}
