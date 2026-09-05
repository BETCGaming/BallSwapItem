using System;
using HarmonyLib;

namespace BallSwapItem;

/// <summary>
/// Puts the Switcheroo into the item pools so it can be found in normal play.
///
/// The game clones its pool assets into runtime copies on load (ItemSpawnerSettings
/// .ResetRuntimeData), and every spawn draws from those copies, so injecting there leaves the
/// shipped assets untouched. It also leaves the pool hash alone: that hash is built from the
/// serialized defaults and exists to catch version mismatches between host and client.
/// </summary>
internal static class SwitcherooPool
{
    public static void InjectAll(ItemSpawnerSettings settings)
    {
        if (!Plugin.Enabled.Value)
        {
            return;
        }

        try
        {
            foreach (ItemSpawnerSettings.ItemPoolData data in settings.ItemPools)
            {
                Inject(data.pool);
            }

            Inject(settings.AheadOfBallItemPool);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not add the Switcheroo to the item pools: {e}");
        }
    }

    private static void Inject(ItemPool? pool)
    {
        if (pool == null || pool.ContainsItemType(Switcheroo.Type))
        {
            return;
        }

        ItemPool.ItemSpawnChance[] chances = pool.spawnChances;
        if (chances.Length == 0)
        {
            return;
        }

        // Weight relative to what else is in this pool, so the item stays rare whatever the
        // game's own numbers are and whatever the host has tuned.
        float total = 0f;
        foreach (ItemPool.ItemSpawnChance chance in chances)
        {
            total += chance.spawnChanceWeight;
        }

        float weight = Math.Max(0.01f, total / chances.Length * Plugin.SpawnRarity.Value);

        Array.Resize(ref chances, chances.Length + 1);
        chances[^1] = new ItemPool.ItemSpawnChance
        {
            item = Switcheroo.Type,
            spawnChanceWeight = weight,
        };

        pool.spawnChances = chances;
        pool.UpdateTotalWeight();

        Plugin.Log.LogInfo($"Added the Switcheroo to pool '{pool.name}' at weight {weight:0.###}.");
    }
}

[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.ResetRuntimeData))]
internal static class ItemSpawnerSettingsResetPatch
{
    private static void Postfix(ItemSpawnerSettings __instance) => SwitcherooPool.InjectAll(__instance);
}

/// <summary>
/// Belt and braces: the runtime pools are rebuilt at points we do not control, so make sure the
/// Switcheroo is present at the one moment that actually matters — when an item is drawn.
/// Injection is a no-op once the pool already contains it.
/// </summary>
[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.GetRandomItemFor))]
internal static class ItemSpawnerSettingsGetRandomPatch
{
    private static void Prefix(ItemSpawnerSettings __instance) => SwitcherooPool.InjectAll(__instance);
}
