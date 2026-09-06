using System;
using HarmonyLib;

namespace BallSwapItem;

/// <summary>
/// Gives the Switcheroo an editable probability slider in the match setup, like any other item.
///
/// The screen builds one slider per entry in <c>itemOrder</c> and labels it from the item's
/// localized name, so the item only has to be in that array to get a row of its own.
///
/// What stopped this before is <c>itemOrderLookup</c>, the array beside it. The game fills that in
/// OnValidate, which is editor-only, so a build ships one entry per game item — and the two places
/// that map an item to its slider index it as <c>[(int)itemType - 1]</c>. Our type is 200, so both
/// read past the end. Rebuilding the array with the game's own algorithm, sized to fit our type, is
/// what makes everything downstream safe.
/// </summary>
[HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.Initialize))]
internal static class MatchSetupRulesPatch
{
    private static void Prefix(MatchSetupRules __instance)
    {
        if (!Plugin.Enabled.Value || SwitcherooPool.FallbackActive)
        {
            return;
        }

        try
        {
            Extend(__instance);
        }
        catch (Exception e)
        {
            SwitcherooPool.UseFallback($"the match setup screen could not be extended: {e}");
            return;
        }

        // Only once the screen can map the item: Initialize restores saved weights and seeds the
        // synced ones from each pool's contents before it builds a single slider, and both of
        // those paths go through the lookup we have just rebuilt.
        SwitcherooPool.Remember(__instance.itemSpawnerSettings);
        SwitcherooPool.EnsureInjected();
    }

    private static void Extend(MatchSetupRules rules)
    {
        ItemType[] order = rules.itemOrder;
        if (Array.IndexOf(order, Switcheroo.Type) < 0)
        {
            ItemType[] extended = new ItemType[order.Length + 1];
            Array.Copy(order, extended, order.Length);
            extended[^1] = Switcheroo.Type;
            order = extended;
        }

        // The game's own mapping, over an array long enough to be indexed by our type. Items with
        // no slider land on -1 exactly as they would in the editor, and are never looked up.
        int[] lookup = new int[Math.Max((int)Switcheroo.Type, order.Length)];
        for (int i = 0; i < lookup.Length; i++)
        {
            lookup[i] = Array.IndexOf(order, (ItemType)(i + 1));
        }

        // Assigned together and last: a screen holding the item in itemOrder while the lookup
        // still has the game's original length is the one arrangement that throws.
        rules.itemOrder = order;
        rules.itemOrderLookup = lookup;
    }
}
