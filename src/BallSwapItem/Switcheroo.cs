using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Registers the Switcheroo as a real item in the game's <see cref="ItemCollection"/>.
///
/// The game's ItemType enum runs None..JumboBurger (0..17). We claim a value far above that
/// so an official item added in a future update can't collide with ours.
/// </summary>
internal static class Switcheroo
{
    public const ItemType Type = (ItemType)200;

    public const string DisplayName = "Switcheroo";

    /// <summary>Hot pink #FF69B4, the agreed device colour.</summary>
    private static readonly Color DeviceColor = new(1f, 105f / 255f, 180f / 255f, 1f);

    private static ItemData? itemData;
    private static bool failedOnce;

    /// <summary>
    /// Called after every <see cref="ItemCollection.Initialize"/>. Initialize() rebuilds its
    /// dictionary from the backing array, so we make sure we are present in both.
    /// </summary>
    public static void Register(ItemCollection collection)
    {
        if (!Plugin.Enabled.Value || failedOnce)
        {
            return;
        }

        try
        {
            itemData ??= BuildItemData(collection);
            if (itemData is null)
            {
                return;
            }

            if (Array.IndexOf(collection.items, itemData) < 0)
            {
                ItemData[] items = collection.items;
                Array.Resize(ref items, items.Length + 1);
                items[^1] = itemData;
                collection.items = items;
            }

            collection.allItemData[Type] = itemData;
        }
        catch (Exception e)
        {
            // Registering is all-or-nothing: a half-registered item would hand the game an
            // ItemType it cannot resolve, so give up loudly rather than limp on.
            failedOnce = true;
            Plugin.Log.LogError($"Failed to register the Switcheroo item, it will not appear: {e}");
        }
    }

    private static ItemData? BuildItemData(ItemCollection collection)
    {
        if (!collection.TryGetItemData(ItemType.OrbitalLaser, out ItemData donor))
        {
            Plugin.Log.LogError("Orbital Laser item data not found; cannot build the Switcheroo from it.");
            return null;
        }

        if (donor.Prefab == null)
        {
            // Asset not loaded yet. Not cached and not fatal: the next Initialize retries.
            return null;
        }

        ItemData data = new();

        // Copy every field off the donor, then override only what makes the Switcheroo its own
        // item. Field-copying rather than property assignment keeps this working if the game
        // adds new ItemData fields in an update.
        foreach (FieldInfo field in typeof(ItemData).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            field.SetValue(data, field.GetValue(donor));
        }

        data.Type = Type;
        data.Icon = BuildIcon();
        data.Prefab = BuildPrefab(donor.Prefab);
        data.MaxUses = 1;
        // Fires the moment the use button is pressed, with no aiming step.
        data.NonAimUse = ItemNonAimingUse.Use;
        // The swap reassigns ownership rather than hitting anything, so the game's
        // ball-collision bookkeeping for item usage does not apply.
        data.CanUsageAffectBalls = false;
        data.CanUsageAffectTeammateBalls = false;
        data.IsExplosive = false;
        data.CanBreakBreakableIce = false;
        data.CanHitProjectiles = false;

        // Null so LocalizedName resolves for our own key rather than inheriting "Orbital Laser".
        data.name = null;
        data.Initialize();

        Plugin.Log.LogInfo($"Built Switcheroo item data as ItemType {(int)Type}.");
        return data;
    }

    private static GameObject BuildPrefab(GameObject donorPrefab)
    {
        // Instantiating an active prefab runs the clone's Awake immediately, and PhysicalItem
        // .Awake throws when it runs outside the game's own spawn flow. Deactivating the source
        // across the Instantiate keeps the clone dormant until the game spawns it properly.
        bool wasActive = donorPrefab.activeSelf;
        donorPrefab.SetActive(false);

        GameObject prefab;
        try
        {
            prefab = UnityEngine.Object.Instantiate(donorPrefab);
        }
        finally
        {
            donorPrefab.SetActive(wasActive);
        }

        prefab.name = "SwitcherooItem";
        UnityEngine.Object.DontDestroyOnLoad(prefab);

        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            foreach (Material material in renderer.materials)
            {
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", DeviceColor);
                }

                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", DeviceColor);
                }
            }
        }

        return prefab;
    }

    private static Sprite? BuildIcon()
    {
        using Stream? stream = typeof(Switcheroo).Assembly
            .GetManifestResourceStream("BallSwapItem.Assets.switcheroo-icon.png");

        if (stream is null)
        {
            Plugin.Log.LogWarning("Embedded Switcheroo icon missing; the item will show no icon.");
            return null;
        }

        byte[] bytes = new byte[stream.Length];
        _ = stream.Read(bytes, 0, bytes.Length);

        Texture2D texture = new(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (!texture.LoadImage(bytes))
        {
            Plugin.Log.LogWarning("Switcheroo icon failed to decode; the item will show no icon.");
            return null;
        }

        texture.name = "SwitcherooIcon";
        UnityEngine.Object.DontDestroyOnLoad(texture);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
        sprite.name = "SwitcherooIcon";
        UnityEngine.Object.DontDestroyOnLoad(sprite);
        return sprite;
    }
}

[HarmonyPatch(typeof(ItemCollection), nameof(ItemCollection.Initialize))]
internal static class ItemCollectionInitializePatch
{
    private static void Postfix(ItemCollection __instance) => Switcheroo.Register(__instance);
}

/// <summary>
/// The game builds item names from a localization key (ITEM_&lt;type&gt;) that does not exist for
/// a modded item, so supply the display name directly.
/// </summary>
[HarmonyPatch(typeof(ItemData), nameof(ItemData.Name), MethodType.Getter)]
internal static class ItemDataNamePatch
{
    private static bool Prefix(ItemData __instance, ref string __result)
    {
        if (__instance.Type != Switcheroo.Type)
        {
            return true;
        }

        __result = Switcheroo.DisplayName;
        return false;
    }
}
