# CMZDedicatedLidgrenServer

> Host a **dedicated CastleMiner Z server** outside the normal game client, keep world state on the server, and pair it with mods like **DirectConnect** for a much smoother custom multiplayer workflow.

**CMZDedicatedLidgrenServer** is the dedicated-server companion project for CastleForge. It is built in **C# / .NET Framework 4.8.1**, loads the original CastleMiner Z runtime through **reflection**, starts a **Lidgren-backed compatible host**, and manages world, chunk, inventory, and time flow on the server side.

> **Transport note:** **CMZDedicatedLidgrenServer** is the **Lidgren / direct-IP** dedicated-server transport for CastleForge. It is separate from **CMZDedicatedSteamServer**, which hosts through the **Steam-native lobby / Steam transport path** under a real logged-in Steam account.

![Preview](_Images/Preview.png)

---

## Why use CMZDedicatedLidgrenServer?

CastleMiner Z was never designed around a clean standalone server-hosting experience for modded or custom direct-IP workflows. **CMZDedicatedLidgrenServer** fills that gap by moving core hosting responsibilities out of the normal game client and into a dedicated executable.

That gives you a cleaner setup for:
- private servers
- local testing
- development workflows
- persistent hosted worlds
- pairing with **DirectConnect** for quick manual joining

### Highlights
- **Dedicated executable host** for CastleMiner Z.
- **Direct-IP friendly** workflow for compatible clients.
- Loads the original game/runtime assemblies through **reflection**.
- Supports a configurable **`game-path`** instead of forcing a hardcoded local game folder.
- Loads Harmony from a local **`Libs`** folder.
- Handles **discovery, approvals, session metadata, and gameplay relay**.
- Hosts **world state server-side** through `ServerWorldHandler`.
- Loads **`world.info`**, chunk delta files, and saved player inventories.
- Handles **chunk requests, inventory retrieval/storage, terrain edits, and pickup flow**.
- Keeps **authoritative day/time progression** on the host.
- Includes a packaged **server layout** with sample config, sample world data, and a default inventory template.

---

## What this project does

### 1) Starts a dedicated CastleMiner Z-compatible server host
This project boots a server process that binds to your configured IP and port, validates compatible clients, and hosts a CastleMiner Z-compatible multiplayer session outside the normal playable client.

### 2) Loads game/runtime assemblies dynamically
Instead of compiling directly against the full game as a normal shared project dependency, the host loads:
- `CastleMinerZ.exe`
- `DNA.Common.dll`
- related runtime dependencies

This keeps the host flexible and lets it work from a configurable game install path.

### 3) Keeps world flow on the server
The host reads the target world, responds to world/chunk requests, processes terrain mutation messages, and keeps the world-side state centered on the server process rather than trusting a normal in-game host.

### 4) Handles inventory persistence
The server can retrieve and store player inventory data, and it falls back to a packaged default inventory template when a player save is not yet present.

### 5) Tracks and relays important multiplayer packets
The host currently handles the important flow for:
- discovery/session info
- connection approval
- player presence bootstrap
- gameplay relay
- chunk delivery
- pickup creation/request/consume flow
- periodic time-of-day broadcasts

### 6) Pairs well with DirectConnect
While this project is documented separately, it is an especially strong match for **DirectConnect**, since that mod gives players a much cleaner in-game direct-IP join path for compatible dedicated hosts.

### Join model
CMZDedicatedLidgrenServer is primarily intended for:
- **DirectConnect**
- compatible **Lidgren-backed clients**
- direct-IP workflows for local, private, or development hosting

It is **not** the Steam-native dedicated transport. If you want a server that appears through the Steam-hosted browser workflow, use **CMZDedicatedSteamServer** instead.

---

## Feature Breakdown

<details>
<summary><strong>Reflection-Based Dedicated Hosting</strong></summary>

### What it does
CMZDedicatedLidgrenServer resolves CastleMiner Z and DNA runtime assemblies at startup, then uses reflection to construct and interact with the networking and world systems the dedicated host needs.

### Why this matters
This avoids requiring the host to be structured like a normal game client build while still staying compatible with the game's original message types and runtime behavior.

### Practical result
- The host checks for required files early.
- It can use a custom `game-path`.
- It can probe both the local `Libs` folder and the configured game folder for dependencies.

</details>

<details>
<summary><strong>Discovery, Approval, and Session Metadata</strong></summary>

The server is responsible for more than just opening a socket.

It also handles:
- compatible discovery replies
- connection approval
- session/game metadata
- join-state flow for newly connecting clients

This helps clients see and join the server using the expected CastleMiner Z-compatible network/session information.

</details>

<details>
<summary><strong>Server-Side World Handling</strong></summary>

`ServerWorldHandler` is the main bridge for world-related server behavior.

It handles:
- loading `world.info`
- initializing save-device access
- reading a spawn hint from the world
- chunk list generation around spawn
- chunk delta loading and caching
- host-consumed world messages
- terrain mutation persistence

This is a major reason the project feels like a real host instead of only a thin packet relay.

</details>

<details>
<summary><strong>Inventory Retrieval and Storage</strong></summary>

The server supports host-side inventory flow.

That includes:
- loading saved inventory data for a connecting player
- sending inventory data back to the client
- using a default inventory template when needed
- saving inventory updates back to storage

Packaged default inventory template:

```text
Inventory\default.inv
```

</details>

<details>
<summary><strong>Chunk Loading and Caching</strong></summary>

The host builds a chunk list around the world's spawn hint and serves chunk data back to clients as requested.

Notable behavior includes:
- spawn-based chunk list generation
- chunk delta file loading
- in-memory LRU-style chunk caching
- chunk response payload construction through the original message registry

</details>

<details>
<summary><strong>Pickup Flow Support</strong></summary>

The server tracks spawned pickups and can resolve request/consume flow on the host.

This includes:
- create pickup handling
- pending pickup tracking
- request pickup handling
- consume pickup payload creation and relay

That helps keep loot flow authoritative enough for basic dedicated play instead of relying entirely on client-side assumptions.

</details>

<details>
<summary><strong>Authoritative Time / Day Progression</strong></summary>

CMZDedicatedLidgrenServer advances world time using **real elapsed time** rather than assuming a normal client-host frame cadence.

It also periodically broadcasts time-of-day updates so connected players stay synchronized.

There is also a config toggle:

```properties
allow-client-time-sync=false
```

When left disabled, the server remains authoritative for time progression.

</details>

<details>
<summary><strong>Player Presence Recovery</strong></summary>

The host caches `PlayerExistsMessage` payloads and can replay them to newly joined clients.

This improves reliability for player bootstrap/presence flow if the original handshake timing is missed or races during join.

</details>

---

## How to use it

### Quick start
1. Build or obtain `CMZDedicatedLidgrenServer.exe`.
2. Place your CastleMiner Z game files in the expected location, or point `game-path` to your install.
3. Edit `server.properties`.
4. Launch `CMZDedicatedLidgrenServer.exe` or `RunServer.bat`.
5. Connect from a compatible client using the configured IP and port.
6. For a nicer client workflow, pair it with **DirectConnect** and use **Launch Dedicated (Lidgren)** plus **Direct Connect** on the client side.

### Typical local test flow
1. Set `server-ip=0.0.0.0` or your local interface.
2. Keep `server-port=61903` unless you need a custom port.
3. Start the host.
4. Connect locally using:
   - `127.0.0.1:61903`
   - or your LAN IP and chosen port
5. Validate world loading, player join, inventory flow, and chunk streaming.

## Local Test Workflow
![Local Test](_Images/LocalTest.gif)

---

## Installation

### Requirements
- Windows
- **.NET Framework 4.8.1**
- CastleMiner Z game files available somewhere on disk
- A compatible CastleForge workflow or manual deployment layout
- Optional but recommended: **DirectConnect** for smoother joining

### Build output / packaged layout
The project is configured to output into a CastleForge-style runtime folder like this:

```text
!Mods\CMZDedicatedLidgrenServer\
```

A typical packaged runtime layout looks like:

```text
!Mods\CMZDedicatedLidgrenServer\
├─ CMZDedicatedLidgrenServer.exe
├─ RunServer.bat
├─ server.properties
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
- copy the game files into the local `Game` folder structure used by your runtime package, or
- point `game-path` to your real CastleMiner Z install folder elsewhere on disk

---

## Configuration

The server reads configuration from `server.properties` in the server root.

### Example

```properties
# Server identity / compatibility
server-name=CMZ Server

# In-game/session message shown to players. This is separate from server-name.
server-message=Welcome to this CastleForge dedicated server.

game-name=CastleMinerZSteam
network-version=4

# Game files
game-path=C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z

# Bind / limits
server-ip=0.0.0.0
server-port=61903
max-players=8

# Save / world identity
save-owner-steam-id=76561198296842857
world-guid=b8c81243-b6ac-48fe-a782-1e2dc5a44d17

# Runtime tuning
view-distance-chunks=8
tick-rate-hz=60

# Session properties
game-mode=1
pvp-state=0
difficulty=1

# Optional behavior
allow-client-time-sync=false
```

### Dynamic server-name tokens

The `server-name` value supports simple runtime tokens. These tokens are replaced by the dedicated server before the name is shown to players.

Example:

```properties
server-name=Test Server | Day {day}
````

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

For **CMZDedicatedLidgrenServer**, the resolved name is sent through discovery responses and join-time server/session info packets. The raw template remains in `server.properties`, but compatible clients see the resolved display name.
For **CMZDedicatedSteamServer**, the resolved name is published through the Steam-hosted lobby/session metadata. The raw template remains in `server.properties`, but players see the resolved display name.

Example:

```properties
server-name=Test Server | Day {day} | dsc.gg/cforge
```

DirectConnect/session display:

```text
Test Server | Day 12 | dsc.gg/cforge
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

For **CMZDedicatedLidgrenServer**, the resolved message is sent through discovery/session info so compatible clients can show it separately from the server name.

### Config fields

| Key | Purpose |
|--------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `server-name`            | Display name shown to players in discovery/session info. Supports dynamic tokens such as `{day}`, `{day00}`, `{players}`, and `{maxplayers}`.            |
| `server-message` | Player-facing in-game/session message shown through discovery/session info. Supports dynamic tokens such as `{day}`, `{day00}`, `{players}`, and `{maxplayers}`. |
| `game-name`              | Expected CastleMiner Z network game name.                                                                                                                |
| `network-version`        | Expected protocol/network version.                                                                                                                       |
| `game-path`              | Optional path to the CastleMiner Z binaries folder. Can be absolute or relative.                                                                         |
| `server-ip`              | Bind address. `0.0.0.0` and `any` both bind all interfaces.                                                                                              |
| `server-port`            | Port clients connect to.                                                                                                                                 |
| `max-players`            | Maximum number of connected players.                                                                                                                     |
| `save-owner-steam-id`    | Steam ID used for save-device key derivation and storage access.                                                                                         |
| `world-guid`             | GUID of the world folder to load under `Worlds\{guid}`.                                                                                                  |
| `view-distance-chunks`   | Host-side chunk view radius used for chunk-related behavior.                                                                                             |
| `tick-rate-hz`           | Main update loop rate in Hz.                                                                                                                             |
| `game-mode`              | Session game mode numeric value.                                                                                                                         |
| `pvp-state`              | Session PVP numeric value.                                                                                                                               |
| `difficulty`             | Session difficulty numeric value.                                                                                                                        |
| `allow-client-time-sync` | Allows client-sent `TimeOfDayMessage` packets to update server time. Recommended to leave `false` unless you intentionally want that behavior.           |

---

## Building

### Visual Studio / MSBuild
Project file:

```text
CastleForge\CastleForge\Servers\CMZDedicatedLidgrenServer\CMZDedicatedLidgrenServer.csproj
```

Important project settings:
- Target framework: `net481`
- Platform target: `x86`
- Output path: `!Mods\CMZDedicatedLidgrenServer\`
- Harmony copied to `Libs\0Harmony.dll`

### What gets copied into the runtime folder
The project is set up to copy runtime support content such as:
- `Libs\0Harmony.dll`
- `RunServer.bat`
- `Game\README.txt`
- `Inventory\default.inv`
- `Worlds\...`
- `Templates\server.properties` as the runtime `server.properties`

---

## Running

After deployment, start the server with either:

```bat
CMZDedicatedLidgrenServer.exe
```

or:

```bat
RunServer.bat
```

On startup, the host prints a summary similar to:

```text
CMZ Server Host
---------------
GameName         : CastleMinerZSteam
NetworkVersion   : 4
Bind             : 0.0.0.0:61903
ServerName       : CMZ Server
MaxPlayers       : 8
SaveOwnerSteamId : 76561198296842857
WorldGuid        : b8c81243-b6ac-48fe-a782-1e2dc5a44d17
WorldFolder      : Worlds\b8c81243-b6ac-48fe-a782-1e2dc5a44d17
WorldPath        : ...
WorldInfo file   : ...\world.info
World loaded     : True
```

Then connect with a compatible client using the configured IP and port.

---

## Compatibility Notes

CMZDedicatedLidgrenServer is intended for a **CastleMiner Z-compatible dedicated hosting workflow** built around the original runtime and compatible network/session expectations. It is the **direct-IP / Lidgren** option in the CastleForge dedicated-server lineup.

It is especially useful with:
- **DirectConnect**
- local multiplayer testing
- private dedicated worlds
- development/debug workflows
- custom hosted sessions that benefit from a cleaner direct-IP path

### Important notes
- The project expects access to the original CastleMiner Z runtime files.
- It does **not** bundle the original game files.
- The current runtime is built around the game's original message registry and reflected types.
- Session compatibility still depends on matching client/server expectations such as `game-name` and `network-version`.
- For Steam-native browser hosting under a logged-in Steam account, use **CMZDedicatedSteamServer** instead.

---

## Server Plugins

CastleForge Dedicated Servers now include basic **server-side plugin support** for host-authoritative world protections and future server extensions.

Plugins run inside the dedicated server process and can inspect selected host/world packets before the server applies or relays them. This allows the server to enforce rules even when connecting players do **not** have the matching client-side mod installed.

Current built-in plugin support includes:

- **Announcements** private join messages and timed global messages
- **FloodGuard** malicious packet spam protection
- **RegionProtect** server enforcement
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
````

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


---

## Troubleshooting

<details>
<summary><strong>The server says the game folder or CastleMinerZ.exe is missing</strong></summary>

Make sure the host can find the real CastleMiner Z files either:
- under the local runtime `Game` path you are using, or
- through `game-path` in `server.properties`

Expected core file:

```text
CastleMinerZ.exe
```

</details>

<details>
<summary><strong>The server says 0Harmony.dll is missing</strong></summary>

Make sure Harmony exists under:

```text
Libs\0Harmony.dll
```

This project expects Harmony in the local `Libs` folder used by the server runtime package.

</details>

<details>
<summary><strong>Clients cannot join</strong></summary>

Check:
- `server-ip`
- `server-port`
- firewall / router rules
- that the client is using the same IP and port
- `game-name`
- `network-version`

If you are using DirectConnect, verify the typed address matches the actual dedicated host endpoint.

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

<details>
<summary><strong>Inventory or save access is not behaving as expected</strong></summary>

Check that:
- `save-owner-steam-id` is populated correctly
- your world and inventory folders exist in the runtime layout
- the host has permission to read/write those folders

The save-owner Steam ID is used for save-device/storage identity handling.

</details>

<details>
<summary><strong>Time sync behaves strangely</strong></summary>

By default, the host keeps time authoritative and advances it using real elapsed time.

If you changed this:

```properties
allow-client-time-sync=true
```

client-sent time packets may influence server time.

For most stable dedicated setups, leaving this `false` is the safer default.

</details>

---

## Technical Overview

<details>
<summary><strong>Main Components</strong></summary>

### `Program.cs`
Entry point for the dedicated host.

It:
- loads `server.properties`
- resolves the game binaries folder from `game-path` or falls back to a local game folder layout
- resolves support libraries from `Libs`
- loads `CastleMinerZ.exe` and related assemblies
- applies Harmony patches
- prints a startup summary
- starts the server and enters the update loop

### `Hosting/LidgrenServer.cs`
The dedicated networking host.

It is responsible for:
- binding the socket
- discovery handling
- connection approval
- status changes
- channel 0 / channel 1 packet handling
- gameplay/bootstrap relay
- cached/replayed `PlayerExistsMessage` support
- live connection enumeration for outbound sends
- pickup consume relay support
- authoritative time/day progression and periodic time broadcasts

### `World/ServerWorldHandler.cs`
Host-side world and persistence bridge.

It handles:
- message ID/type lookup
- `world.info` loading
- save device initialization
- spawn hint loading
- chunk list / chunk request handling
- chunk caching
- terrain mutation handling
- inventory persistence
- host-consumed world messages
- pickup request/create/consume support
- raw `TimeOfDayMessage` payload construction

### `Networking/CmzMessageCodec.cs`
Maps CastleMiner Z message IDs to reflected message types and back, and builds raw message payloads in the expected CMZ format.

### `Config/ServerConfig.cs`
Loads and validates typed config values from `server.properties`, supplies defaults, and derives world-related paths.

### `Patching/ServerPatches.cs`
Central Harmony bootstrap that applies runtime patches once per process.

</details>

<details>
<summary><strong>Implementation Notes</strong></summary>

### Core design
CMZDedicatedLidgrenServer is designed around:
- runtime reflection
- compatibility with the original message registry
- host-authoritative world/inventory flow
- packaged deployment for repeatable server setups

### Included runtime support files
The project includes support content so a release package can be much easier to stand up:
- config template
- runtime `server.properties`
- a game-files README
- a default inventory template
- a sample world folder structure
- Harmony in the local runtime layout

### Why this matters
That makes the project much easier to showcase and distribute as part of CastleForge than a bare technical source dump.

</details>

---

## Original standalone project

This CastleForge version of **CMZDedicatedLidgrenServer** is based on and evolves the earlier standalone dedicated-server project:

- [CMZDedicatedServers](https://github.com/RussDev7/CMZDedicatedServers)

## License

This project is licensed under **GPL-3.0-or-later**.
See [LICENSE](LICENSE) for details.

## Credits

Developed and maintained by:
- RussDev7
- unknowghost0