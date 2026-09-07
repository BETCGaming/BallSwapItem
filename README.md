# BallSwapItem

Adds the **Switcheroo** to Super Battle Golf: a pickup item that randomly reassigns every
player's ball mid-hole.

Find one, use it, and after a short wind-up everybody is playing someone else's ball. Nobody
keeps their own.

## How the swap works

The Switcheroo does **not** move any balls. It reassigns who owns which ball, so a ball caught
in mid-flight keeps its exact trajectory — it simply belongs to someone else when it lands.
Name tags, team colours and ball cosmetics update in place.

Players are left out of a swap when they:

- have already holed out or been eliminated
- have a ball in the middle of an out-of-bounds return
- have not yet teed off on the current hole

A swap needs at least two eligible players. Out-of-bounds after a swap behaves exactly as the
base game does: a penalty stroke, and the ball returns over your head.

## Requirements

**Every player in the lobby needs this mod.** The Switcheroo is added to the item pool, which
changes data the host and clients agree on, so an unmodded player cannot take part.

## Configuration

| Key             | Default | Description                                                        |
| --------------- | ------- | ------------------------------------------------------------------ |
| `Enabled`       | `true`  | Registers the item. With this off the Switcheroo never appears.    |
| `WindUpSeconds` | `3`     | Delay between using the item and the swap landing.                 |
| `SoundVolume`   | `0.8`   | Volume of the sting played once a swap resolves.                   |
| `DebugHotkeys`  | `false` | Host-only testing keys: F9 grants a Switcheroo, F10 forces a swap. |

Whether a hole allows more than one swap is **not** a config setting either. It is an on/off row
called **One Switcheroo per hole** in the match setup's Battle section, off by default, set by the
host per match.

How often the Switcheroo spawns is **not** a config setting. It has its own slider in the match
setup's item probabilities, alongside the game's own items, and the host sets it per match. It
ships at the same rarity as the Orbital Laser and the Thunderstorm, and appears in every pool
except mobility.

[ModConfig](https://thunderstore.io/c/super-battle-golf/p/AtomicStudio/ModConfig/) is supported
for editing these in-game; no extra setup is needed.

## Building

Requires the .NET SDK 10 and a local copy of the game.

```sh
dotnet build -c Release -v d
```

The Thunderstore package is written to `artifacts/thunderstore/`. Copy
`Config.Build.user.props.template` to `Config.Build.user.props` first if the game is not at the
default Steam path; with it in place, builds deploy straight into the game's `BepInEx/plugins/`.

## Credits

Not affiliated with [BallSwap](https://thunderstore.io/c/super-battle-golf/p/exiira/BallSwap/)
by exiira, which swaps balls on a timer rather than through an item. The BallSwap mod did inspire us greatly to make this mod.
