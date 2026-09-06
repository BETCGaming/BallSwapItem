using System;
using HarmonyLib;
using UnityEngine.Localization.Settings;

namespace BallSwapItem;

/// <summary>
/// Gives the Switcheroo a readable name everywhere the game asks for one.
///
/// Item names are looked up in the Data string table under "ITEM_&lt;type&gt;". Our type has no
/// entry, so Unity Localization falls back to printing the table and key — which is the
/// "Data/ITEM_200" players see when hovering a dropped Switcheroo. Adding the entry at runtime
/// fixes every place that name appears at once, rather than patching each piece of UI.
/// </summary>
internal static class SwitcherooLocalization
{
    private static bool applied;

    public static void Initialize()
    {
        // Each locale has its own table, so the entry has to be re-added when the player
        // switches language.
        LocalizationManager.LanguageChanged += () => applied = false;
    }

    /// <summary>Retried from <see cref="ModRunner"/> until the table has finished loading.</summary>
    public static void EnsureApplied()
    {
        if (applied)
        {
            return;
        }

        try
        {
            // Fully qualified: the game has its own StringTable enum in the global namespace.
            UnityEngine.Localization.Tables.StringTable? table =
                LocalizationSettings.StringDatabase?.GetTable(global::StringTable.Data.ToString());

            if (table == null)
            {
                return;
            }

            string key = $"ITEM_{(int)Switcheroo.Type}";
            table.AddEntry(key, Switcheroo.DisplayName);

            applied = true;
            Plugin.Log.LogInfo($"Registered the name '{Switcheroo.DisplayName}' as {key}.");
        }
        catch (Exception e)
        {
            applied = true;
            Plugin.Log.LogWarning($"Could not name the Switcheroo, its name may show as a key: {e.Message}");
        }
    }
}

/// <summary>
/// The player animator selects hold and use animations from an "Equipped item" integer set to
/// the ItemType. Our value matches no state, which is why the Switcheroo had no button-press
/// animation and sat in the hand in the default pose. Presenting it to the animator as the
/// Orbital Laser gives it the device's own animations, which is what it is built from.
/// </summary>
[HarmonyPatch(typeof(PlayerAnimatorIo), nameof(PlayerAnimatorIo.SetEquippedItem))]
internal static class AnimatorEquippedItemPatch
{
    private static void Prefix(ref ItemType equippedItem)
    {
        if (equippedItem == Switcheroo.Type)
        {
            equippedItem = ItemType.OrbitalLaser;
        }
    }
}
