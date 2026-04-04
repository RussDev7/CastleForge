# CMZDedicatedServer

A dedicated server host for **CastleMiner Z** built in **C# / .NET Framework 4.8.1**.

![Preview](_Images/Preview.png)

This project loads the original game/runtime assemblies through reflection, starts a Lidgren-backed compatible server, and hosts persistent world state outside the normal game client.

## What it does

- Starts a dedicated CastleMiner Z compatible server by IP and port
- Loads the original game assemblies at runtime via reflection
- Supports a configurable `game-path` instead of requiring the game under a hardcoded local `Game` folder
- Loads Harmony from a local `libs` folder instead of next to the executable
- Handles connection approval, discovery/session metadata, and gameplay packet relay
- Hosts world state server-side through `ServerWorldHandler`
- Loads `world.info`, chunk delta data, and player inventories from disk
- Relays player visibility, movement, text, block edits, chunk requests, pickups, and inventory flow
- Keeps authoritative day/time progression server-side and periodically broadcasts it to clients
- Supports a configurable bind IP, port, player count, world GUID, view distance, and tick rate

## Project layout

```text
CMZDedicatedServer-main/
├─ LICENSE
├─ README.md
├─ build.bat
├─ clean.bat
└─ src/
   ├─ CMZServerHost.sln
   └─ CMZServerHost/
      ├─ App.config
      ├─ CMZServerHost.csproj
      ├─ CmzMessageCodec.cs
      ├─ LidgrenServer.cs
      ├─ Program.cs
      ├─ ServerAssemblyLoader.cs
      ├─ ServerConfig.cs
      ├─ ServerPatches.cs
      ├─ ServerRuntime.cs
      ├─ ServerWorldHandler.cs
      ├─ lib/
      │  └─ 0Harmony.dll
      └─ build/
         └─ ServerHost/
            ├─ CMZServerHost.exe
            ├─ server.properties
            ├─ Libs/
            │  └─ 0Harmony.dll
            └─ Game/
               ├─ CastleMinerZ.exe
               ├─ DNA.Common.dll
               └─ game content files...
```

## Main components

### `Program.cs`
Entry point for the dedicated host.

It:
- loads `server.properties`
- resolves the game binaries folder from `game-path` or falls back to `./game`
- resolves support libraries from `./libs`
- loads `CastleMinerZ.exe` and related assemblies
- applies Harmony patches
- prints a startup summary
- starts the server and enters the update loop

### `LidgrenServer.cs`
The dedicated networking host.

It is responsible for:
- binding the socket
- connection approval
- session startup
- direct-IP traffic handling
- channel 0 / channel 1 packet handling
- relay of gameplay and bootstrap messages
- player-exists cache/replay for new joiners
- live connection enumeration for outgoing sends
- pickup consume relay support
- authoritative day/time progression and periodic world time broadcasts

### `ServerWorldHandler.cs`
Host-side world and persistence bridge.

It handles:
- message ID/type lookup
- `world.info` loading
- save device initialization
- chunk list / chunk request handling
- terrain mutation handling
- inventory persistence
- host-consumed world messages
- pickup request/create/consume support
- raw `TimeOfDayMessage` payload construction

### `CmzMessageCodec.cs`
Maps CastleMiner Z message IDs to reflected message types and back.

### `ServerConfig.cs`
Loads typed config values from `server.properties`.

## Requirements

- Windows
- **.NET Framework 4.8.1**
- Visual Studio / MSBuild capable of building .NET Framework projects
- The original CastleMiner Z game files available somewhere on disk

The game files do **not** have to live under a folder literally named `Game` as long as `game-path` points to the correct location.

## Configuration

The server reads configuration from `server.properties` in the server root.

Example:

```properties
server-name=CMZ Server
game-name=CastleMinerZSteam
network-version=4

game-path=C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z

server-ip=0.0.0.0
server-port=61903
max-players=8

steam-user-id=76561198296842857
world-guid=b8c81243-b6ac-48fe-a782-1e2dc5a44d17

view-distance-chunks=8
tick-rate-hz=60

game-mode=1
pvp-state=0
difficulty=1
```

### Config fields

| Key | Purpose |
|---|---|
| `server-name` | Display name shown to clients |
| `game-name` | Expected network game name |
| `network-version` | Expected protocol version |
| `game-path` | Optional path to the CastleMiner Z binaries folder; defaults to `./game` when omitted |
| `server-ip` | Bind address (`0.0.0.0` or `any` binds all interfaces) |
| `server-port` | Port clients connect to |
| `max-players` | Maximum connected players |
| `steam-user-id` | Save-device / world key identity used for storage access |
| `world-guid` | GUID used to locate the world folder |
| `view-distance-chunks` | Chunk radius used by the host |
| `tick-rate-hz` | Server update loop rate |
| `game-mode` | Session game mode value |
| `pvp-state` | Session PVP state value |
| `difficulty` | Session difficulty value |

## Building

### Option 1: Use the included batch script

```bat
build.bat
```

This script:
- locates MSBuild using `vswhere`
- restores and builds the solution
- collects release files
- creates a zip package

### Option 2: Build from Visual Studio / MSBuild

Solution:

```text
src/CMZServerHost.sln
```

Project file:

```text
src/CMZServerHost/CMZServerHost.csproj
```

Important project settings:
- Target framework: `net481`
- Platform target: `x86`
- Output path: `build\ServerHost\`
- Harmony copied to `build\ServerHost\libs\0Harmony.dll`

## Running

After building, run:

```bat
src\CMZServerHost\build\ServerHost\CMZServerHost.exe
```

On startup the server prints a summary similar to:

```text
CMZ Server Host
---------------
GameName         : CastleMinerZSteam
NetworkVersion   : 4
Bind             : 0.0.0.0:61903
ServerName       : CMZ Server
MaxPlayers       : 8
SaveOwnerSteamId : 76561198296842857
WorldGuid        : ...
WorldFolder      : Worlds\...
WorldPath        : ...
WorldInfo file   : ...\world.info
World loaded     : True
```

Then connect using the server IP and the configured `server-port`.

## Game binaries and support libraries

### Game folder
The server needs access to the real CastleMiner Z binaries and content.

By default it looks for them under:

```text
build\ServerHost\game\
```

But you can point it somewhere else with `game-path`, for example:

```properties
game-path=C:\Program Files (x86)\Steam\steamapps\common\CastleMiner Z
```

### Harmony
`0Harmony.dll` is expected under:

```text
build\ServerHost\libs\0Harmony.dll
```

It does not need to live next to `CMZServerHost.exe`.

## World and save data

The host derives the world folder from `world-guid` and expects it under:

```text
Worlds\{world-guid}
```

Typical files used by the host include:
- `world.info`
- chunk delta data
- player inventory saves

## Networking notes

The current implementation includes:
- channel 0 / channel 1 packet handling compatible with the reflected game runtime
- direct send and broadcast wrapper handling
- player visibility bootstrap via cached/replayed `PlayerExistsMessage`
- relay of text/chat-style packets and gameplay updates
- server-side pickup resolution for create/request/consume flow
- live connection enumeration to avoid stale connection lists on outbound sends

## Time / day progression

The dedicated host advances world time using **real elapsed time** rather than fixed loop iterations.

This is important because the server loop may run faster or slower than a normal 60 FPS client host. The current implementation keeps authoritative day progression on the server and periodically broadcasts the current world day/time to clients.

## Notes and current implementation details

- The server is built around **reflection** rather than direct game project references.
- The host uses the original game message types and message registry where possible.
- The server update loop is driven by `tick-rate-hz`.
- Pickup, inventory, chunk, and terrain flow are now handled server-side well enough for basic dedicated play.
- The repository includes source plus a ready-to-run `build/ServerHost/` layout.

## Troubleshooting

### The server says the game folder is missing
Make sure `CastleMinerZ.exe` exists either under the default local folder:

```text
build\ServerHost\game\
```

or at the path specified by `game-path`.

### The server says `0Harmony.dll` is missing
Make sure Harmony exists under:

```text
build\ServerHost\libs\0Harmony.dll
```

### Clients cannot join
Check:
- `server-port`
- firewall rules
- that both clients are using the same IP and port
- `game-name` and `network-version`

### The wrong world loads
Check the `world-guid` value in `server.properties` and verify the folder exists under `Worlds\`.

### Save access fails
Check that `steam-user-id` is populated correctly, because the current save-device setup still uses it as the storage identity/key seed.

## License

This project is licensed under **GPL-3.0-or-later**.
See [LICENSE](LICENSE) for details.

## Credits

Developed and maintained by:
- RussDev7
- unknowghost0
