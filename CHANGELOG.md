# Changelog

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
