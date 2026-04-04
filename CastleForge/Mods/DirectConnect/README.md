# DirectConnect

> Add a clean, vanilla-feeling **direct IP connection flow** to CastleMiner Z, launch a compatible dedicated server from the menu, and skip the friction of relying entirely on the normal browser flow.

**DirectConnect** is a CastleForge mod designed to make joining compatible **Lidgren-backed CastleMiner Z servers** much easier. It adds new bottom-right menu buttons to the online browser, lets you manually enter an address, remembers the last server you connected to, and includes a built-in cancel flow while joining.

It pairs especially well with a dedicated host setup, but it is documented separately from the dedicated server itself.

---

## Image Placeholder: Preview
![Preview](_Images/Preview.png)

---

## Why use DirectConnect?

CastleMiner Z was not built around a modern “type in an IP and join” experience for custom dedicated hosting. DirectConnect fills that gap by blending a manual connection flow directly into the game’s frontend instead of forcing players to use external tools or awkward workarounds.

### Highlights
- **Connect by IP** from inside the game.
- Supports **`IP`** or **`IP:Port`** input.
- **Remembers your last address** for quick reconnects.
- Adds a **real Cancel button** while joining.
- Can **launch your dedicated server executable** directly from the menu.
- Can **launch a second CastleMiner Z instance** from the same menu.
- Uses a **vanilla-styled UI flow** so it feels native to the game.
- Restores the normal provider when you return to the main menu so regular browsing still works.

---

## Image Placeholder: Direct Connect Flow
![DirectConnect](_Images/DirectConnect.png)

---

## What this mod adds

### 1) A new **Direct Connect** button in the online menu
When the online game screen is opened, DirectConnect injects a new button into the bottom-right corner of the menu.

Selecting it opens a keyboard dialog where you can enter:
- `IP`
- `IP:Port`

If you omit the port, the mod defaults to:
- **`61903`**

### 2) A new **Launch Dedicated** button
This button tries to start a compatible dedicated server executable directly from the frontend.

The mod looks for:
- `CMZServerHost.exe` next to `CastleMinerZ.exe`
- `!Mods\CMZServerHost\CMZServerHost.exe`
- `!Mods\DirectConnect\CMZServerHost.exe`

If it cannot find the executable, it shows a user-facing dialog explaining the expected locations.

### 3) A new **Launch Second CMZ** button
This launches another instance of the currently running CastleMiner Z executable.

This is useful for:
- local testing
- quick multiplayer checks
- host/client workflow debugging
- mod validation against multiple game instances

### 4) Last server memory
DirectConnect stores the last successfully accepted direct-connect address and pre-fills it the next time you open the dialog.

That means quick reconnects without retyping the same address every session.

### 5) A proper **Cancel** button during join
While a join is in progress, the mod adds a dedicated Cancel button to the frontend connecting screen.

You can cancel by:
- clicking **Cancel**
- pressing **Esc**
- pressing controller **B** or **Back**

The cancel flow is designed to unwind cleanly without leaving a half-open join state behind.

---

## Image Placeholder: Connecting Screen
![CancelConnect](_Images/CancelConnect.png)

---

## Feature Breakdown

<details>
<summary><strong>Direct IP Join</strong></summary>

### What it does
DirectConnect creates a direct join path for compatible Lidgren-based sessions by building a synthetic available-session entry and forwarding it into the game’s join pipeline.

### User-facing behavior
- Enter an address in the dialog.
- The mod validates the address.
- The join is forwarded into the frontend using the mod’s direct-connect provider path.

### Input rules
Accepted formats:
- `127.0.0.1`
- `127.0.0.1:61903`

Current parser behavior:
- defaults to port `61903` if omitted
- validates the port range
- currently expects a **numeric IP address**
- does **not** currently resolve hostnames in the active direct-entry parser

So `example.com:61903` is not the intended input format right now, while `192.168.0.15:61903` is.

</details>

<details>
<summary><strong>Vanilla-Styled Menu Integration</strong></summary>

DirectConnect styles its buttons using the game’s existing menu controls so they blend into the normal frontend instead of looking like an external overlay.

The buttons are anchored in the bottom-right and are re-positioned repeatedly so they continue to track screen scaling and resolution changes.

Button order from top to bottom:
1. **Launch Second CMZ**
2. **Launch Dedicated**
3. **Direct Connect**

</details>

<details>
<summary><strong>Launch Dedicated Integration</strong></summary>

This is a convenience feature for players and hosts who regularly test or use a compatible dedicated server executable.

Clicking **Launch Dedicated**:
- searches known deployment paths
- starts `CMZServerHost.exe`
- uses the executable’s own folder as its working directory

This keeps the direct-connect workflow close to the game instead of requiring a separate manual launch step every time.

</details>

<details>
<summary><strong>Launch Second Instance</strong></summary>

This button launches the current CastleMiner Z executable again.

Useful scenarios include:
- testing frontend flows
- verifying client/server behavior on one machine
- checking menu patches quickly
- validating join/cancel behavior without needing another PC

Note that any external single-instance enforcement from Steam or the game environment may still affect whether multiple instances are allowed.

</details>

<details>
<summary><strong>Join Cancel Protection</strong></summary>

The built-in cancel flow is one of the nicest quality-of-life features in this mod.

It is designed to do more than just hide the screen. On cancellation, the mod attempts to:
- stop duplicate cancellation requests
- clear pending world-load continuation state
- re-enable normal message processing
- silently dispose partial join sessions
- avoid bouncing through the game’s normal session-ended UI flow
- return you cleanly to the main menu

It also swallows late join callbacks after cancellation so a delayed result does not drag the player back into the flow they already abandoned.

</details>

<details>
<summary><strong>Provider Swap + Restore</strong></summary>

For the direct-connect path, the mod swaps the game over to a **Lidgren-based network provider** before joining.

When you return to the main menu and there is no active session, DirectConnect restores the original provider so regular browsing behavior can continue normally.

This helps keep the direct-connect functionality isolated to the join flow where it is actually needed.

</details>

<details>
<summary><strong>Address Persistence</strong></summary>

The last entered address is saved to disk and reused later.

Saved file:

```text
!Mods\DirectConnect\LastServerAddress.txt
```

This is especially useful if you reconnect to the same host often during testing or normal play.

</details>

---

## How to use it

### Joining a server directly
1. Open the game.
2. Navigate to the online game browser screen.
3. Click **Direct Connect**.
4. Enter a valid address such as:
   - `127.0.0.1`
   - `127.0.0.1:61903`
5. Confirm the dialog.
6. Let the game continue through the join pipeline.

### Launching a dedicated server from the menu
1. Place `CMZServerHost.exe` in one of the supported locations.
2. Open the online game browser.
3. Click **Launch Dedicated**.
4. Wait for the server to start.
5. Use **Direct Connect** to join it.

### Launching a second CMZ instance
1. Open the online game browser.
2. Click **Launch Second CMZ**.
3. Use the second instance for local testing or side-by-side validation.

---

## Image Placeholder: Local Test Workflow
![LocalTest](_Images/LocalTest.png)

---

## Installation

### Requirements
- CastleForge / ModLoader installed
- CastleMiner Z
- A build of **DirectConnect.dll**
- A compatible direct-connect target such as a **Lidgren-backed dedicated server**

### Basic install
Place the mod DLL in your mods output/load path.

Typical CastleForge-style layout:

```text
!Mods\DirectConnect.dll
```

The mod also creates and uses its own support folder:

```text
!Mods\DirectConnect\
```

That folder is used for saved direct-connect data such as the last entered address.

### Embedded dependency handling
The project embeds Harmony and can extract embedded resources into the DirectConnect mod folder when needed.

---

## Project layout

```text
CastleForge/
└─ CastleForge/
   └─ Mods/
      └─ DirectConnect/
         ├─ README.md
         ├─ DirectConnect.cs
         ├─ DirectConnect.csproj
         ├─ Embedded/
         │  ├─ 0Harmony.dll
         │  ├─ EmbeddedExporter.cs
         │  └─ EmbeddedResolver.cs
         ├─ Patching/
         │  └─ GamePatches.cs
         └─ Properties/
            └─ AssemblyInfo.cs
```

---

## Configuration

DirectConnect currently does **not** expose a normal end-user config file with gameplay settings or toggles.

Instead, it is primarily behavior-driven:
- menu button injection
- direct-connect dialog flow
- saved last-address persistence
- optional dedicated EXE launch support

### Persistent data written by the mod

```text
!Mods\DirectConnect\LastServerAddress.txt
```

That file stores the last address entered through the direct connect dialog.

---

## Image Placeholder: Saved Address Example
![SavedAddress](_Images/SavedAddress.png)

---

## Compatibility Notes

DirectConnect is built specifically around a **Lidgren-based direct-connect flow** for CastleMiner Z.

It is best suited for:
- compatible dedicated server hosting setups
- direct-IP local testing
- development workflows
- players who want manual host entry without relying on the default browser flow alone

### Important notes
- The active direct-entry parser currently expects a **numeric IP address**.
- If no port is specified, DirectConnect uses **61903**.
- The dedicated server launcher expects the executable to be named exactly:

```text
CMZServerHost.exe
```

---

## Troubleshooting

<details>
<summary><strong>I clicked Direct Connect and it says the address is invalid</strong></summary>

Use one of these formats:
- `127.0.0.1`
- `127.0.0.1:61903`

Right now, the active parser is intended for numeric IP input.

</details>

<details>
<summary><strong>The Launch Dedicated button cannot find my server</strong></summary>

Make sure the file is named exactly:

```text
CMZServerHost.exe
```

Supported search locations:
- next to `CastleMinerZ.exe`
- `!Mods\CMZServerHost\CMZServerHost.exe`
- `!Mods\DirectConnect\CMZServerHost.exe`

</details>

<details>
<summary><strong>My last server is not being remembered</strong></summary>

Check whether this file exists and is writable:

```text
!Mods\DirectConnect\LastServerAddress.txt
```

The address is saved after the dialog accepts a valid entry.

</details>

<details>
<summary><strong>I canceled a join and want normal browsing back</strong></summary>

The mod is designed to restore the original provider when returning to the main menu and there is no active network session.

If you are troubleshooting provider-related behavior, fully returning to the frontend is the intended reset path.

</details>

<details>
<summary><strong>The buttons do not appear where I expected</strong></summary>

The custom buttons are injected into the **online game selection screen** and anchored to the **bottom-right** of the screen.

Their placement is recalculated repeatedly so they stay aligned with resolution and UI scaling changes.

</details>

---

## Technical Overview

<details>
<summary><strong>Implementation Notes</strong></summary>

### Core behavior
DirectConnect is a CastleForge mod that:
- initializes through `ModBase`
- loads embedded dependencies through `EmbeddedResolver`
- applies Harmony patches from a central `GamePatches` container
- extracts embedded resources into `!Mods\DirectConnect` when needed

### UI patches
The mod patches the online screen to:
- inject custom buttons on push
- keep them positioned during update
- shut down active discovery objects when the screen is popped

### Join path
The active direct-connect flow:
- parses the user-entered address
- builds a synthetic `AvailableNetworkSession`
- switches to a `LidgrenNetworkSessionStaticProvider`
- calls the game’s private `FrontEnd.JoinGame(...)` path through reflection

### Cancel flow
The connecting-screen cancel system:
- arms when join begins
- draws a manual cancel button
- intercepts mouse, keyboard, and controller cancel input
- silently disposes partial sessions
- swallows late callbacks after cancel

### Additional internal support
The project also includes reusable discovery/helper plumbing for host lookup and a password prompt path for protected discovered sessions, even though the active direct-entry flow currently joins directly after address parsing.

### Build details
Current project metadata indicates:
- target framework: **.NET Framework 4.8.1**
- platform target: **x86**
- assembly name: **DirectConnect**
- mod version in constructor: **0.0.1**

</details>

---

## Technical Diagrams
```mermaid
flowchart LR
    A[Load Config + Paths] --> B[Load Game Assemblies + Harmony]
    B --> C[Start Lidgren NetPeer]
    C --> D[Create Host Gamer + Init World Handler]
    D --> E[Process Discovery Approval Status Data]
    E --> F[Route Host-Only World Logic]
    F --> G[Relay Client Packets]
    G --> H[Broadcast Time-of-Day + Maintain Session]
```

---

<details>
<summary><strong>1) Full server lifecycle overview</strong></summary>

```mermaid
flowchart TD
    A[Launch CMZServerHost.exe] --> B[Program.Main]
    B --> C[Load server.properties]
    C --> D[Resolve baseDir gamePath libsPath]
    D --> E{Required files present?}
    E -- No --> F[Print error and exit]
    E -- Yes --> G[Register AssemblyResolve]
    G --> H[Load CastleMinerZ.exe]
    H --> I[Load DNA.Common.dll if present]
    I --> J[Apply Harmony server patches]
    J --> K[Print startup summary]
    K --> L[Construct LidgrenServer]
    L --> M[Start server]

    M --> N[Load DNA.Common + XNA]
    N --> O[Create NetPeerConfiguration]
    O --> P[Enable DiscoveryRequest]
    P --> Q[Enable ConnectionApproval]
    Q --> R[Enable StatusChanged]
    R --> S[Enable Data]
    S --> T[Create and start NetPeer]
    T --> U[Create synthetic host gamer]
    U --> V{World handler configured?}
    V -- Yes --> W[Init ServerWorldHandler]
    W --> X[Build message registry]
    X --> Y[Init save device]
    Y --> Z[Load world.info]
    Z --> AA[Read spawn hint]
    V -- No --> AB[Skip world init]
    AA --> AC[Create CmzMessageCodec]
    AB --> AC
    AC --> AD[Enter update loop]

    AD --> AE[Read incoming Lidgren messages]
    AE --> AF{Message type?}
    AF -- DiscoveryRequest --> AG[HandleDiscoveryRequest]
    AF -- ConnectionApproval --> AH[HandleConnectionApproval]
    AF -- StatusChanged --> AI[HandleStatusChanged]
    AF -- Data --> AJ[HandleDataMessage]

    AG --> AK[Recycle message]
    AH --> AK
    AI --> AK
    AJ --> AK
    AK --> AL[Advance time of day]
    AL --> AM{5 seconds elapsed?}
    AM -- Yes --> AN[Build time payload]
    AN --> AO[Broadcast time-of-day to clients]
    AM -- No --> AP[Continue loop]
    AO --> AP
    AP --> AQ{Ctrl+C / stop requested?}
    AQ -- No --> AD
    AQ -- Yes --> AR[Shutdown NetPeer]
    AR --> AS[Server stopped]
```

</details>

---

<details>
<summary><strong>2) Startup / bootstrap detail</strong></summary>

```mermaid
flowchart TD
    A[Executable starts] --> B[Load config from server root]
    B --> C[Resolve game path]
    C --> D[Resolve libs path]
    D --> E{CastleMinerZ.exe exists?}
    E -- No --> F[Exit code 2]
    E -- Yes --> G{0Harmony.dll exists?}
    G -- No --> H[Exit code 3]
    G -- Yes --> I[Hook AppDomain.AssemblyResolve]

    I --> J[Probe libs folder first]
    I --> K[Probe game DLLs]
    I --> L[Probe game EXEs]

    J --> M[Load game assembly]
    K --> M
    L --> M

    M --> N[Apply ServerPatches]
    N --> O[Construct LidgrenServer with config values]
    O --> P[Load DNA.Common.dll]
    P --> Q[Load Microsoft.Xna.Framework from GAC]
    Q --> R[Create NetPeerConfiguration]
    R --> S[Set bind address port max players game name]
    S --> T[Enable discovery approval status data]
    T --> U[Create NetPeer]
    U --> V[Start NetPeer]
    V --> W[Create synthetic host gamer]
    W --> X[Init ServerWorldHandler]
    X --> Y[Register reflected assemblies]
    Y --> Z[Force DNA.Net.Message static registry]
    Z --> AA[Map message IDs to reflected types]
    AA --> AB[Init save device]
    AB --> AC[Load world.info]
    AC --> AD[Read spawn hint]
    AD --> AE[Create CmzMessageCodec]
    AE --> AF[Server ready]
```

</details>

---

<details>
<summary><strong>3) Connection / join flow</strong></summary>

```mermaid
flowchart TD
    A[Client sends DiscoveryRequest] --> B[Server handles discovery]
    B --> C[Reply with session/server metadata]

    C --> D[Client attempts connection]
    D --> E[ConnectionApproval received]
    E --> F[Read join payload / gamer object]
    F --> G{Gamertag is 'unknow ghost'?}
    G -- Yes --> H[Rename to Player]
    G -- No --> I[Keep existing gamertag]
    H --> J[Attach gamer to connection Tag]
    I --> J
    J --> K[Approve connection]

    K --> L[StatusChanged = Connected]
    L --> M[Create reflected NetworkGamer for remote player]
    M --> N[Assign next player GID]
    N --> O[Build ConnectedMessage]
    O --> P[Peer list = host + existing remotes]
    P --> Q[Send ConnectedMessage to joiner]
    Q --> R[Add remote gamer to runtime collections]
    R --> S[Send server info / bootstrap messages]
    S --> T[Send time-of-day bootstrap]
    T --> U[Broadcast NewPeer to existing clients]
    U --> V[Client joins live session]
```

</details>

---

<details>
<summary><strong>4) Data packet routing flow</strong></summary>

```mermaid
flowchart TD
    A[Incoming Data message] --> B{Sequence channel?}

    B -- Channel 1 --> C[Read wrapper opcode]
    C --> D{Opcode?}

    D -- 3 direct proxy --> E[Read recipientId senderId payload]
    E --> F[Find target connection by player ID]
    F --> G[Forward wrapped payload as channel 0 packet]

    D -- 4 broadcast wrapper --> H[Read senderId payload]
    H --> I[TryApplyIncomingTimeOfDay]
    I --> J{PlayerExistsMessage?}
    J -- Yes --> K[Cache PlayerExists payload by sender]
    J -- No --> L[Continue]
    K --> L
    L --> M{World handler consumes host-only message?}
    M -- Yes --> N[Stop relay]
    M -- No --> O{PlayerExists requestResponse?}
    O -- Yes --> P[Replay cached PlayerExists to joiner]
    O -- No --> Q[Relay original payload to all peers except sender]
    P --> Q

    B -- Channel 0 --> R[Read recipientId senderId payload]
    R --> S{recipientId == 0?}
    S -- Yes --> T[Offer to ServerWorldHandler first]
    T --> U{Consumed by host/world logic?}
    U -- Yes --> V[Stop relay]
    U -- No --> W[Fallback relay]
    S -- No --> W

    W --> X[Send to recipient or broadcast peers]
    G --> Y[Done]
    N --> Y
    Q --> Y
    V --> Y
    X --> Y
```

</details>

---

<details>
<summary><strong>5) World-authoritative host flow</strong></summary>

```mermaid
flowchart TD
    A[Host-directed gameplay payload arrives] --> B[ServerWorldHandler.TryHandleHostMessage]
    B --> C{Payload type?}

    C -- world info / bootstrap --> D[Build or send world bootstrap data]
    C -- chunk list / chunk request --> E[Load chunk delta / build chunk response]
    C -- terrain mutation --> F[Apply world change and persist]
    C -- inventory flow --> G[Load save or persist inventory]
    C -- pickup request/create/consume --> H[Resolve pickup server-side]
    C -- time-of-day --> I[Build authoritative time payload]
    C -- unknown / unhandled --> J[Return false to caller]

    D --> K[Send response back through host]
    E --> K
    F --> K
    G --> K
    H --> K
    I --> K
    K --> L[Caller stops normal relay]
    J --> M[Caller falls back to standard relay]
```

</details>

---

<details>
<summary><strong>6) Disconnect flow</strong></summary>

```mermaid
flowchart TD
    A[StatusChanged = Disconnected] --> B[Find connection and gamer]
    B --> C[Get disconnected player GID]
    C --> D[Build DropPeerMessage]
    D --> E[Send DropPeer to remaining clients]
    E --> F[Remove connection -> gamer mapping]
    F --> G[Remove gamer from active list]
    G --> H[Remove cached PlayerExists payload]
    H --> I[Notify ServerWorldHandler.OnClientDisconnected]
    I --> J{No players left?}
    J -- Yes --> K[Reset next player GID to 1]
    J -- No --> L[Keep current state]
```

</details>

---

## Best Use Cases

- **Joining a private dedicated host by IP**
- **Testing your server locally without relying on Steam browsing**
- **Spinning up a host and client from one machine**
- **Faster reconnect loops during development**
- **Cleaner frontend flow for custom CastleMiner Z networking setups**

---

## Pairing With Other CastleForge Components

DirectConnect is especially useful alongside:
- **CMZServerHost** for dedicated hosting
- your broader **CastleForge** mod ecosystem
- local testing workflows for networking-related mods

It acts as the in-game bridge between the player-facing frontend and a manual direct-IP join workflow.

---

## Status Snapshot

### User-facing features included
- Direct Connect button
- Launch Dedicated button
- Launch Second CMZ button
- Last server address memory
- Connecting-screen Cancel button
- Provider restore on return to main menu
- Vanilla-style placement and look

### End-user config
- No traditional config file at this time

### Saved data
- `!Mods\DirectConnect\LastServerAddress.txt`

---

## License

This project is licensed under **GPL-3.0-or-later**.
See the repository license for full details.

---

## Credits

Developed and maintained by **RussDev7** as part of the **CastleForge** ecosystem.