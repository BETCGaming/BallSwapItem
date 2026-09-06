# Switcheroo in the match setup item probabilities

Give the Switcheroo a real, editable slider in the lobby's item-probability tab, alongside the
game's own items, and let that slider be what decides how often it spawns.

## Context

`deployment.md` lists this as an accepted caveat: *"The item has no slider in the lobby's rules
screen. That UI is built from a fixed item list, so its spawn rate is set through config instead."*

That is half right. The UI is **not** a fixed list of slider objects — `MatchSetupRules` builds one
slider per entry in a `public ItemType[] itemOrder` field at runtime. What is fixed is a lookup
array beside it, and that array is the whole reason commit 90257c7 ("Keep the Switcheroo out of the
item pools") backed the earlier attempt out.

`SwitcherooPool.cs` records the retreat: the pool array "is read in one place to actually pick an
item, and in seven places by the lobby's rules screen — several of which map an item to its slider
through an array sized to the game's own item list, and threw on a modded type."

That array is `MatchSetupRules.itemOrderLookup`, and it is `public int[]`. It can be rebuilt.

## Decisions

Settled before writing this plan:

| Question | Decision |
|---|---|
| Which pools | Every pool **except mobility** — ahead-of-ball plus the four crate pools |
| Default weight | The same rarity rank as the **Orbital Laser and the Thunderstorm**, per pool |
| `SpawnChance` config key | **Removed.** The slider is the only control, as with every game item |
| If a game update breaks the injection | **Fall back** to today's draw interception and log loudly |

## How the game actually works

Everything below is from decompiling `GameAssembly.dll`. Method names are the game's.

### The sliders are generated, not authored

`MatchSetupRules.Start` instantiates `itemPrefab` once per entry in `itemOrder`, parents it to
`itemsParent`, collects its `SliderOption` into `spawnChanceSliders`, and labels it from
`itemData.LocalizedName`:

```csharp
for (int num2 = 0; num2 < itemOrder.Length; num2++)
{
    GameObject gameObject = UnityEngine.Object.Instantiate(itemPrefab);
    ...
    if (GameManager.AllItems.TryGetItemData(itemOrder[num2], out var itemData))
        gameObject.GetComponentInChildren<LocalizeStringEvent>().StringReference = itemData.LocalizedName;
}
```

Appending our type to `itemOrder` therefore produces a slider, already labelled **Switcheroo** —
`SwitcherooLocalization` registers `ITEM_200` and `Switcheroo.BuildItemData` sets `data.Icon`.

### The one thing that throws

```csharp
protected override void OnValidate()
{
    itemOrderLookup = new int[itemOrder.Length];
    for (int i = 0; i < itemOrder.Length; i++)
        itemOrderLookup[i] = Array.IndexOf(itemOrder, (ItemType)(i + 1));
}
```

`OnValidate` is editor-only, so in a build `itemOrderLookup` is whatever was serialized: one entry
per game item. It is indexed by item type in exactly two places, both `[(int)itemType - 1]`:

- `OnSpawnChanceWeightsChangedInItemPool` (line 809) — fires from the `spawnChanceWeights`
  `OnSet`/`OnAdd` callbacks, so **any** weight for our item entering the dictionary trips it.
- `EnsureItemPoolSpawnChanceNonZero` (line 1090) — walks a pool's `SpawnChances`, so being in a
  pool at all trips it.

`ItemType.Switcheroo` is 200, so both index 199 of an 18-element array. Rebuilding the array to
200+ entries with the game's own algorithm fixes both at once. Types with no slider resolve to
`-1`, exactly as they would in the editor, and are never looked up because they are in no pool.

### Weights are already networked

```csharp
private SyncDictionary<ItemPoolId, float> spawnChanceWeights;   // key: (itemPoolIndex, itemType)
```

Host-authoritative, synced to clients, with `serverPersistentSpawnChanceWeights` carrying values
between matches. Our item needs no new networking — it is another key in a dictionary the game
already replicates. `BlockUnmodded` guarantees every peer knows the type.

### A slider is only editable if the item is in that pool

```csharp
bool itemExistsInPool = Array.Exists(itemPool.SpawnChances, sc => sc.item == item);
slider.Slider.interactable = itemExistsInPool;
```

So the item must be in `ItemPool.SpawnChances` for the slider to do anything. **This is the step
that used to crash**, and it is safe once `itemOrderLookup` is rebuilt.

Consequence worth stating: the Switcheroo slider will still appear on the **mobility tab**, greyed
out at zero, because `itemOrder` is global to the screen while pool membership is per pool. That is
precisely how the game presents any item absent from a pool, so it follows the convention rather
than breaking it.

### There are two sets of pools, and the difference matters

```csharp
public List<ItemPoolData> ItemPools        => runtimeItemPools;   // clones, "Close item pool(Clone)"
public List<ItemPoolData> ItemPoolsDefaults => itemPools;         // the assets
public ItemPool AheadOfBallItemPool        => runtimeAheadOfBallItemPool;
public ItemPool AheadOfBallItemPoolDefaults => aheadOfBallItemPool;
```

`MatchSetupRules.GetItemPool` reads the runtime clones; `GetDefaultItemPool` reads the assets, and
that is what `GetDefaultWeight`, `GetDefaultItemFactor`, `IsSpawnChangeDefault` and the reset button
consult. **Both must contain the item** or the reset button would zero the Switcheroo while leaving
every other item at its default.

`ResetRuntimeData` rebuilds the clones from the assets with `Object.Instantiate`, so an asset that
already contains the Switcheroo yields clones that contain it too.

### Totals and hashes are stored, not computed

`ItemPool.totalSpawnChanceWeight` and `itemPoolHash` are serialized fields, refreshed only by
`UpdateTotalWeight()` — which is public. Appending to `SpawnChances` without calling it leaves
`GetWeightedRandomItem` drawing against a stale total, effectively ignoring our weight.

Calling it changes `itemPoolHash`, and `MatchSetupRules.Deserialize` compares
`serverValues.spawnChanceHash` against `GetAllItemPoolsHash()`:

```
Debug.LogWarning($"Outdated spawn chances in serialized data! (hash mismatch ...) Resetting");
```

So a saved match setup is reset **once** after installing the mod, and once more if it is ever
removed. This is honest — the pools genuinely differ — but it must be in the changelog.

Note `GetAllItemPoolsHash` hashes the **asset** pools, so this is a consequence of writing to the
defaults, which we do deliberately for the reset button.

## The weight rule

"Same rarity rank as the Orbital Laser and the Thunderstorm", resolved per pool at runtime rather
than hard-coded, so a future game rebalance carries the Switcheroo with it:

1. Read `pool.GetSpawnChanceWeight(ItemType.OrbitalLaser)` and `(ItemType.Thunderstorm)`.
2. Use the mean of whichever of the two are present (non-zero) in that pool.
3. If neither is present, use the **lowest non-zero weight** in the pool — the rarest tier — and log
   which pool fell back, so a game update that drops those items from a pool is visible.

Both are top-tier rare items, so the Switcheroo lands where a swap-everyone item belongs. It also
means the shipped default is whatever the game itself considers that rank, in every pool, with no
number for us to keep in sync.

## Implementation

### 1. `SwitcherooPool.cs` — inject into pools

Replace the draw-interception prefix as the primary mechanism (keep it, dormant, as the fallback —
step 5).

- `EnsurePools()`: for pool indices 0–4, inject into **both** the runtime pool and the defaults
  pool, then call `UpdateTotalWeight()` on each pool touched. Skip index 5 (mobility) entirely, so
  the mobility settings object and its hash stay untouched.
- Idempotent: check `ContainsItemType(Switcheroo.Type)` first. Pools are `ScriptableObject`s that
  live for the process, and this will be called more than once.
- Harmony postfix on `ItemSpawnerSettings.ResetRuntimeData` to re-inject into freshly made clones,
  covering clones created before our first injection.
- Log one line per pool, as today: `Added the Switcheroo to pool '<name>' at weight <w>.`

Reaching the settings object: `MatchSetupRules.itemSpawnerSettings` is a public field.
**To confirm during implementation:** an in-match path that does not require the setup screen (an
`ItemSpawner` component field is the likely one), so a host who somehow never opens the rules tab
still spawns the item.

### 2. New `SwitcherooRules.cs` — extend the setup screen

Harmony prefix on `MatchSetupRules.Start` (before the slider loop), doing, in order:

1. `SwitcherooPool.EnsurePools()` — the pools must contain the item before `RegisterItemPool` seeds
   `spawnChanceWeights` from `pool.SpawnChances`.
2. Append `Switcheroo.Type` to `itemOrder` if absent.
3. Rebuild `itemOrderLookup` with the game's own algorithm, sized `max((int)Switcheroo.Type,
   itemOrder.Length)`:
   ```csharp
   itemOrderLookup = new int[Math.Max((int)Switcheroo.Type, itemOrder.Length)];
   for (int i = 0; i < itemOrderLookup.Length; i++)
       itemOrderLookup[i] = Array.IndexOf(itemOrder, (ItemType)(i + 1));
   ```

All three are idempotent — the screen is rebuilt each time the lobby is entered.

Wrap the whole thing in a `try`/`catch`. On failure: set a `uiInjectionFailed` flag, log an error
naming the step, and let the original run untouched (step 5).

### 3. Remove the `SpawnChance` config key

- Delete the `Plugin.SpawnChance` binding.
- Remove its row from the README's config table; note the removal in the changelog.
- The key is left behind in existing `.cfg` files; BepInEx ignores unknown keys, and the orphan line
  is harmless. Worth a line in `deployment.md`'s local-test-settings table, which currently tells
  you to set it to 0.8.

### 4. Retire the caveat

Delete the "no slider in the lobby's rules screen" bullet from `deployment.md`'s accepted caveats —
it is the one this feature exists to remove.

### 5. Fallback path

Keep `ItemPoolDrawPatch`, gated on `uiInjectionFailed || pool injection failed`:

- Normally inert, so the game's own weighted draw does the work and the slider means something.
- When the UI could not be extended, it intercepts draws as it does today. With `SpawnChance` gone,
  its rate is the same rule as step "The weight rule", computed from the pool it was handed:
  `weight / (total + weight)`. The item keeps spawning at its intended rarity with no slider.
- Log once, at error level, naming what failed — silent degradation is what makes a game update
  hard to diagnose later.

## Verification

Single player first:

- [ ] The Switcheroo has a slider in the ahead-of-ball tab and all four crate tabs, labelled
      **Switcheroo**, with its icon.
- [ ] It is greyed out at zero on the **mobility** tab, like any item absent from a pool.
- [ ] Its default position matches the Orbital Laser's and the Thunderstorm's in each pool.
- [ ] Dragging it changes the spawn rate in play, and the preset flips to **Custom**, as with any
      item.
- [ ] Setting it to zero stops the item appearing entirely.
- [ ] **Reset spawn chances** returns it to the laser/thunderstorm rank rather than to zero.
- [ ] Opening and closing the lobby repeatedly produces exactly one slider, not one per visit.
- [ ] `Outdated spawn chances ... Resetting` appears once on the first launch after installing, and
      not on subsequent launches.
- [ ] No `IndexOutOfRangeException` in the log while the item-probability tab is open — that tab
      runs a per-frame update, so the old crash was immediate and obvious.

Two players — this is where the old attempt failed:

- [ ] The client sees the same slider values as the host, and sees them change live when the host
      drags one.
- [ ] The client's slider is read-only, as it is for every other item.
- [ ] A host-set weight actually governs what the client picks up in play.
- [ ] Joining mid-setup shows the right values rather than defaults.

## Risks

| Risk | Mitigation |
|---|---|
| A game update resizes or repurposes `itemOrderLookup` | Fallback path (step 5); rebuild uses the game's own algorithm rather than assumptions about length |
| Item type 200 collides with a future official item | Already an accepted caveat in `deployment.md`; re-check after each game patch |
| Saved match setups reset once on install | Documented in the changelog; unavoidable if the reset button is to work |
| Injecting twice into a shared `ScriptableObject` | `ContainsItemType` guard on every entry point |
| Runtime clones made before our injection | Postfix on `ResetRuntimeData`, plus injection into the assets the clones are made from |
| Weight rule silently degrading if the game drops the laser or thunderstorm from a pool | Fall back to the rarest tier and log which pool |

## Release

This is the last feature before cutting **1.0.0**, so fold it into that release rather than another
test build:

1. Implement and test the above.
2. Set `<Version>` to `1.0.0` in `src/BallSwapItem/BallSwapItem.csproj`.
3. Retitle the top changelog section to `1.0.0` and drop the "Not published" line, per
   `deployment.md` step 2.
4. Work through `deployment.md` steps 1–5: GitHub repo, Thunderstore token, pre-flight, publish,
   tag `v1.0.0`.

Both machines need matching builds before the two-player checks — the pool contents and the
`itemOrder` layout must agree, and `BlockUnmodded` will not catch a version difference.
