using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace BallSwapItem;

/// <summary>
/// Puts the once-per-hole limit in the match setup's Battle section, as an on/off row beside the
/// game's own battle rules, rather than leaving it a host-only config key nobody in the lobby can
/// see.
///
/// The rules machinery takes a modded value far more easily than the item pools did: every lookup
/// is a dictionary keyed by Rule rather than an array indexed by it, and GetDefaultValue ends in a
/// default arm returning zero — which is exactly the "off" this rule wants. So the only real work
/// is the row itself, which unlike the item sliders is authored in the scene rather than generated,
/// and therefore has to be cloned from one of its neighbours.
/// </summary>
[HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.Initialize))]
internal static class MatchSetupRuleRowPatch
{
    private const string LabelKey = "RULE_SWITCHEROO_ONCE_PER_HOLE";
    private const string LabelText = "One Switcheroo per hole";

    /// <summary>The row we built, so entering the lobby again does not stack up copies.</summary>
    private static DropdownOption? row;

    /// <summary>Which screen that row belongs to, so a rebuilt screen gets a row of its own.</summary>
    private static int ownerId;

    /// <summary>Where the rule labels live, learned from the row we cloned rather than assumed.</summary>
    private static TableReference labelTable;
    private static bool labelTableKnown;
    private static bool languageHookInstalled;

    /// <summary>
    /// A postfix, unlike the item probabilities: the game's own InitDropdownOnOff calls happen
    /// inside Initialize, and ours has to follow them.
    /// </summary>
    private static void Postfix(MatchSetupRules __instance)
    {
        if (!Plugin.Enabled.Value)
        {
            return;
        }

        try
        {
            Build(__instance);
        }
        catch (Exception e)
        {
            // Nothing to undo and no gameplay path to restore: without a row the rule simply
            // resolves to its default of off, which is how the item behaves today.
            Plugin.Log.LogError(
                $"Could not add the once-per-hole row to the match setup: {e} The rule stays off "
                + "and the Switcheroo can be used as often as it is found.");
        }
    }

    private static void Build(MatchSetupRules rules)
    {
        // Anything that groups rules by section should see ours in the right one.
        MatchSetupRules.CategoryPerRule[Switcheroo.OncePerHoleRule] = MatchSetupRules.RuleCategory.Battle;

        // A destroyed row reads as null through Unity's own comparison, and a screen rebuilt from
        // scratch needs a row of its own rather than one pointing into the old one.
        if (row == null || ownerId != rules.GetInstanceID())
        {
            if (!Clone(rules))
            {
                return;
            }
        }

        // Re-initialized on every visit, exactly as the game does with its own rows: this is what
        // registers it in onOffDropdownLookup and sets the dropdown from the current value, so a
        // second visit does not show a stale one.
        AccessTools.Method(typeof(MatchSetupRules), "InitDropdownOnOff")
            .Invoke(rules, new object[] { row!, Switcheroo.OncePerHoleRule, null! });
    }

    private static bool Clone(MatchSetupRules rules)
    {
        DropdownOption source = rules.hitOtherPlayersBalls;
        if (source == null)
        {
            Plugin.Log.LogWarning("No battle row to copy; the once-per-hole rule has no control.");
            return false;
        }

        GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
        clone.name = "SwitcherooOncePerHole";
        clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);

        DropdownOption option = clone.GetComponent<DropdownOption>();
        if (option == null)
        {
            UnityEngine.Object.Destroy(clone);
            Plugin.Log.LogWarning("The copied battle row has no DropdownOption; leaving the rule out of the UI.");
            return false;
        }

        Label(clone);

        row = option;
        ownerId = rules.GetInstanceID();
        Plugin.Log.LogInfo("Added the once-per-hole rule to the match setup's Battle section.");
        return true;
    }

    /// <summary>
    /// The clone arrives labelled as the row it was copied from. The dropdown's own On and Off
    /// options are localized by their own component and come across correctly, so only this label
    /// needs changing.
    /// </summary>
    private static void Label(GameObject clone)
    {
        LocalizeStringEvent? label = clone.GetComponentInChildren<LocalizeStringEvent>(includeInactive: true);
        if (label == null)
        {
            Plugin.Log.LogWarning("The copied row has no localized label; it will read as the row it was copied from.");
            return;
        }

        // The table is read off the row we copied rather than guessed: the item name lives in the
        // Data table, and there is no reason the rule labels have to.
        labelTable = label.StringReference.TableReference;
        labelTableKnown = true;

        if (AddEntry())
        {
            label.StringReference.TableEntryReference = LabelKey;
            label.RefreshString();
            InstallLanguageHook();
            return;
        }

        // The table was not available. Plain text on the label beats showing a raw key, and the
        // component has to go or it would put the copied row's text back.
        TMP_Text? text = label.GetComponent<TMP_Text>();
        UnityEngine.Object.Destroy(label);

        if (text != null)
        {
            text.text = LabelText;
            Plugin.Log.LogWarning("Localization was not ready; the once-per-hole row is labelled in English only.");
        }
    }

    private static bool AddEntry()
    {
        if (!labelTableKnown)
        {
            return false;
        }

        try
        {
            // Fully qualified: the game has its own StringTable enum in the global namespace.
            UnityEngine.Localization.Tables.StringTable? table =
                LocalizationSettings.StringDatabase?.GetTable(labelTable);
            if (table == null)
            {
                return false;
            }

            table.AddEntry(LabelKey, LabelText);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not name the once-per-hole rule: {e.Message}");
            return false;
        }
    }

    /// <summary>Each locale has its own table, so the entry has to be re-added on a switch.</summary>
    private static void InstallLanguageHook()
    {
        if (languageHookInstalled)
        {
            return;
        }

        LocalizationManager.LanguageChanged += () => AddEntry();
        languageHookInstalled = true;
    }
}
