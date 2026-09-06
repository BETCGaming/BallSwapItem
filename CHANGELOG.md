# Changelog

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
