# CMZDedicatedSteamServer

> Host a **Steam-native CastleMiner Z dedicated server** outside the normal game window, advertise it through the Steam browser flow, and run it under a real logged-in Steam account.

**CMZDedicatedSteamServer** is the Steam transport companion project for CastleForge. It is built in **C# / .NET Framework 4.8.1**, loads the original CastleMiner Z runtime through **reflection**, initializes the normal **Steam client API path**, creates a Steam-hosted session/lobby, and runs dedicated host logic without opening the playable game window.

> **Transport note:** **CMZDedicatedSteamServer** is the **Steam-native** dedicated-server transport for CastleForge. It is separate from **CMZDedicatedLidgrenServer**, which is the **Lidgren / direct-IP** transport for manual IP-based workflows.

> **Current status:** this project is intended for the CastleForge Steam hosting workflow and is still evolving. Treat it as an advanced / in-progress dedicated-server option rather than a drop-in anonymous Steam game-server replacement.

![Preview](_Images/Preview.png)

---

## Why use CMZDedicatedSteamServer?

CastleMiner Z's normal Steam multiplayer flow was designed around the live game host, not a standalone server executable. **CMZDedicatedSteamServer** moves that hosting flow into a dedicated process so you can bring up a Steam-visible host without leaving the normal game window running.

That gives you a cleaner setup for:
- Steam-browser visibility
- dedicated testing and development
- hosting under a separate Steam account
- server-side world and inventory persistence
- pairing with **DirectConnect** for quick frontend launch convenience

### Highlights
- **Dedicated executable host** for the Steam transport path.
- Uses the **normal Steam client API path** under a real logged-in Steam account.
- Creates a **Steam-visible lobby/session** for CastleMiner Z-compatible clients.
- Loads the original game/runtime assemblies through **reflection**.
- Supports a configurable **`game-path`** instead of forcing a hardcoded install path.
- Loads Harmony from a local **`Libs`** folder.
- Uses packaged **server.properties**, world, and inventory layout similar to the Lidgren server package.
- Does **not** open the playable CastleMiner Z game window.
- Automatically respawns all players in Endurance mode when every real connected player is dead, preventing dedicated servers from getting stuck on "Waiting for host to restart"
- Can persist and restore world time between restarts through the built-in **RememberTime** plugin.
- Can be launched from **DirectConnect** using **Launch Dedicated (Steam)**.
- Includes server-side **Player Enforcement** commands with SteamID-backed bans, saved player names, optional ban reasons, and transport-level hard drops.

---

## What this project does

### 1) Starts a dedicated Steam-native CastleMiner Z host
This project boots a dedicated process that initializes Steam, creates a Steam session/lobby, and exposes a CastleMiner Z-compatible hosted session without leaving the normal playable client open.

### 2) Loads game/runtime assemblies dynamically
Instead of compiling directly against the full game as a normal shared project dependency, the host loads:
- `CastleMinerZ.exe`
- `DNA.Common.dll`
- `DNA.Steam.dll`
- related runtime dependencies

This keeps the host flexible and lets it work from a configurable game install path.

### 3) Uses the active logged-in Steam account
This transport is built around the **normal SteamAPI path** under a real logged-in Steam account.

That means:
- Steam must already be running
- the server process must run under the **same Windows user context** as Steam
- the active Steam account should own CastleMiner Z

### 4) Keeps world flow on the server
Like the Lidgren server path, the Steam dedicated server is designed around host-side handling for world, chunk, inventory, and time-related flow rather than relying on a normal in-game host.

### 5) Uses a packaged dedicated-server layout
The runtime package is designed to feel familiar if you already use the Lidgren dedicated server:
- local `Libs`
- local `Inventory`
- local `Worlds`
- local `server.properties`
- optional `RunServer.bat`

### 6) Pairs well with DirectConnect
While Steam clients can use the online browser flow, **DirectConnect** also provides a convenient **Launch Dedicated (Steam)** button that closes the game first and then starts the Steam dedicated server from the frontend.

### 7) Endurance auto-respawn

When the Steam dedicated server is running with:

```properties
game-mode=0
```

it automatically handles Endurance death wipes.

Vanilla CastleMiner Z expects the original host player to restart the level after all players die. On a dedicated server, there is no real playable host pressing restart, so dead clients could remain stuck waiting for the host.

CMZDedicatedSteamServer tracks real connected player death state and sends the vanilla restart/respawn message when every real connected player is dead. The dedicated server process does not restart, and the server continues using its authoritative world time.

---

## Key differences from the Lidgren server

| CMZDedicatedLidgrenServer | CMZDedicatedSteamServer |
|---|---|
| Direct-IP / Lidgren transport | Steam-native transport |
| Best paired with **Direct Connect** for joining by IP | Intended for Steam-browser / Steam-hosted session workflows |
| Does not require the live Steam client runtime to host | Requires a real logged-in Steam client session |
| Best for local, private, or direct-IP hosting | Best for Steam-visible hosting and Steam-session workflows |

---

## How to use it

### Quick start
1. Build or obtain `CMZDedicatedSteamServer.exe`.
2. Make sure Steam is already running under the same Windows user context.
3. Edit `server.properties`.
4. Launch `CMZDedicatedSteamServer.exe` or `RunServer.bat`.
5. Confirm the server reaches Steam initialization and lobby/session creation.
6. Join from a compatible CastleMiner Z Steam client through the intended Steam workflow.

### Typical local test flow
1. Sign into the Steam account you want the server to host under.
2. Set `game-path` to your CastleMiner Z Steam install folder.
3. Start the dedicated Steam server.
4. Verify the server creates a Steam-visible session/lobby.
5. Join from another compatible Steam client/account.
6. Validate session visibility, world loading, inventory flow, and gameplay/bootstrap behavior.

## Local Test Workflow
![Local Test](_Images/LocalTest.gif)

---

## Installation

### Requirements
- Windows
- **.NET Framework 4.8.1**
- Steam installed and already running
- CastleMiner Z game files available somewhere on disk
- A Steam account that can host through the normal Steam client API path
- Optional but recommended: **DirectConnect** for frontend launch convenience

### Build output / packaged layout
The project is configured to output into a CastleForge-style runtime folder like this:

```text
!Mods\CMZDedicatedSteamServer\
```

A typical packaged runtime layout looks like:

```text
!Mods\CMZDedicatedSteamServer\
├─ CMZDedicatedSteamServer.exe
├─ RunServer.bat
├─ server.properties
├─ steam_appid.txt
├─ Libs/
│  └─ 0Harmony.dll
├─ Game/
│  └─ README.txt
├─ Inventory/
│  └─ default.inv
└─ Worlds/
   └─ {world-guid}/
      └─ world.info
```

### Game files
This repository/package intentionally does **not** ship the original CastleMiner Z game files.

You can either:
- point `game-path` to your real CastleMiner Z Steam install folder elsewhere on disk, or
- copy the required game files into the local layout you use for your dedicated package

Required runtime files include:
- `CastleMinerZ.exe`
- `DNA.Common.dll`
- `DNA.Steam.dll`
- `steam_api.dll`

---

## Configuration

The server reads configuration from `server.properties` in the server root.

### Example

```properties
# Server identity / compatibility
server-name=CMZ Steam Server

# In-game/session message shown to players. This is separate from server-name.
server-message=Welcome to this CastleForge dedicated server.

game-name=CastleMinerZSteam
network-version=4

# Bind / limits
server-ip=0.0.0.0
server-port=61903
max-players=8
password=

# Game files
game-path=C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z

# Steam runtime / hosting behavior
steam-app-id=253430
steam-lobby-visible=true
steam-allow-minimal-updates=false
steam-account-required=true
steam-friends-only=false
write-steam-appid-file=true
require-running-steam-client=true

# Save / world identity
save-owner-steam-id=0
world-guid=

# Host view / update loop
view-distance-chunks=8
tick-rate-hz=60

# Session gameplay values
game-mode=1
pvp-state=0
difficulty=1
infinite-resource-mode=false

# Optional behavior
allow-client-time-sync=false
```

### Dynamic server-name tokens

The `server-name` value supports simple runtime tokens. These tokens are replaced by the dedicated server before the name is shown to players.

Example:

```properties
server-name=Test Server | Day {day}
```

This may appear in the server browser or join/session info as:

```text
Test Server | Day 12
```

Supported tokens:

| Token          | Description                             | Example |
| -------------- | --------------------------------------- | ------- |
| `{day}`        | Current player-facing world day.        | `12`    |
| `{day00}`      | Current world day padded to two digits. | `07`    |
| `{players}`    | Current connected player count.         | `3`     |
| `{maxplayers}` | Configured maximum player count.        | `32`    |

Example with player count:

```properties
server-name=Test Server | Day {day00} | {players}/{maxplayers}
```

Example output:

```text
Test Server | Day 07 | 3/32
```

Notes:

* Tokens are optional. A normal static name such as `server-name=CMZ Server` still works.
* The day value is controlled by the dedicated server's authoritative time progression.
* Very long names may be shortened before being published to the server/session browser.

For **CMZDedicatedSteamServer**, the resolved name is published through the Steam-hosted lobby/session metadata. The raw template remains in `server.properties`, but players see the resolved display name.

Example:

```properties
server-name=Test Server | Day {day}
```

Steam browser/session display:

```text
Test Server | Day 12
```

### Server message

The `server-message` value controls the player-facing in-game/session message.

Example:

```properties
server-message=Welcome to the CastleForge 24/7 server! Discord: dsc.gg/cforge
```

The message can also use the same runtime tokens as `server-name`:

```properties
server-message=Welcome! Current day: {day}. Players online: {players}/{maxplayers}. Discord: dsc.gg/cforge
```

Supported tokens:

| Token          | Description                             | Example |
|----------------|-----------------------------------------|---------|
| `{day}`        | Current player-facing world day.        | `12`    |
| `{day00}`      | Current world day padded to two digits. | `07`    |
| `{players}`    | Current connected player count.         | `3`     |
| `{maxplayers}` | Configured maximum player count.        | `32`    |

For **CMZDedicatedSteamServer**, the resolved message is published through the Steam-hosted session/lobby metadata used by CastleMiner Z's browser/details flow.

> Steam note: CastleMiner Z's vanilla Steam browser may use the same Steam session metadata field for the displayed server message/details text, so the visible browser/details behavior can depend on how the Steam lobby metadata is consumed by the client.

### Config fields

| Key | Purpose |
|-------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `server-name`                 | Display name shown in Steam-hosted session/server info. Supports dynamic tokens such as `{day}`, `{day00}`, `{players}`, and `{maxplayers}`.                        |
| `server-message`              | Player-facing in-game/session message shown through Steam-hosted session info. Supports dynamic tokens such as `{day}`, `{day00}`, `{players}`, and `{maxplayers}`. |
| `game-name`                   | Expected CastleMiner Z network game name.                                                                                                                           |
| `network-version`             | Expected protocol/network version.                                                                                                                                  |
| `server-ip`                   | Local IP used by the host process.                                                                                                                                  |
| `server-port`                 | Host/server port setting used by the dedicated process.                                                                                                             |
| `max-players`                 | Maximum number of connected players.                                                                                                                                |
| `password`                    | Optional session password.                                                                                                                                          |
| `game-path`                   | Path to the CastleMiner Z Steam binaries folder.                                                                                                                    |
| `steam-app-id`                | Steam App ID for CastleMiner Z.                                                                                                                                     |
| `steam-lobby-visible`         | Whether the Steam-hosted lobby/session is visible.                                                                                                                  |
| `steam-allow-minimal-updates` | Allows reduced/minimal Steam update behavior if your runtime supports it.                                                                                           |
| `steam-account-required`      | Documents that this transport expects a real logged-in Steam account.                                                                                               |
| `steam-friends-only`          | Restricts the hosted session to friends-only visibility if enabled.                                                                                                 |
| `write-steam-appid-file`      | Writes `steam_appid.txt` beside the server EXE at startup.                                                                                                          |
| `require-running-steam-client`| Documents that Steam must already be running before launch.                                                                                                         |
| `save-owner-steam-id`         | Steam ID used for save identity. `0` means use the currently logged-in Steam account automatically.                                                                 |
| `world-guid`                  | GUID of the world folder to load under `Worlds\{guid}`.                                                                                                             |
| `view-distance-chunks`        | Host-side chunk view radius used for chunk-related behavior.                                                                                                        |
| `tick-rate-hz`                | Main update loop rate in Hz.                                                                                                                                        |
| `game-mode`                   | Session game mode value. `0` is Endurance; when all real connected players are dead, the dedicated server automatically sends a respawn/restart message.            |
| `pvp-state`                   | Session PVP numeric value.                                                                                                                                          |
| `difficulty`                  | Session difficulty numeric value.                                                                                                                                   |
| `infinite-resource-mode`      | Enables/disables infinite-resource session metadata.                                                                                                                |
| `allow-client-time-sync`      | Allows client-sent `TimeOfDayMessage` packets to update server time. Recommended to leave `false` unless you intentionally want that behavior.                      |

---

## Running

After deployment, start the server with either:

```bat
CMZDedicatedSteamServer.exe
```

or:

```bat
RunServer.bat
```

On startup, the host should print a summary similar to:

```text
CMZ Dedicated Steam Server
--------------------------
GameName       : CastleMinerZSteam
NetworkVersion : 4
GamePath       : C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z
ServerName     : CMZ Steam Server
MaxPlayers     : 8
WorldGuid      : ...
SteamAppId     : 253430
[Steam] Loaded native steam_api.dll from game-path.
[Steam] Initialized as 'YourHostAccount' (...)
[SteamLobby] Lobby creation requested.
[SteamLobby] Lobby created. AltSessionID=...
```

## Server Administration / Player Enforcement

CMZDedicatedSteamServer includes built-in console commands for basic player enforcement.

These commands are handled server-side and use a hard drop / transport-level removal path instead of relying only on a normal in-game kick message. This helps prevent modified clients from bypassing a kick by removing or ignoring the client-side kick-message pipeline.

### Commands

| Command                             | Description                                                                                |
|-------------------------------------|--------------------------------------------------------------------------------------------|
| `players`                           | Lists connected players with their player ID, name, and SteamID.                           |
| `bans`                              | Lists saved bans with ban type, last known player name, SteamID, reason, and created time. |
| `kick <id\|steamid\|name> [reason]` | Hard-kicks a connected player.                                                             |
| `ban <id\|steamid\|name> [reason]`  | Bans and hard-drops a connected player.                                                    |
| `unban <steamid\|name>`             | Removes a saved ban by SteamID, exact name, or unique partial name.                        |

### Examples

```text
players
bans

kick 2
kick 2 Being annoying
kick jacob Being annoying
kick "Jacob Smith" Being annoying
kick 76561198000000000 Being annoying

ban 2
ban 2 Griefing protected areas
ban jacob Griefing protected areas
ban "Jacob Smith" Griefing protected areas
ban 76561198000000000 Griefing protected areas

unban jacob
unban "Jacob Smith"
unban 76561198000000000
```

### Names with spaces

Player names with spaces should be wrapped in quotes:

```text
ban "Jacob Smith" Griefing protected areas
kick "Jacob Smith" Being annoying
```

You can also use the player ID from `players`, which is usually the safest option:

```text
players
ban 2 Griefing protected areas
```

### Optional reasons

Kick and ban commands support optional reasons. The reason is saved to the ban list and passed to the transport disconnect/deny path when possible.

Example:

```text
ban 2 Griefing protected areas
```

Because this uses a hard drop path, the reason should be treated as best-effort display text. A modified client may hide the visible message, but the server still removes the peer from the authoritative session.

### Ban storage

Steam bans are stored under:

```text
PlayerEnforcement\Bans.ini
```

Steam bans use the player's SteamID as the main ban key and also save the last known player name so the ban list remains readable.

Example:

```ini
# type|value|lastName|reason|createdUtcTicks
steam|76561198000000000|Jacob Smith|Griefing protected areas|638813123456789000
```

This means a player changing their Steam display name will not bypass the ban.

---

## Compatibility Notes

CMZDedicatedSteamServer is intended for a **CastleMiner Z-compatible Steam hosting workflow** built around the original runtime and the normal Steam client API path.

It is especially useful for:
- Steam-browser visibility goals
- dedicated testing and development
- hosting under a separate Steam account
- pairing with **DirectConnect** for quick launch convenience

### Important notes
- The project expects access to the original CastleMiner Z runtime files.
- It does **not** bundle the original game files.
- Steam must already be running before the dedicated server starts.
- The dedicated server process should run under the **same Windows user context** as the Steam client.
- This project is **not** the anonymous Steam GameServer API path.
- Session compatibility still depends on matching client/server expectations such as `game-name` and `network-version`.

---

## Server Plugins

CastleForge Dedicated Servers now include basic **server-side plugin support** for host-authoritative world protections and future server extensions.

Plugins run inside the dedicated server process and can inspect selected host/world packets before the server applies or relays them. This allows the server to enforce rules even when connecting players do **not** have the matching client-side mod installed.

Current built-in plugin support includes:

- **Announcements** private join messages and timed global messages
- **FloodGuard** malicious packet spam protection
- **RegionProtect** server enforcement
- **RememberTime** per-world time persistence between restarts
- **Player Enforcement** console commands with SteamID-backed persistent bans
- block mining / placing protection
- explosion protection
- crate item protection
- crate break protection
- per-world plugin configuration

> Server plugins are currently compiled into the dedicated server build. External plugin DLL loading may be added later.

## Announcements Server Plugin

The dedicated servers include a built-in **Announcements** plugin for simple server messages.

Announcements can:

- send a private welcome message to each joining player
- send a timed global message to all connected players
- wait a configurable amount of time before the first global message
- require a minimum number of online players before global messages are sent
- reload its config from disk using the server console `reload` command, if enabled by the host

### Config location

The Announcements config is stored beside each dedicated server executable:

```text
CMZDedicatedSteamServer/
└─ Plugins/
   └─ Announcements/
      └─ Announcements.Config.ini
```

For the Lidgren dedicated server:

```text
CMZDedicatedLidgrenServer/
└─ Plugins/
   └─ Announcements/
      └─ Announcements.Config.ini
```

### Example config

```ini
[General]
Enabled = true

[Join]
PrivateJoinMessageEnabled = true
PrivateJoinMessage = Welcome {player}! This is a CastleForge dedicated server. Join us: dsc.gg/cforge

[Global]
TimedGlobalMessageEnabled = true
GlobalMessage = Need help, updates, or mods? Join the CastleForge Discord: dsc.gg/cforge
InitialGlobalDelaySeconds = 120
GlobalMessageIntervalMinutes = 15
MinimumPlayersForGlobalMessage = 1
```

### Message tokens

Announcement messages support simple runtime tokens:

| Token          | Description                      | Example      |
|----------------|----------------------------------|--------------|
| `{player}`     | Joining player's display name.   | `RussDev7`   |
| `{players}`    | Current connected player count.  | `3`          |
| `{maxplayers}` | Configured maximum player count. | `32`         |
| `{time}`       | Current local server time.       | `8:30 PM`    |
| `{date}`       | Current local server date.       | `2026-04-26` |

### Behavior

The private join message is sent only to the joining player.

The timed global message is broadcast to all connected players after `InitialGlobalDelaySeconds`, then repeats every `GlobalMessageIntervalMinutes`.

Set `MinimumPlayersForGlobalMessage = 0` to allow global messages even when the server is empty, or set it to `1` or higher to only announce when players are online.

## RegionProtect Server Plugin

The dedicated servers include a built-in **RegionProtect** plugin that protects configured world areas directly from the server.

Unlike the normal client/host RegionProtect mod, the dedicated server version does not require players to install anything client-side. The server checks protected actions before saving or relaying world changes.

### Protected actions

RegionProtect currently protects:

| Action                  | Packet handled                                     | Description                                              |
|-------------------------|----------------------------------------------------|----------------------------------------------------------|
| Mining / block breaking | `AlterBlockMessage`                                | Blocks protected terrain removal                         |
| Block placing           | `AlterBlockMessage`                                | Blocks protected block placement                         |
| Explosions              | `DetonateExplosiveMessage` / `RemoveBlocksMessage` | Blocks protected explosion damage                        |
| Crate item edits        | `ItemCrateMessage`                                 | Blocks adding/removing crate contents in protected areas |
| Crate breaking          | `DestroyCrateMessage`                              | Blocks crate destruction in protected areas              |

### Config location

RegionProtect stores its configuration beside each dedicated server executable:

```text
CMZDedicatedLidgrenServer/
└─ Plugins/
   └─ RegionProtect/
      ├─ RegionProtect.Config.ini
      └─ Worlds/
         └─ <world-guid>/
            └─ RegionProtect.Regions.ini
```

For the Steam dedicated server:

```text
CMZDedicatedSteamServer/
└─ Plugins/
   └─ RegionProtect/
      ├─ RegionProtect.Config.ini
      └─ Worlds/
         └─ <world-guid>/
            └─ RegionProtect.Regions.ini
```

### General config

`RegionProtect.Config.ini` controls which protection systems are enabled:

```ini
[General]
Enabled                = true
ProtectMining          = true
ProtectPlacing         = true
ProtectExplosions      = true
ProtectCrateItems      = true
ProtectCrateMining     = true
WarnPlayers            = true
WarningCooldownSeconds = 2
LogDenied              = true
```

### Region config

Each world has its own `RegionProtect.Regions.ini` file:

```ini
[SpawnProtection]
Enabled        = true
Range          = 64
AllowedPlayers = RussDev7

[Region:SpawnTown]
Min            = -80,0,-80
Max            = 80,120,80
AllowedPlayers = RussDev7,SomeAdmin
```

### Player warning behavior

When a player tries to edit a protected area, the server blocks the action and sends a warning such as:

```text
[RegionProtect] Protected by region 'SpawnTown'. Breaking blocks here was blocked. Client-only desync; not saved to server.
```

In some cases, the client may briefly show a block as broken or changed. The server does **not** save that blocked change, and the area will correct itself after resyncing or rejoining.

### Notes and limitations

* RegionProtect is server-authoritative.
* Players do not need the RegionProtect mod installed to be blocked by protected regions.
* Commands such as `/regionpos` and `/regioncreate` are not currently part of the dedicated server plugin.
* Regions are currently edited manually through the `.ini` files.
* Explosion restoration can visually desync on the attacking client, but protected explosion damage is not saved to the server.

## FloodGuard plugin

FloodGuard is a lightweight packet-rate guard for the dedicated server. It watches inbound gameplay packets before normal host/world handling or peer relay. When a sender exceeds the configured rate, the server temporarily blackholes that sender's packets instead of relaying or applying them.

Config is created on first run at:

```text
Plugins\FloodGuard\FloodGuard.Config.ini
```

Default config:

```ini
[General]
Enabled = true
PerSenderMaxPacketsPerSec = 120
BlackholeMs = 3000

[AllowedPlayers]
# Comma-separated allow list. Entries may be player names, Player1-style fallback names,
# numeric GIDs, or SteamIDs on the Steam server.
AllowedPlayers =
```

Notes:
- `PerSenderMaxPacketsPerSec` is counted per sender/GID over a one-second window.
- `BlackholeMs` is how long to silently drop packets after the sender exceeds the limit.
- `AllowedPlayers` bypasses FloodGuard for trusted players or test accounts.

## RememberTime Server Plugin

CMZDedicatedSteamServer includes a built-in **RememberTime** plugin that saves the host's authoritative day/time value and restores it when the server starts again.

RememberTime can:

- save the current authoritative server time on a configurable interval
- restore the saved time when the Steam dedicated server process starts
- save one final time during clean server shutdown
- store saved time per world so different worlds keep separate day/time progress
- reduce disk writes by using `SaveIntervalSeconds` instead of writing every tick

### Config location

RememberTime creates its config and per-world state beside the Steam dedicated server executable:

```text
CMZDedicatedSteamServer/
└─ Plugins/
   └─ RememberTime/
      ├─ RememberTime.Config.ini
      └─ Worlds/
         └─ <world-guid>/
            └─ Time.State.ini
```

### Example config

```ini
[General]
Enabled = true

# Saves the server's current day/time every X seconds.
# Higher values reduce disk writes.
# Lower values reduce lost time if the server crashes.
SaveIntervalSeconds = 60

# Restores the saved time when the server process starts.
RestoreOnStartup = true

# Writes one final time when the server stops cleanly.
SaveOnShutdown = true
```

### Example saved state

```ini
[State]
TimeOfDay = 12.4135227
DisplayDay = 13
SavedUtc = 2026-04-27T18:20:31.0000000Z
Reason = interval
```

### Notes

- `TimeOfDay` is the full server day/time float, not only the `0.0` to `1.0` visual time fraction.
- Example: `12.41` means the server is on display **Day 13** at roughly the same visual time as `0.41`.
- `SaveIntervalSeconds = 60` is a good default for normal hosting.
- If the process crashes, the server may lose up to the configured interval. Clean shutdowns still write one final save when `SaveOnShutdown = true`.
- The saved time affects dynamic `{day}` and `{day00}` tokens after restore because those tokens use the authoritative server time.

---

## Troubleshooting

<details>
<summary><strong>The server says Steam initialization failed</strong></summary>

Check that:
- Steam is already running
- the dedicated server process is using the same Windows user context as Steam
- the active Steam account has access to CastleMiner Z
- `steam_api.dll` is available through the configured game path

</details>

<details>
<summary><strong>The server says the game folder or required runtime files are missing</strong></summary>

Make sure the host can find the real CastleMiner Z files through `game-path` or your local runtime layout.

Expected files include:

```text
CastleMinerZ.exe
DNA.Common.dll
DNA.Steam.dll
steam_api.dll
```

</details>

<details>
<summary><strong>Players are stuck on "Waiting for host to restart" in Endurance</strong></summary>

Make sure the server build includes Endurance auto-respawn support.

This situation can happen in `game-mode=0` because vanilla CastleMiner Z expects the original playable host to press restart after everyone dies. Dedicated servers do not have a real host player on that screen.

With Endurance auto-respawn support, the server tracks real connected players and automatically sends the restart/respawn message when all real players are dead.

If players still remain stuck, check that:

- the server is actually running `game-mode=0`
- players are fully connected and sending normal player update packets
- the server log shows the Endurance auto-restart message
- the client and server are using compatible builds

</details>

<details>
<summary><strong>A kicked or banned player does not see a normal kick message</strong></summary>

Player Enforcement uses a hard drop / transport-level removal path. This is intentional.

Some modified clients can hide, remove, or ignore the normal in-game kick-message pipeline. The dedicated server therefore removes the peer from server-side state, broadcasts a drop-peer update to remaining clients, and stops accepting packets from the removed peer.

The visible reason message is best-effort only. A normal client may show it, but a modified client may hide it.

</details>

<details>
<summary><strong>A banned Steam player changed names and still cannot join</strong></summary>

Steam bans are keyed by SteamID, not display name. The saved player name is only stored to make the ban list readable.

Use:

```text
bans
unban 76561198000000000
````

to remove the SteamID ban.

</details>

<details>
<summary><strong>The server starts but does not appear in the browser</strong></summary>

Check:
- `steam-lobby-visible=true`
- `game-name`
- `network-version`
- `game-mode`
- `difficulty`
- `pvp-state`
- `infinite-resource-mode`

Also verify that the server completed Steam initialization and reached lobby/session creation in the startup log.

</details>

<details>
<summary><strong>Joining hangs or behaves unexpectedly</strong></summary>

This project is part of an evolving Steam-native dedicated-server workflow.

If you are troubleshooting join issues, verify:
- both sides are using compatible CastleForge / CastleMiner Z expectations
- the server successfully created its Steam-hosted session
- the client and server agree on `game-name` and `network-version`
- your world/inventory/runtime layout is valid

</details>

<details>
<summary><strong>The wrong world loads</strong></summary>

Verify:

```properties
world-guid=...
```

and make sure the matching folder exists under:

```text
Worlds\{world-guid}
```

</details>

---

## Technical Overview

<details>
<summary><strong>Main Components</strong></summary>

### `Program.cs`
Entry point for the dedicated Steam host.

It:
- loads `server.properties`
- resolves the game binaries folder from `game-path`
- resolves support libraries from `Libs`
- loads `CastleMinerZ.exe` and related assemblies
- applies Harmony patches
- prints a startup summary
- starts the Steam dedicated host and enters the update loop

### `Hosting/SteamDedicatedServer.cs`
The main Steam-hosting runtime.

It is responsible for:
- Steam-side host setup
- lobby/session creation flow
- peer tracking
- connection approval flow
- channel/data routing
- host-side message/bootstrap handling
- dedicated update-loop behavior

### `Steam/SteamServerBootstrap.cs`
Steam runtime bootstrap helper.

It handles:
- locating/loading Steam runtime components
- writing `steam_appid.txt` when configured
- initializing the normal Steam client API path
- resolving the active host Steam identity

### `World/ServerWorldHandler.cs`
Host-side world and persistence bridge shared conceptually with the dedicated hosting workflow.

It handles:
- `world.info` loading
- save device initialization
- chunk request / chunk cache flow
- inventory persistence
- host-consumed world messages
- time-of-day handling

### `Config/SteamServerConfig.cs`
Loads and validates typed config values from `server.properties`, supplies defaults, and derives world-related paths.

</details>

---

## Original standalone project

This CastleForge version of **CMZDedicatedSteamServer** is based on and evolves the earlier standalone dedicated-server project:

- [CMZDedicatedServers](https://github.com/RussDev7/CMZDedicatedServers)

## License

This project is licensed under **GPL-3.0-or-later**.
See [LICENSE](LICENSE) for details.

## Credits

Developed and maintained by:
- RussDev7
- unknowghost0
