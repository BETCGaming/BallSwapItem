# BallSwapItem — a Switcheroo item for Super Battle Golf

## Context

Super Battle Golf (Steam 4069520) is a Unity **Mono** game using **Mirror** networking with the host as
server, modded through **BepInEx 5**. An existing Thunderstore mod, `exiira/BallSwap` (MIT), swaps every
player's ball on a random timer — host-only, no player agency.

This project builds a **new, separate mod** published under the Thunderstore team **BETCGaming**, which
turns that idea into a pickup item: the **Switcheroo**. A player finds it, uses it, and after a short
wind-up every player's ball is randomly reassigned. Unlike BallSwap it is player-triggered, has a
telegraphed wind-up, and ships as a real item in the item pool with its own model and icon.

**Key finding that shaped the design:** inspecting `com.github.exiira.BallSwap.dll` and its source
(`github.com/Dualyy/ballswap`, MIT) shows the swap never moves a ball. It is two SyncVar assignments per
player — `PlayerGolfer.NetworkownBall` and `GolfBall.Networkowner` — plus a reflected call to the private
`GolfBall.UpdateNameTag`. No Rigidbody, transform, position or velocity is touched. We deliberately match
that mechanic: a ball in mid-flight is unaffected, it simply belongs to someone else now. This sidesteps
Mirror teleport signalling, snapshot interpolation, grounding state and out-of-bounds animation entirely.

## Decisions

| Area | Decision |
|---|---|
| Swap mechanic | Ownership swap (SyncVars), matching BallSwap. No physics involvement. |
| Eligibility | Include the user; derangement so nobody keeps their own; skip balls in the hole, mid out-of-bounds return, or not yet hit this hole. |
| Out of bounds | Untouched — vanilla penalty stroke + drop-on-head knockdown. |
| Use flow | Instant use (no aiming) with a 3-second configurable wind-up. |
| Telegraphing | On-screen countdown + existing FMOD cue during wind-up; `bomboclat.mp3` on all clients *after* the swap resolves. |
| Model | Clone of the Orbital Laser device, material recoloured **hot pink `#FF69B4`**. |
| Rarity | Rare, spawn weight configurable. |
| Unmodded players | Blocked from joining, with a clear message. |
| Package | Thunderstore `BETCGaming/BallSwapItem`; plugin GUID `com.github.BETCGaming.BallSwapItem`. |

## Prerequisites

1. **Install .NET SDK 10** — this machine has runtimes 6.0.36 and 8.0.30 but **no SDK**, and the official
   template requires SDK 10. `winget install Microsoft.DotNet.SDK.10`.
2. **Thunderstore publish token** — the `BETCGaming` team exists; generate a **service account API token**
   for it (thunderstore.io → Settings → Teams → BETCGaming → Service accounts). TCLI uses this to publish.
   It must stay out of the repo — supply it via environment variable at publish time.
3. **GitHub repo** — create `BETCGaming/BallSwapItem` to host the source and link it from the package.
4. **Original package icon** — Thunderstore requires exactly **256×256 PNG**. The file currently in the
   project folder is `exiira-BallSwap-0.1.3.png`, another author's published artwork; it must not ship.
   A new icon gets drawn in its visual spirit (golf ball ringed by recycle arrows).

## Architecture

### Project scaffold
```
dotnet new install SuperBattleGolfModding.BepInExTemplate
dotnet new sbgmod --output BallSwapItem \
  --guid com.github.BETCGaming.BallSwapItem \
  --ts-team BETCGaming
```
Produces `src/BallSwapItem/{Plugin.cs,BallSwapItem.csproj,thunderstore.toml}`, `Directory.Build.props`,
`Directory.Build.targets`, `Config.Build.user.props.template`. Copy the template to
`Config.Build.user.props` and point it at
`C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf`. Game code lives in
`GameAssembly.dll`; the build publicizes it (BallSwap's DLL carries `IgnoresAccessChecksToAttribute`,
confirming this is the supported path).

### Source layout (all under `src/BallSwapItem/`)

- **`Plugin.cs`** — BepInEx entry point, config binding, Harmony bootstrap. Config entries are picked up
  automatically by `AtomicStudio-ModConfig` (soft dependency) for in-game editing; no API needed.
- **`SwitcherooItem.cs`** — registers the custom item.
- **`SwitcherooNetwork.cs`** — custom Mirror messages and handlers.
- **`SwapService.cs`** — the server-side swap itself.
- **`SwitcherooUi.cs`** — countdown overlay and info-feed message.
- **`ModGate.cs`** — the unmodded-player join block.
- **`Assets/`** — `switcheroo_icon.png` (inventory icon, original art) and `bomboclat.mp3`, both embedded
  as assembly resources so the DLL is self-contained.

### 1. Item registration

`ItemCollection` holds `allItemData` and exposes `TryGetItemData` / `GetItemIcon` / `GetItemAtIndex`.
`ItemData` carries `Type`, `Prefab`, `Icon`, `AnimatorOverrideController`, `NonAimUse`, `MaxUses`,
`CanUsageAffectBalls` and friends.

Register a Switcheroo entry by cloning the Orbital Laser's `ItemData` at runtime and overriding:
`Type = (ItemType)200` (the enum runs `None`..`JumboBurger` = 0..17; 200 leaves ample headroom for
official additions), `NonAimUse = true`, `MaxUses = 1`, our icon, and a cloned prefab whose renderer
materials are tinted hot pink. Harmony-patch `ItemCollection` initialization to append the entry, and
patch `TryGetItemData` / `GetItemIcon` as a fallback so nothing returns the `UnknownItemIcon`.

### 2. Item pool injection

`ItemSpawnerSettings` owns `itemPools` / `runtimeItemPools` and `GetItemPoolsHash`; `ItemPool` has
`spawnChances`, `totalSpawnChanceWeight` and `itemPoolHash`; `MatchSetupRules.GetAllItemPoolsHash`
aggregates them. Append a spawn chance for our type into the **runtime** pools with a configurable weight
(default well below the average entry) and let `UpdateTotalWeight` recompute. Every player runs the same
injection, so the hashes agree — which is exactly why unmodded clients must be blocked (below).
Items reach players through `ItemSpawner`'s `ServerTryGiveItemToRandomPlayer`; no extra work needed there.

### 3. Use flow

`PlayerInventory.TryUseItem` dispatches through a compiler-generated `GetItemUseRoutine`, alongside
per-item coroutines like `ActivateOrbitalLaserRoutine`. Harmony-patch that dispatch so our `ItemType`
returns a `SwitcherooRoutine` which:

1. plays the equipment raise/flourish using the cloned animator override,
2. fires the existing `AudioSettings.OrbitalLaserAnticipationEvent` FMOD cue,
3. shows the countdown overlay for `WindUpSeconds` (default 3),
4. sends `SwitcherooRequest` to the host,
5. consumes the item through the normal inventory path.

### 4. Networking (no Mirror weaver)

`[Command]`/`[ClientRpc]` need Mirror's weaver, which a BepInEx mod cannot run. Use plain
`NetworkMessage` structs registered on both sides instead:

- `SwitcherooRequest` (client → server): nothing but a nonce.
- `SwitcherooResult` (server → all clients): the resolved `(playerNetId, ballNetId)` pairing plus the
  initiator, so clients play audio and info-feed text off the same data.

Register with `NetworkServer.RegisterHandler` / `NetworkClient.RegisterHandler` during connection setup.
Keep traffic sparse — `AntiCheat.dll` is a per-connection **rate limiter** (`AntiCheatRateChecker`,
`RegisterHit`, `minSuspiciousHitCount`), not an integrity checker, so modding is fine but spam is not.
Server-side, reject a request from a player who does not hold the item, and rate-limit per connection.

### 5. The swap (`SwapService`, server only)

Mirrors BallSwap's proven logic with the extra eligibility rules:

```
participants = CourseManager.MatchParticipants
  .Where(p => p != null && p.NetworkownBall != null
           && p.matchResolution is not (Scored or Eliminated)
           && !p.NetworkownBall.isInHole
           && p.NetworkownBall.OutOfBoundsReturnState is none
           && PlayerState.courseStrokes > 0)          // skip balls not yet hit this hole
if participants.Count < 2 -> abort, tell the initiator, refund the item
balls = derangement(participants.Select(p => p.NetworkownBall))
for i: participants[i].NetworkownBall = balls[i]; balls[i].Networkowner = participants[i]
       AccessTools.Method(typeof(GolfBall), "UpdateNameTag").Invoke(balls[i], null)
broadcast SwitcherooResult
```

`GolfBall`'s `owner` SyncVar has a hook (`OnOwnerChanged`) that already refreshes team colour, worldspace
icon and cosmetics, so visuals re-badge in place. `ServerLastStrokePosition` is deliberately left alone:
out-of-bounds return hangs the ball over the **owner's** head (`hasBallBeenMovedToHangOverHeadPosition`,
`GetDropOnHeadPosition`) rather than restoring a stored position — **verify this during implementation**
before relying on it.

### 6. Post-swap audio

On `SwitcherooResult`, every client plays `bomboclat.mp3` — extracted from assembly resources to the
plugin folder on first run, loaded via `UnityWebRequestMultimedia.GetAudioClip(..., AudioType.MPEG)`
(the file is ID3v2-tagged MP3; the decoder handles that, and no ffmpeg is installed for conversion),
played through a dedicated `AudioSource` with a config volume. The game mixes through FMOD, so this sits
outside its volume sliders — hence the separate config entry.

### 7. Unmodded-player gate (`ModGate.cs`)

On the host, expect a `SwitcherooHello` message from each connecting client within a few seconds; if it
does not arrive, `conn.Disconnect()` with a message naming the mod. Client-side, surface the reason.
Config-gated (`BlockUnmodded`, default true) so a host can opt out and run without the item injected.

## Build sequence

Built as a vertical slice, proving each layer in-game before the next is added:

1. Scaffold at the repo root (`superbattlegolfmod/` becomes `BETCGaming/BallSwapItem`), `git init`,
   first commit. Confirm `dotnet build -c Release` produces a Thunderstore package.
2. Bare plugin loads in-game — verified in `BepInEx/LogOutput.log`.
3. Item registers: correct icon in the slot, hot pink device in hand.
4. Swap fires from a debug hotkey (server path only, no item involved) — proves the SyncVar swap.
5. Then layer on: pool injection, custom network messages, wind-up + countdown UI, post-swap audio,
   and the unmodded-player join gate.

Icon art is generated as original candidates for review before anything ships.

## Files to create

All new. Critical ones: `src/BallSwapItem/Plugin.cs`, `SwitcherooItem.cs`, `SwitcherooNetwork.cs`,
`SwapService.cs`, `SwitcherooUi.cs`, `ModGate.cs`, `thunderstore.toml`, `icon.png`, `README.md`,
`CHANGELOG.md`, plus `Config.Build.user.props` (git-ignored, machine-local).

## Verification

1. `dotnet build -c Release -v d` → package lands in `artifacts/thunderstore/`.
2. Install the built package into an r2modman/Gale profile for Super Battle Golf alongside
   `BepInEx-BepInExPack-5.4.2305`. (r2modman is installed but currently has only a Valheim profile.)
3. **Single client:** enable a debug config option that grants the Switcheroo directly. Confirm the icon
   renders (not `UnknownItemIcon`), the device is hot pink in hand, the wind-up plays, and the countdown
   appears. With fewer than 2 eligible participants the swap must abort cleanly and refund.
4. **Two clients** (a second Steam account or a friend) — the real test:
   - balls visibly re-badge to new owners and **do not move**;
   - a swap fired while a ball is mid-flight leaves the trajectory untouched;
   - `bomboclat.mp3` plays for both players after the swap;
   - a ball that then goes out of bounds takes a vanilla penalty stroke and drops on its owner;
   - a player still on the tee this hole is excluded;
   - an unmodded client is refused with the expected message.
5. Check `BepInEx/LogOutput.log` for Harmony patch failures after any game update.

## Risks

- **Game updates break patches** — BallSwap 0.1.3 was itself a hotfix after the city update broke it.
  Keep patches narrow and fail loudly in the log.
- **`ItemType` collision** if the developers add items; 200 is chosen to avoid it, and the value is worth
  re-checking each game update.
- **Compiler-generated dispatch** (`<>c__DisplayClass152_0.<TryUseItem>g__GetItemUseRoutine|0`) is an
  unstable Harmony target; if it proves fragile, patch `PlayerInventory.TryUseItem` itself instead.
- **`bomboclat.mp3` is third-party audio** being publicly redistributed — worth confirming before publish.
