# One swap per hole, as a Battle rule

Move the `OneSwitchPerRound` config key into the match setup's **Battle** section as an on/off
option, alongside Homing shots, Knockout speed boost, Hit other players and Hit other players'
balls.

## Context

The limit already exists and works: `SwitcherooNetwork` locks the swap for the rest of the hole
when `Plugin.OneSwitchPerRound` is on, tells clients through `SwitcherooLockMessage`, and clears it
on `CourseManager.CurrentHoleGlobalIndexChanged`. What it lacks is a home in the UI — it is a
host-only config key, so nobody in the lobby can see or change it.

This is the same move 0.3.0 made for the spawn rate, and the surface is friendlier.

## Decisions

| Question | Decision |
|---|---|
| What the limit means | **Per hole**, exactly as it works today. No gameplay change |
| Default | **Off** — unlimited swaps stay the out-of-the-box behaviour |
| `OneSwitchPerRound` config key | **Removed.** The Battle row is the only control |
| Presets | Selecting Classic or Pro Golf **resets it to Off**, as with any rule a preset does not list |

## Why this is easier than the item probabilities

The item sliders needed `itemOrderLookup` rebuilt because it is an array indexed by item type. The
rules machinery has no equivalent:

- Every rule lookup is a `Dictionary<Rule, …>` — `onOffDropdownLookup`, `sliderLookup`,
  `dropdownLookup`, `toggleLookup`. **No array is indexed by `Rule`.**
- `GetDefaultValue` ends in `default: return 0f;`, so an unknown rule already has a defined default
  of zero. Our chosen default is Off, which means **no patch at all** for the default.
- `ResetRules` and `GetFormattedValue` iterate those dictionaries, so our row joins in for free.
- `UpdateRule`'s switch has no default arm, and `UpdateInteractableOptions` and `UpdateTooltips`
  name the game's own controls explicitly — none of them can trip over an extra rule.
- `ShouldSetCustomPreset` returns true for any rule on the server, so our row flips the preset to
  **Custom** like every other.

Values ride in `SyncDictionary<Rule, float> rules`, already host-authoritative and synced, and
`MatchSetupMenu.ServerValues.ruleKeys` is a `List<Rule>`, so a custom key round-trips. Unlike the
spawn chances there is **no hash check** on rules, so nothing gets invalidated or reset on install.

The work is therefore almost entirely UI.

## The UI is authored, not generated

The item sliders were instantiated in a loop, which is why appending to `itemOrder` was enough.
Rule rows are not — they are serialized references to objects placed in the scene:

```csharp
[Header("Battle")]
public DropdownOption homingShots;
public DropdownOption knockoutSpeedBoost;
public DropdownOption hitOtherPlayers;
public DropdownOption hitOtherPlayersBalls;
```

So the row has to be cloned from one of them, inserted as its sibling, and relabelled.

## Implementation

### 1. The rule value

`Switcheroo.cs` gains, beside the existing type constants:

```csharp
/// <summary>Clear of the game's own Rule range (Countdown..SmallTeamBoost, 0..30).</summary>
public const MatchSetupRules.Rule OncePerHoleRule = (MatchSetupRules.Rule)200;
```

Register its category so anything that groups rules by section sees ours:

```csharp
MatchSetupRules.CategoryPerRule[OncePerHoleRule] = MatchSetupRules.RuleCategory.Battle;
```

`CategoryPerRule` is a public static dictionary. `MatchSetupRules` itself never reads it, so the
consumer is elsewhere in the UI — **confirm what reads it during implementation** and check our row
appears correctly there too.

### 2. New `SwitcherooRule.cs` — build the row

A **postfix** on `MatchSetupRules.Initialize` — not a prefix, unlike the item work, because the
game's own `InitDropdownOnOff` calls happen inside `Initialize` and ours must follow them.

1. Bail if the row already exists — `Initialize` runs every time the lobby is entered.
2. `Object.Instantiate(rules.hitOtherPlayersBalls.gameObject)`, parent it to that row's parent, and
   `SetSiblingIndex(hitOtherPlayersBalls.transform.GetSiblingIndex() + 1)` so it lands at the end of
   the Battle group.
3. Point the clone's `LocalizeStringEvent` at our own string-table entry (below).
4. Call the game's own `InitDropdownOnOff(clone, OncePerHoleRule)` through
   `AccessTools.Method` — it is private, and the precedent is `EquipmentSwitcherPatch`'s use of
   `CanHoldEquipment`. Going through the game's own method is what registers the row in
   `onOffDropdownLookup`, wires the value change, the Custom-preset flip and the greying, and gets
   it included in `ResetRules`.
5. Wrap the whole thing in `try`/`catch`; on failure log at error level and leave the screen alone.

Cloning an on/off row means the **On/Off dropdown options stay localized** by the clone's own
`LocalizeDropdown`; only the row's label needs changing.

### 3. The label

Same mechanism as the item name in `SwitcherooLocalization`: add a runtime entry to the game's
string table and point the row's `LocalizeStringEvent` at that key.

- Proposed key: `RULE_SWITCHEROO_ONCE_PER_HOLE`, text **"One Switcheroo per hole"**.
- **Confirm which table the rule labels use** — the item name lives in `StringTable.Data`, and the
  rules may use another. Read the key off an existing row's `LocalizeStringEvent` and use the same
  table.
- Re-add on language change, exactly as `SwitcherooLocalization.EnsureApplied` already does.

A tooltip is optional: `UpdateTooltips` registers the game's own rows by `RectTransform` and will
simply not know about ours. If one is wanted, register it there through the same `tooltip` field.

### 4. Read the rule instead of the config

`SwitcherooNetwork.cs:379` becomes:

```csharp
if (MatchSetupRules.GetValueAsBool(Switcheroo.OncePerHoleRule))
```

`GetValueAsBool` is public and static, and falls back to `GetDefaultValue` when there is no
`MatchSetupRules` instance — so it is safe outside a lobby and returns Off, which is the default we
want anyway.

Nothing else changes. The lock, the `SwitcherooLockMessage` broadcast, the per-hole reset in
`OnHoleChanged` and the client-side `LockedThisRound` refusal all stay exactly as they are — this
feature only changes where the host's answer comes from.

### 5. Remove the config key

- Delete the `Plugin.OneSwitchPerRound` binding.
- Update the README: the rule is set in the match setup's Battle section, not in config.
- Remove the `OneSwitchPerRound` row from `deployment.md`'s local test settings table, and note the
  removal there as was done for `SpawnChance`.
- Changelog: an existing key left in a config file is ignored, so nothing to clean up.

### 6. Fallback

Simpler than the pool fallback, because nothing breaks if the row cannot be built: the rule still
resolves through `GetValueAsBool` to its default of Off, so the limit is simply never on and the
Switcheroo behaves as it does today with the setting off.

So: log once at error level naming what failed, and carry on. No gameplay path to restore, no
groundwork to undo.

## Verification

Single player:

- [ ] A row reading **One Switcheroo per hole** appears at the end of the **Battle** section, with
      the same look as the rows above it and a working On/Off dropdown.
- [ ] It defaults to **Off**.
- [ ] Turning it on flips the preset chip to **Custom**, as any rule does.
- [ ] Selecting **Classic** or **Pro Golf** puts it back to Off — the agreed behaviour for a rule a
      preset does not list.
- [ ] Entering and leaving the lobby repeatedly leaves exactly one row, not one per visit.
- [ ] With it **on**: the second Switcheroo use in a hole is refused with `ONCE PER ROUND`, the item
      is kept, and the allowance returns on the next hole.
- [ ] With it **off**: repeated swaps in one hole work, as they do today.

Two players:

- [ ] The client sees the row, sees the host's value, sees it change live, and cannot edit it.
- [ ] The host's setting governs the client's refusal — the client refuses locally on
      `LockedThisRound`, which the host drives.
- [ ] A client joining mid-setup sees the right value rather than the default.

## Risks

| Risk | Mitigation |
|---|---|
| Rule value 200 colliding with a future game rule | Same accepted caveat as the other 200s; re-check after each game patch |
| The cloned row's label or layout not matching the authored ones | Clone a real Battle row rather than building one; verify against its neighbours on screen |
| A game update renaming or reshaping `InitDropdownOnOff` | Located via `AccessTools`; failure is caught and logged, and the rule falls back to Off |
| `CategoryPerRule`'s unknown consumer mishandling a custom rule | Confirm the consumer during implementation and check our row there |
| Label table guessed wrong, showing a raw key | Read the table off an existing row rather than assuming `StringTable.Data` |

## Release

Ship as **0.4.0**, a test build, so it can be reviewed with the audience alongside 0.3.0's item
probabilities before cutting **1.0.0**:

1. Implement and work through the verification above.
2. Set `<Version>` to `0.4.0`; add its changelog section.
3. Review both features with the audience.
4. Cut `1.0.0` per `deployment.md`: retitle the changelog, drop the "Not published" line, then
   GitHub, Thunderstore token, pre-flight, publish, tag.

Both machines need matching builds for the two-player checks.
