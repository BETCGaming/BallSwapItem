# Testing BallSwapItem with a second player

BallSwapItem is not published to Thunderstore yet, so the other player cannot install it the
normal way. Send them the built package and have them install it locally.

**Both machines must run the same build.** The swap needs the mod on the host *and* on every
client — an unmodded client gets disconnected on purpose (see [Troubleshooting](#troubleshooting)).

## What to send

```
artifacts/thunderstore/BETCGaming-BallSwapItem-1.0.0.zip
```

Rebuild it before sending if you have changed any code since:

```sh
dotnet build -c Release
```

The zip is a standard Thunderstore package — `manifest.json`, `icon.png`, and the plugin at
`plugins/com.github.BETCGaming.BallSwapItem.dll`.

## Option A — mod manager (preferred)

1. Install **Gale** or **r2modman**, choose *Super Battle Golf*, and create a profile.
   - If Super Battle Golf is missing from the game list, the manager is out of date. Update it,
     or use Gale, which picks up new communities faster.
2. In the **Online** tab, install **BepInExPack** (`BepInEx-BepInExPack-5.4.2305`).
   Importing a local mod does **not** pull dependencies, so this step is separate and required.
3. Import the zip:
   - r2modman: *Settings → Import local mod*
   - Gale: *Import → Local mod*
4. **Launch the game from the mod manager**, not from Steam. The manager is what injects the
   loader; starting from Steam runs the game unmodded.

## Option B — manual install

Fewer moving parts, and it is exactly how the host machine is set up.

1. Download **BepInExPack** from the
   [Super Battle Golf Thunderstore page](https://thunderstore.io/c/super-battle-golf/p/BepInEx/BepInExPack/).
2. Open that zip and copy everything *inside* its `BepInExPack/` folder into the game root
   (`.../steamapps/common/Super Battle Golf/`):
   - `winhttp.dll`
   - `doorstop_config.ini`
   - `.doorstop_version`
   - `BepInEx/`
3. From the BallSwapItem zip, copy `plugins/com.github.BETCGaming.BallSwapItem.dll` into
   `.../Super Battle Golf/BepInEx/plugins/`.
4. Launch normally from Steam.

To uninstall, delete those four items from the game root.

## Confirming the install

Open `.../Super Battle Golf/BepInEx/LogOutput.log` and look for:

```
[Info   :BallSwapItem] Plugin BallSwapItem v1.0.0 is loaded!
[Info   :BallSwapItem] Built Switcheroo item data as ItemType 200.
[Info   :BallSwapItem] Swap sound ready.
```

Once a lobby loads you should also see the pool injection lines, one per pool:

```
[Info   :BallSwapItem] Added the Switcheroo to pool 'Close item pool(Clone)' at weight 2.059.
```

## Optional: host testing hotkeys

Edit `BepInEx/config/com.github.BETCGaming.BallSwapItem.cfg` on the **host** and set:

```ini
DebugHotkeys = true
```

- **F9** — give yourself a Switcheroo
- **F10** — force a swap immediately, without using an item

Both are host-only and act on server state directly. Turn this off for normal play.

## Open, not yet confirmed

- [ ] **Held device orientation on the other player's screen.** It looked correct to the host but
      upside down to the other player, because the rotation was read from each client's own
      config and the two disagreed. Fixed in 0.1.7 by fixing the orientation in code, which also
      neutralises the stale value left in older config files. **Both players need 0.1.7 or later,
      and it has only been checked on one screen so far.**
- [ ] **The spent device being thrown at all.** Until 0.2.0 the throw hit a switch in the game
      that threw on our thrown-item type, which killed the use routine before it cleared the use
      state or removed the item — the repeating use-and-discard. Fixed in 0.2.0 and confirmed in
      the host's own log; **still unseen on the other player's screen**, colour included.

## What to check

The point of a second player is that most of this cannot be verified alone.

- [ ] The Switcheroo turns up from item boxes during normal play, without F9.
- [ ] The icon and the hot pink device look right in both players' hands.
- [ ] Using it shows the `SWITCHEROO IN 3` countdown **on both screens**, not just the user's.
- [ ] Both players hear the wind-up cue, then the payoff sound when the swap lands.
- [ ] After the swap, balls **do not move** — they stay put and re-badge to their new owners
      (name tags and team colours update in place).
- [ ] A swap fired while a ball is **in mid-flight** leaves its trajectory untouched; the ball
      finishes its arc and simply belongs to someone else.
- [ ] Nobody keeps their own ball.
- [ ] A player who has not yet teed off on the current hole is left out of the swap.
- [ ] A ball that goes out of bounds after a swap behaves exactly as in the base game: penalty
      stroke, ball returns over the owner's head and knocks them down.
- [ ] A player who has already holed out is left out of the swap.
- [ ] Using the item plays the press-the-button animation, then the player throws the spent
      device away, and it is hot pink on both screens.
- [ ] Using a Switcheroo while another player's swap is counting down is refused with
      "SWAP IN PROGRESS", and **the item is kept** rather than spent for nothing. Check the
      refused player's inventory, not just the message.
- [ ] The countdown names whoever used it — "USED BY <NAME>" — on **both** screens, including the
      player who did not press it.
- [ ] The game's info feed shows a line for the swap on both screens, with the Orbital Laser icon
      and the right two players.
- [ ] A refusal that happens during a countdown is readable: the red notice sits below the
      countdown rather than over it.
- [ ] The Switcheroo has its own slider in the match setup's item probabilities, in the
      ahead-of-ball tab and all four crate tabs, labelled and with its icon.
- [ ] It is greyed out at zero on the **mobility** tab, as any item absent from a pool is.
- [ ] Its default sits at the same place as the Orbital Laser's and the Thunderstorm's.
- [ ] The client sees the host's slider values, sees them change live, and cannot edit them.
- [ ] A host-set weight actually changes how often the item turns up in play, and zero stops it.
- [ ] **Reset spawn chances** returns it to its default rather than to zero.
- [ ] No `IndexOutOfRangeException` while the item-probability tab is open — that tab updates
      every frame, so the old crash was immediate.
- [ ] A row reading **One Switcheroo per hole** sits at the end of the **Battle** section, looking
      like the rows above it, and defaults to **Off**.
- [ ] Turning it on flips the preset to **Custom**; selecting Classic or Pro Golf puts it back Off.
- [ ] With it **on**, a second use in the same hole is refused with `ONCE PER ROUND`, the item is
      kept, and the allowance returns on the next hole.
- [ ] With it **off**, repeated swaps in one hole work.
- [ ] The client sees the row and the host's value, and sees it change live.
- [ ] Leaving and re-entering the lobby leaves exactly one row, showing the current value.

The host's `LogOutput.log` records each swap:

```
[Info   :BallSwapItem] Switcheroo swapped 2 balls.
```

or, when it declines:

```
[Info   :BallSwapItem] Switcheroo used but no swap ran: only 1 eligible ball(s) in play.
```

## Troubleshooting

**The other player gets disconnected a few seconds after joining.**
Expected when they do not have the mod. The host log shows:

```
[Warning:BallSwapItem] Disconnecting connection 1: BallSwapItem is not installed.
```

If they *do* have it installed, they are either running a different build or launching the game
without the loader (starting from Steam while the mod lives in a mod-manager profile). Check
their `LogOutput.log` for the plugin load line first.

To let unmodded players in anyway, set `BlockUnmodded = false` in the host's config. They can
then play, but the host may hand them a Switcheroo they cannot resolve, so this is only for
diagnosing the gate itself.

**Nothing happens when the item is used.**
Check the host log. `no Switcheroo in their inventory` means the server rejected the request;
`No writer found for ...` means an old build is installed and needs replacing.

**The game starts unmodded.**
`winhttp.dll` is missing from the game root, or the game was launched from Steam while the mod
is installed in a mod-manager profile.
