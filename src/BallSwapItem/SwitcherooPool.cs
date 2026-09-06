using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BallSwapItem;

/// <summary>
/// Puts the Switcheroo in the game's item pools, so the game's own weighted draw picks it and the
/// weight behind that draw is the one the host sets in the match setup screen.
///
/// An earlier version of this mod gave up on the pools: the lobby's rules screen maps an item to
/// its slider through <c>itemOrderLookup</c>, an array sized to the game's own item list, and threw
/// on a modded type. <see cref="MatchSetupRulesPatch"/> rebuilds that array, which is what makes
/// holding the item in a pool safe.
///
/// The mobility pools — spring boots and the golf cart — are deliberately left alone, so that
/// spawner's settings and its pool hash are never touched.
/// </summary>
internal static class SwitcherooPool
{
    /// <summary>
    /// Set when the pools or the rules screen could not be extended. The item then goes back to
    /// being picked at draw time, so it still turns up in play but has no slider.
    /// </summary>
    public static bool FallbackActive { get; private set; }

    /// <summary>The spawner settings we manage: everything except mobility.</summary>
    private static ItemSpawnerSettings? managed;

    /// <summary>
    /// Instance ids of the pools we are responsible for, refreshed whenever the runtime pools are
    /// rebuilt. Used by the fallback draw so it never reaches into a mobility pool.
    /// </summary>
    private static readonly HashSet<int> ManagedPools = new();

    /// <summary>Told to us by the rules screen, which holds both spawner settings objects.</summary>
    public static void Remember(ItemSpawnerSettings settings) => managed = settings;

    public static bool IsManaged(ItemSpawnerSettings settings) => managed == settings;

    public static bool IsManagedPool(ItemPool pool) => ManagedPools.Contains(pool.GetInstanceID());

    /// <summary>
    /// Adds the Switcheroo to every pool of the managed spawner, runtime copies and the assets
    /// behind them alike. The assets matter: the rules screen reads its defaults from them, so an
    /// item missing there would be reset to zero by the screen's own reset button.
    /// </summary>
    public static void EnsureInjected()
    {
        if (!Plugin.Enabled.Value || managed == null || FallbackActive)
        {
            return;
        }

        try
        {
            ManagedPools.Clear();

            Inject(managed.AheadOfBallItemPoolDefaults);
            Inject(managed.AheadOfBallItemPool);

            foreach (ItemSpawnerSettings.ItemPoolData pool in managed.ItemPoolsDefaults)
            {
                Inject(pool.pool);
            }

            foreach (ItemSpawnerSettings.ItemPoolData pool in managed.ItemPools)
            {
                Inject(pool.pool);
            }
        }
        catch (Exception e)
        {
            UseFallback($"the Switcheroo could not be added to the item pools: {e}");
        }
    }

    private static void Inject(ItemPool? pool)
    {
        if (pool == null)
        {
            return;
        }

        // Recorded even when it already holds the item: this is also how the fallback draw knows
        // which pools are ours.
        ManagedPools.Add(pool.GetInstanceID());

        if (pool.ContainsItemType(Switcheroo.Type))
        {
            return;
        }

        float weight = RankWeight(pool, warnIfMissing: true);
        if (weight <= 0f)
        {
            Plugin.Log.LogWarning($"Pool '{pool.name}' has no weights to rank against; leaving it alone.");
            return;
        }

        ItemPool.ItemSpawnChance[] chances = pool.spawnChances;
        Array.Resize(ref chances, chances.Length + 1);
        chances[^1] = new ItemPool.ItemSpawnChance
        {
            item = Switcheroo.Type,
            spawnChanceWeight = weight,
        };
        pool.spawnChances = chances;

        // The pool's total and its hash are stored fields the editor refreshes, not properties.
        // Without this the draw runs against a stale total and ignores the weight entirely.
        pool.UpdateTotalWeight();

        Plugin.Log.LogInfo($"Added the Switcheroo to pool '{pool.name}' at weight {weight:0.###}.");
    }

    /// <summary>
    /// The Switcheroo sits at the same rarity as the Orbital Laser and the Thunderstorm, read from
    /// the pool itself rather than written down here, so a rebalance in a game update carries the
    /// Switcheroo along with the items it is ranked against.
    /// </summary>
    internal static float RankWeight(ItemPool pool, bool warnIfMissing)
    {
        float laser = pool.GetSpawnChanceWeight(ItemType.OrbitalLaser);
        float storm = pool.GetSpawnChanceWeight(ItemType.Thunderstorm);

        if (laser > 0f && storm > 0f)
        {
            return (laser + storm) / 2f;
        }

        if (laser > 0f || storm > 0f)
        {
            return Mathf.Max(laser, storm);
        }

        // Neither is in this pool. Fall back to the rarest thing it does have, and say so: a game
        // update that moves those items around is worth noticing rather than silently absorbing.
        float rarest = 0f;
        foreach (ItemPool.ItemSpawnChance chance in pool.SpawnChances)
        {
            if (chance.item == Switcheroo.Type || chance.spawnChanceWeight <= 0f)
            {
                continue;
            }

            if (rarest == 0f || chance.spawnChanceWeight < rarest)
            {
                rarest = chance.spawnChanceWeight;
            }
        }

        if (warnIfMissing)
        {
            Plugin.Log.LogWarning(
                $"Pool '{pool.name}' has neither the Orbital Laser nor the Thunderstorm; "
                + $"ranking the Switcheroo against its rarest item at weight {rarest:0.###}.");
        }

        return rarest;
    }

    /// <summary>
    /// Gives up on the pools and goes back to picking the item as a draw is made. Loud on purpose:
    /// the item still appears, so nothing looks broken in play, and the missing slider would
    /// otherwise be a mystery a game update later.
    /// </summary>
    public static void UseFallback(string reason)
    {
        if (FallbackActive)
        {
            return;
        }

        FallbackActive = true;
        Plugin.Log.LogError(
            $"Switcheroo item probabilities are unavailable: {reason} Falling back to picking the "
            + "item as items are drawn, so it still spawns, but it has no slider in the match setup.");

        Withdraw();
    }

    /// <summary>
    /// Takes the item back out of the pools. A half-injected pool with a rules screen that cannot
    /// map it is the one state that throws, so the fallback undoes its own groundwork.
    /// </summary>
    private static void Withdraw()
    {
        if (managed == null)
        {
            return;
        }

        try
        {
            RemoveFrom(managed.AheadOfBallItemPoolDefaults);
            RemoveFrom(managed.AheadOfBallItemPool);

            foreach (ItemSpawnerSettings.ItemPoolData pool in managed.ItemPoolsDefaults)
            {
                RemoveFrom(pool.pool);
            }

            foreach (ItemSpawnerSettings.ItemPoolData pool in managed.ItemPools)
            {
                RemoveFrom(pool.pool);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not take the Switcheroo back out of the item pools: {e}");
        }
    }

    private static void RemoveFrom(ItemPool? pool)
    {
        if (pool == null || !pool.ContainsItemType(Switcheroo.Type))
        {
            return;
        }

        List<ItemPool.ItemSpawnChance> kept = new(pool.spawnChances.Length);
        foreach (ItemPool.ItemSpawnChance chance in pool.spawnChances)
        {
            if (chance.item != Switcheroo.Type)
            {
                kept.Add(chance);
            }
        }

        pool.spawnChances = kept.ToArray();
        pool.UpdateTotalWeight();
    }
}

/// <summary>
/// The runtime pools are fresh copies of the assets, remade whenever the spawner resets, so the
/// item has to be put back into the new copies.
/// </summary>
[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.ResetRuntimeData))]
internal static class ItemSpawnerRuntimePoolPatch
{
    private static void Postfix(ItemSpawnerSettings __instance)
    {
        if (SwitcherooPool.IsManaged(__instance))
        {
            SwitcherooPool.EnsureInjected();
        }
    }
}

/// <summary>
/// The safety net, inert unless the pools or the rules screen could not be extended. It is how the
/// mod used to work: intercept the single point where a pool picks an item, at a rate matching the
/// weight the item would have carried.
/// </summary>
[HarmonyPatch(typeof(ItemPool), nameof(ItemPool.GetWeightedRandomItem))]
internal static class ItemPoolDrawPatch
{
    private static bool Prefix(ItemPool __instance, ref ItemType __result)
    {
        if (!Plugin.Enabled.Value || !SwitcherooPool.FallbackActive)
        {
            return true;
        }

        if (!SwitcherooPool.IsManagedPool(__instance))
        {
            return true;
        }

        float weight = SwitcherooPool.RankWeight(__instance, warnIfMissing: false);
        float total = __instance.TotalSpawnChanceWeight;
        if (weight <= 0f || total <= 0f)
        {
            return true;
        }

        // The share the item would have held had it been in the pool, since it is not.
        if (UnityEngine.Random.value >= weight / (total + weight))
        {
            return true;
        }

        __result = Switcheroo.Type;
        return false;
    }
}
