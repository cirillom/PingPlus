# Ping Plus

Networked ping improvements for Risk of Rain 2.

## Item ownership

- Pinging an item or equipment broadcasts one `Owned by:` chat line to the whole party
- Counts are calculated by the host from authoritative player inventories
- Only players with at least one copy are listed; zero counts are omitted
- Item stacks use their real count and equipment uses `×1`
- Ordinary world, object, and enemy pings remain unchanged

## Pinned pings

- Press `G` while aiming to create a pinned ping visible to the whole party
- Only objects and enemies can be pinned; aiming at empty terrain does nothing
- Press `G` on the same object again to unpin it
- Normal pings remain completely vanilla
- Each player's oldest pinned ping is removed when their configured cap is reached
- Pinned pings are cleared between stages

## Configuration

- `PinnedPingDuration` defaults to `0` seconds; `0` means until removed or the stage ends
- `MaxPinnedPings` defaults to `5`
- `PinKey` defaults to `G`

## Multiplayer

The host and every player must install the same Ping Plus version. Do not enable Better Item Ping or Freeze Ping alongside Ping Plus, or their behavior will run twice.

## Installation

Install Ping Plus from Thunderstore using **Download with dependencies**.

When testing by copying only `PingPlus.dll`, first install **R2API Networking** in that r2modman profile. Copying a DLL does not process `manifest.json`, so r2modman cannot install its dependencies automatically.

After Ping Plus loads once, successfully, edit its settings from r2modman's **Config editor** or open:

`BepInEx/config/com.cirillom.pingplus.cfg`
