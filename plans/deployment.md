# Deploying BallSwapItem

How this got from a local test build onto Thunderstore, kept as the procedure for the next
release rather than a to-do list.

Current state: **published as 1.0.1** on 2026-09-06 and tagged `v1.0.1`; `main` is pushed. See
[Publishing an update later](#publishing-an-update-later) for the next one.

Thunderstore categories live in `[publish.categories]` in `src/BallSwapItem/thunderstore.toml` and
are applied at publish time. They are a property of the listing rather than a version, so they can
also be changed on the package page without burning a version number. Slugs for this community are
listed at <https://thunderstore.io/api/experimental/community/super-battle-golf/category/>.

## Before publishing

Publishing a version number is permanent — Thunderstore does not allow re-uploading over one.
Get these settled first.

### 1. Finish the open testing

`testing-with-friend.md` has an **Open, not yet confirmed** section and a two-player checklist.
The items that need a second player are the ones most likely to be broken, because several bugs
so far only appeared on the *other* player's screen:

- Held device orientation on the second player's screen.
- The discarded device being hot pink for both players, not just the thrower.
- The lobby's item-probability tab opening cleanly.
- Whether the repeating use-and-discard recurs. Diagnosed and fixed in 0.2.0 — the throw hit a
  switch in the game that threw on our type and killed the use routine before it could clear the
  use state — but the fix has not been confirmed in play yet.

### 2. Decide the version number

Settled: shipped as `1.0.0`. For the next release, set `<Version>` in
`src/BallSwapItem/BallSwapItem.csproj` and retitle the top changelog section to match.

### 3. Settle the accepted caveats

These are known and currently accepted. Worth one last look, because they are harder to change
after people have installed the mod:

- **`bomboclat.mp3` is third-party audio** being publicly redistributed. You chose to ship it.
- **Custom enum values are 200** for `ItemType`, `EquipmentType` and `ThrownUsedItemType`, plus a
  fixed network asset id. A game update adding many items could theoretically collide; worth
  re-checking after each game patch.
- **A kicked unmodded player sees a generic disconnect**, not a message naming the mod. Mirror
  gives no way to send a reason to a client that cannot read our messages.

## Step 1 — GitHub

The remote is already configured; the repository itself does not exist yet.

1. Create **`BETCGaming/BallSwapItem`** on github.com — empty, no README, no licence, no
   `.gitignore` (the repo already has all three, and an initial commit would need merging).
2. Add `maxthegrim13@gmail.com` to that account under **Settings → Emails** and verify it, so the
   commits link to the account rather than showing as an unlinked author.
3. Push:

```sh
git push -u origin main
```

`src/BallSwapItem/thunderstore.toml` already points `websiteUrl` at this repository, so the link
on the package page will 404 until this exists.

## Step 2 — Thunderstore token

The `BETCGaming` team exists. Generate a token for it:

thunderstore.io → **Settings → Teams → BETCGaming → Service accounts** → create one → copy the
token it shows you once.

Keep it out of the repository and out of `thunderstore.toml`. Put it in an environment variable
for the session instead:

```powershell
$env:TCLI_TOKEN = "<paste the token>"
```

## Step 3 — Pre-flight checks

```powershell
dotnet build -c Release
```

That writes `artifacts/thunderstore/BETCGaming-BallSwapItem-<version>.zip`. Confirm before
publishing:

- [ ] `manifest.json` has the right `version_number`, `namespace` `BETCGaming`, and the
      `BepInEx-BepInExPack-5.4.2305` dependency.
- [ ] `icon.png` is exactly **256×256**.
- [ ] The zip contains `plugins/com.github.BETCGaming.BallSwapItem.dll`, `README.md`,
      `CHANGELOG.md` and `LICENSE`.
- [ ] The DLL is the **Release** build, not a Debug one left over from a test run. Close the game
      before building, or the deploy step silently keeps the old file in place.

```powershell
# Inspect what is actually in the package
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "artifacts\thunderstore\BETCGaming-BallSwapItem-1.0.0.zip"))
$zip.Entries | Select-Object FullName, Length
$zip.Dispose()
```

**The shipped defaults are already correct** and are not affected by your local testing settings:
`BlockUnmodded` true, `DebugHotkeys` false,
`VerboseLogging` false. The config file lives in the game folder, is generated at runtime, and is
not part of the repository or the package — so no reset is needed for the release. See
[Local test settings](#local-test-settings) for your own machine.

## Step 4 — Publish

Publish the package that was just built and verified, rather than rebuilding as part of
publishing:

```powershell
dotnet tcli publish `
  --config-path src\BallSwapItem\thunderstore.toml `
  --file artifacts\thunderstore\BETCGaming-BallSwapItem-1.0.0.zip `
  --token $token
```

`--config-path` is required: `thunderstore.toml` lives in `src/BallSwapItem/`, while tcli looks for
it in the working directory and exits if it is not there. The token is read from `.env`, which is
gitignored and must stay that way.

The template also wires publishing into the build via
`dotnet build -c Release -property:PublishTS=true`, but that path does not pass a token, so the
explicit command above is the reliable one.

## Step 5 — After publishing

- [ ] Open the package page and check the icon, description, README rendering and the GitHub link.
- [ ] Install it through **Gale** or **r2modman** from Thunderstore on a clean profile and confirm
      it loads — this is the first time the real install path gets exercised.
- [ ] Tag the release so the source matches what shipped:

```sh
git tag -a v1.0.0 -m "First Thunderstore release"
git push origin v1.0.0
```

- [ ] Tell the people who have been testing with a local zip to switch to the Thunderstore
      version, so they stop running a hand-copied DLL.

## Publishing an update later

1. Bump `<Version>` in `src/BallSwapItem/BallSwapItem.csproj`.
2. Add a changelog section for it.
3. `dotnet build -c Release`, then the `tcli publish` command with the new filename.
4. Commit and tag.

Version numbers cannot be reused, so a mistake means burning a number and publishing the next one.

## Local test settings

Your machine's config still has testing values, in
`C:\Program Files (x86)\Steam\steamapps\common\Super Battle Golf\BepInEx\config\com.github.BETCGaming.BallSwapItem.cfg`:

| Key | Testing | Normal play |
|---|---|---|
| `DebugHotkeys` | true | false |
| `VerboseLogging` | true | false |

`SpawnChance` was removed in 0.3.0 and `OneSwitchPerRound` in 1.0.0 — spawn rate is now the match
setup slider, and the once-per-hole limit a Battle row. Old keys left in the file are ignored, so
there is nothing to clean up.

These affect only this machine. Leave `VerboseLogging` on until the repeating use-and-discard has
clearly stopped happening — it only writes while the item is being used, and it is the only thing
that will identify the step it stalls at if it recurs.
