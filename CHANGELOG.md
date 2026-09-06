# Changelog

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
