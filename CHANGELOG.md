# Changelog

## 1.0.0

First public release. The sections below it are the pre-release test builds it was built from.

### Added

- The once-per-hole limit is now a **One Switcheroo per hole** row in the match setup's Battle
  section, set by the host per match and visible to everyone in the lobby. It is off by default,
  flips the preset to Custom like any rule, and the game's presets reset it to off because they do
  not list it.

### Changed

- **Removed the `OneSwitchPerRound` config key.** The Battle row replaces it. A leftover key in an
  existing config file is ignored.

## 0.3.0

Not published; a test build.

### Added

- The Switcheroo has its own item-probability slider in the match setup, alongside the game's own
  items, and that slider is now what decides how often it spawns. It appears in the ahead-of-ball
  pool and the four crate pools, and ships at the same rarity as the Orbital Laser and the
  Thunderstorm — read from each pool at runtime, so a rebalance in a game update carries the
  Switcheroo with it. Mobility pools are left alone, so the slider is greyed out on that tab the
  way any item missing from a pool is.

### Changed

- The item is now in the game's item pools and drawn by the game's own weighted pick, rather than
  being substituted in as items were drawn.
- **Removed the `SpawnChance` config key.** The match setup slider replaces it. A leftover key in
  an existing config file is ignored.
- Installing the mod resets a saved match setup's spawn chances once, because the item pools it
  was saved against no longer match. Removing the mod later resets them once more.

### Fixed

- The lobby's rules screen can hold a modded item at all. It maps each item to its slider through
  an array the game fills in an editor-only callback, sized to its own item list, which is what
  threw on the Switcheroo and forced the item out of the pools in 0.1.7.

## 0.2.1

Not published; a test build.

### Added

- Everyone can now see who used the Switcheroo. The countdown names them on every player's screen,
  and the swap posts a line per affected player in the game's own info feed.
- A swap already counting down now blocks any further Switcheroo use until it lands. The press is
  refused with flashing "SWAP IN PROGRESS" text and the denial sound, and the item is kept.

### Fixed

- A Switcheroo used during another player's countdown is no longer consumed for nothing. The item
  is now spent only once the host has accepted the use, and every refusal the host makes is sent
  back to that player and shown on screen, rather than the host dropping the request in silence.
- The host settles whether enough balls are in play before accepting a use rather than after, so
  an accepted use can no longer turn out to have nothing to swap.
- The Switcheroo has an info feed icon, so the game will post its feed line at all. Without one
  InfoFeed refused the line and logged an error on every single swap, which is why no one could
  see who had used it.
- The refusal notice steps below the countdown while one is running, rather than being drawn on
  top of it.

## 0.2.0

Not published; a test build.

### Fixed

- Using the Switcheroo no longer aborts partway through. The game's ThrowUsedItemInternal picks a
  throw's hand, rotation, spin and speed from a switch whose default arm throws, so our thrown
  type never reached the prefab lookup it was registered in. The exception killed the use routine
  before it could clear the use state or remove the spent item, which left the player mid-use
  holding a Switcheroo they could fire again — the repeating use-and-discard. Our own type is now
  handled with the Orbital Laser's throw settings, on every client rather than just the thrower.
- A throw that fails for any other reason no longer strands the player: the toss is cosmetic and
  the swap is already committed by then, so the routine finishes and cleans up regardless.

## 0.1.9

Not published; a test build.

### Added

- The Switcheroo refuses to fire unless at least two balls are actually in play. The player keeps
  the item and gets the refusal sound with flashing "NO BALLS TO SWAP" text. This is what made
  the item usable in the lobby's driving range, where the game registers no match participants at
  all and there was never anything to swap.
- The refusal notice now carries its own text, so it can explain which rule stopped the use.
- VerboseLogging, which traces each step of a use and a swap. For diagnosing a stuck or repeating
  use; off by default.

### Changed

- The server checks that a swap is possible before starting the countdown, rather than after, so
  a countdown never plays for a swap that cannot happen.

## 0.1.8

Not published; a test build.

### Added

- The player now throws the spent device away after pressing its button, using the Orbital
  Laser's own throw timing and total activation length so the toss lands on the animation frame
  it was authored for.
- The discarded device is hot pink like the one that was held. The game throws a separate
  short-lived model keyed by its own type, so a custom one is registered rather than reusing the
  grey Orbital Laser.

## 0.1.7

Not published; a test build.

### Fixed

- The device is no longer upside down for other players. Its rotation was read from each
  client's own config, so two players applied different rotations to the same held object. The
  orientation is now fixed in code, which also means a stale value in an existing config file
  can no longer flip it.
- The lobby's rules screen no longer throws. The Switcheroo is no longer added to the item
  pools at all: that array is read once to pick an item and seven times by the rules screen,
  several of which index an array sized to the game's own item list. One of those runs every
  frame while the item-probability tab is open. The single draw point is intercepted instead,
  leaving the pools exactly as the game ships them.

### Changed

- SpawnRarity and SpawnChanceOverride are replaced by a single SpawnChance, the probability that
  any item handed out is a Switcheroo.
- HeldPitchDegrees, HeldYawDegrees and HeldRollDegrees are removed, since a per-client setting
  cannot drive something every player has to see the same way.

## 0.1.6

Not published; a test build.

### Fixed

- The device is visible in the player's hand again. Moving the model under a wrapper object in
  0.1.4 left the model itself deactivated: EquipmentManager activates the object it instantiates,
  which is now the wrapper, so the model underneath stayed hidden.
- The lobby's rules screen no longer throws. It maps an item to its slider by indexing an array
  sized to the game's own item list, so a modded item indexed past the end and aborted
  MatchSetupRules.Initialize partway through, before it pushed the rules out to clients. The
  Switcheroo is now skipped by that notification; it has no slider, and its spawn weight lives
  in the item pool the spawner actually draws from.

## 0.1.5

Not published; a test build.

### Added

- OneSwitchPerRound: allows a single swap per hole. A player using a Switcheroo after that keeps
  the item and gets a quiet refusal sound with flashing "ONCE PER ROUND" text. The rule is
  enforced by the host and clients follow it, so a mismatched client setting cannot bypass it.
- The swap is announced in the game's own info feed, one line per player caught in it.
- DenialVolume, for the refusal sound.

## 0.1.4

Not published; a test build.

### Added

- The Orbital Laser's hold and button-press animations. The animator picks animations from an
  "Equipped item" integer set to the ItemType, and ours matched no state, so the device had no
  use animation and sat in the default pose.
- HeldPitchDegrees and HeldRollDegrees alongside HeldYawDegrees, for trimming how the device
  sits in the hand. All default to none now that the animator supplies the pose.

### Fixed

- The item is named "Switcheroo" instead of showing "Data/ITEM_200". Names are looked up in the
  Data string table under ITEM_<type>, which had no entry for a modded item, so Unity
  Localization printed the table and key instead. The entry is added at runtime, which fixes
  every place the name appears rather than one piece of UI at a time.

## 0.1.3

Not published; a test build.

### Added

- SpawnChanceOverride, forcing the Switcheroo to a set share of every item pool. For testing;
  leave at 0 for normal play.
- HeldYawDegrees, rotating the device in the player's hands. Defaults to 180 so the antenna
  points away from the player.

### Fixed

- Dropped Switcheroos are visible to other players. Mirror clears registered prefabs when a
  client shuts down, so the registration made at startup was gone by the time anyone joined a
  lobby, and their client had no prefab to build. It is now renewed each time the client starts.
- Picking a dropped Switcheroo back up returns a Switcheroo. The pickup carried the Orbital
  Laser's serialized item type, so it turned back into a laser in the player's hands.

## 0.1.2

Not published; a test build.

### Added

- A hot pink device in the player's hands. Held models come from EquipmentType rather than
  ItemData.Prefab, which the item had no entry for, so the hands were empty.

### Changed

- The countdown now uses the game's own font with a white to #FFFCD4 vertical gradient and a
  black outline, drawn with TextMeshPro instead of IMGUI.
- Package and inventory art switched to the glowing icon.

### Fixed

- Dropped Switcheroos appear on the ground. The pickup template was deactivated and Instantiate
  copies activeSelf, so every dropped copy spawned invisible.
- Dropped Switcheroos look the same to everyone. The pickup inherited the Orbital Laser's network
  asset id, so other players' clients would have built the game's grey laser instead.

## 0.1.1

Not published; a test build, versioned so host and client can be told apart at a glance.

### Fixed

- Registered Mirror read/write functions for the mod's network messages by hand. Mirror's weaver
  generates these for NetworkMessage structs, not only for commands and RPCs, so without them
  every send failed and the item never reached the host.
- Accepted a player's own use of the item on a host-run lobby. Mirror delivers local-connection
  messages on a later network update, by which point the item had already been consumed, so the
  host's request was rejected as if no Switcheroo were held.
- Stopped the mod gate disconnecting players who have the mod. The announcement message was sent
  before the game's authenticator finished, and Mirror drops any connection that sends an
  auth-required message too early — which only modded clients ever did.

## 0.1.0

### Added

- The Switcheroo item: a hot pink device built from the Orbital Laser's model that randomly
  reassigns every eligible player's ball after a short wind-up.
- Ownership-based swapping, so balls in mid-flight keep their trajectory.
- On-screen countdown between using the item and the swap landing.
- Sound sting played on every client once a swap resolves.
- Injection into every item pool at a weight relative to that pool's average.
- An opt-out gate that keeps unmodded players out of a lobby.
- Config for the wind-up, spawn rarity, sound volume, and host-only testing hotkeys.
