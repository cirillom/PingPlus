# Ping Plus

Networked ping improvements for Risk of Rain 2.

![Pinned pings and item ownership in game](https://raw.githubusercontent.com/cirillom/PingPlus/refs/heads/main/example.png)

## Item ownership

- Pinging an item or equipment broadcasts one `Owned by:` chat line to the whole party
- Counts are calculated by the host from authoritative player inventories
- Only players with at least one copy are listed; zero counts are omitted
- Item stacks use their real count and equipment uses `×1`
- Ordinary world, object, and enemy pings remain unchanged

## Loot colors

- Item and chest pings use Risk of Rain 2's tier colors: white, green, red, boss yellow, equipment orange, lunar blue, and void purple
- Normal, large, legendary, equipment, lunar, and void chests use the matching loot color
- Scrap items keep their tier color, and both scrap pickups and Scrappers use a dedicated scrap ping icon
- Enemy attack pings keep their vanilla red appearance

## Ping distance

- Normal and pinned pings show the local player's distance in meters
- The label updates only when the rounded distance changes

## Pinned pings

- Press `G` while aiming to create a pinned ping visible to the whole party
- Only objects and enemies can be pinned; aiming at empty terrain does nothing
- Press `G` on the same object again to unpin it
- Press `P` to remove only the pinned pings you created
- The host assigns each player one stable block of five names from a 20-letter Greek sequence:
  - Block 1: `ALPHA`, `BETA`, `GAMMA`, `DELTA`, `EPSILON`
  - Block 2: `ZETA`, `ETA`, `THETA`, `IOTA`, `KAPPA`
  - Block 3: `LAMBDA`, `MU`, `NU`, `XI`, `OMICRON`
  - Block 4: `PI`, `RHO`, `SIGMA`, `TAU`, `UPSILON`
- Loot colors and the scrap icon apply to normal and pinned pings
- Each player can keep at most five pinned pings; creating another removes only their oldest
- Pinned pings are cleared between stages

## Configuration

- `PinnedPingDuration` defaults to `0` seconds; `0` means until removed or the stage ends
- `MaxPinnedPings` defaults to `5` and accepts values from `1` to `5`
- `PinKey` defaults to `G`
- `ClearKey` defaults to `P`
- `ShowDistance` defaults to `true`

## Multiplayer

The host owns pinned-ping allocation and cleanup, and players joining during a stage receive the existing pinned pings. The host and every player must install the same Ping Plus version. Do not enable Better Item Ping or Freeze Ping alongside Ping Plus, or their behavior will run twice.

## Installation

Install Ping Plus from Thunderstore using **Download with dependencies**.

When testing by copying only `PingPlus.dll`, first install **R2API Networking** in that r2modman profile. Copying a DLL does not process `manifest.json`, so r2modman cannot install its dependencies automatically.

After Ping Plus loads once, successfully, edit its settings from r2modman's **Config editor** or open:

`BepInEx/config/com.cirillom.pingplus.cfg`
