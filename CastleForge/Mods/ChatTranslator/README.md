# ChatTranslator

> Seamless live chat translation for CastleMiner Z.
> Read incoming messages in your own language, reply in another language without leaving the game, and switch between manual or auto-detect workflows on the fly.

---

## Overview

**ChatTranslator** is a focused quality-of-life communication mod for **CastleForge** that intercepts in-game chat, translates it in real time, and rewrites what *you* see locally so conversations stay readable and natural.

It supports two main workflows:

- **Manual mode** — you choose the remote language yourself.
- **Auto mode** — incoming messages are auto-detected, and your replies follow the **last language detected from other players**.

It also includes:

- one-shot translated sends that do **not** change your active mode
- a persisted baseline language and default remote language
- an in-game hotkey to reload the config without restarting
- a translation log writer while the system is active
- lightweight background translation so regular gameplay is not stalled by chat requests

---

### Auto Mode Example
![AutoTranslate](_Images/AutoTranslate.gif)

### Manual Mode Example
![ManualMode](_Images/ManualMode.gif)

### Translation Log Example
![TranslatedLines](_Images/TranslatedLines.png)

---

## Why Use It?

ChatTranslator is built for players who want smoother communication in mixed-language sessions without alt-tabbing to external tools.

### What it does well

- Keeps chat readable in your own language
- Lets you reply in another language with very little effort
- Supports both predictable fixed-language workflows and adaptive auto-detection workflows
- Preserves the original intent of your own messages locally by reconstructing what *you* typed
- Works through chat commands, so there is no bulky UI to manage mid-session

---

## Feature Highlights

### Live incoming translation
Incoming broadcast chat can be translated into your configured **baseline language**, so foreign-language messages become readable in your chat feed.

### Outgoing translation
When translation is active, your outgoing messages are translated before being sent so other players receive the translated version.

### Auto-detect reply flow
In auto mode, ChatTranslator remembers the **last detected language from other players** and uses that language for your outgoing reply path.

### Manual language pair mode
In manual mode, you explicitly set the remote language yourself and keep that language pair active until you change it or disable it.

### One-shot sending tools
You can send a single translated message without changing your current translation mode.

### Config hot reload
The config can be reloaded in-game through a configurable hotkey.

### Translation activity logging
While translation is active, translated chat can be written to a timestamped log file under the mod folder.

### Lightweight async design
The mod queues translation work off-thread and marshals the final chat update back onto the main game thread, helping reduce hitching during normal use.

---

## How It Works

### Baseline language
Your **baseline language** is the language you personally type in and want to read chat in.

Examples:

- If your baseline is `en`, incoming translated chat is shown in English.
- If your baseline is `es`, incoming translated chat is shown in Spanish.

### Manual mode
You choose a remote language such as `es`, `de`, `ru`, or `zh`.

Behavior:

- incoming chat is treated as **remote -> baseline**
- your outgoing chat is translated as **baseline -> remote**
- using `/lang` with the same code again toggles manual mode off
- using manual mode disables auto mode

### Auto mode
You turn auto mode on with `/t` or `/translate`.

Behavior:

- incoming messages are translated with source language auto-detection
- the last detected language from **other players** is remembered
- your outgoing messages are translated from **baseline -> last detected language**
- if there is no valid last detected language yet, your outgoing message stays normal

### Local message reconstruction
When your own translated message comes back through the normal chat path, ChatTranslator attempts to restore the original baseline text locally so your chat feed remains readable to *you*.

That means other players can receive the translated message, while you still see a clean local view with a direction tag such as:

```text
[EN->ES] Hello everyone
```

---

## Installation

### Requirements

- **CastleForge / ModLoader**
- **ModLoaderExtensions**
- CastleMiner Z runtime environment used by your mod setup
- Internet access for the translation requests

### Build / target details

From the project file, this mod targets:

- **.NET Framework 4.8.1**
- **x86**

### Typical install flow

1. Install the CastleForge core components.
2. Make sure `ModLoader` and `ModLoaderExtensions` are available.
3. Place the built `ChatTranslator.dll` into your mods output folder.
4. Launch the game once so the mod can create its config file.
5. Edit the config if desired.
6. Re-enter the game and use the chat commands listed below.

---

## Files Created By The Mod

### Config file
```text
!Mods\ChatTranslator\ChatTranslator.Config.ini
```

### Log folder
```text
!Mods\ChatTranslator\!Logs\
```

### Example log file name
```text
CT_yyyyMMdd_HHmmss_UTC.log
```

---

## Configuration

ChatTranslator uses a simple INI file and will create it automatically if it does not exist.

### Default config

```ini
# ChatTranslator - Configuration
# Use ISO language codes like en, es, de, ru, zh, fr, etc.
# Lines starting with ';' or '#' are comments.

[Languages]
; Your own baseline language (what you type/read in).
BaseLanguage          = en

; Optional default remote language when manual mode is used.
; Leave empty to start with translation off.
DefaultRemoteLanguage = 

[Hotkeys]
; Reload this config while in-game:
ReloadConfig          = Ctrl+Shift+R
```

### Config options

| Setting | Default | What it does |
|---|---:|---|
| `BaseLanguage` | `en` | Your own language for reading chat and for your outgoing source text |
| `DefaultRemoteLanguage` | *(blank)* | Optional manual-mode target language used as the initial remote language |
| `ReloadConfig` | `Ctrl+Shift+R` | Hotkey that reloads the config in-game |

### Notes

- Language values are normalized to lower-case language codes.
- Friendly inputs like `english`, `spanish`, `german`, `russian`, and `chinese` are normalized internally.
- If your baseline language and remote language are set to the same value, manual translation is effectively disabled.

---

## Commands

<details>
<summary><strong>Expand full command reference</strong></summary>

### Core translation controls

| Command | Aliases | Description |
|---|---|---|
| `/translate` | `/t` | Toggle auto-translate mode |
| `/language <code>` | `/lang <code>`, `/l <code>` | Toggle manual translation using a specific language code |
| `/toff` | — | Turn all translation off |
| `/tclear` | `/tc` | Clear both the manual remote language and the last auto-detected language |
| `/tstatus` | — | Print current baseline, manual remote, last detected language, and mode |

### Baseline language

| Command | Aliases | Description |
|---|---|---|
| `/baselang <code>` | `/bl <code>` | Set your baseline language |

### Manual quick toggles

| Command | Description |
|---|---|
| `/es` | Toggle manual Spanish mode |
| `/en` | Toggle manual English mode |
| `/ru` | Toggle manual Russian mode |
| `/zh` | Toggle manual Chinese mode |
| `/de` | Toggle manual German mode |

### One-shot send helpers

| Command | Aliases | Description |
|---|---|---|
| `/sendlang <code> <message...>` | `/sl <code> <message...>` | Send one translated message in a specific language without changing your current mode |
| `/send <message...>` | `/s <message...>` | Send one message in your baseline language without changing your current translation mode |

### Testing

| Command | Description |
|---|---|
| `/ttest <text...>` | Dry-run translation test without sending to chat |

</details>

---

## Recommended Usage Patterns

### 1. Fixed language pair session
Use this when you already know the language the other player is using.

```text
/baselang en
/lang es
```

Result:

- their Spanish chat is shown to you in English
- your English messages are sent in Spanish

### 2. Adaptive mixed-language session
Use this when you do not know what language the other player will use.

```text
/baselang en
/t
```

Result:

- incoming messages are auto-detected and translated into English
- your replies are translated into the **last detected language from another player**

### 3. One-off translated message
Use this when you do not want to change your active mode.

```text
/sendlang de Hello everyone
```

Result:

- a single message is translated to German and sent
- your existing auto/manual mode remains unchanged

### 4. Send one message in your own language
Use this when you are in manual or auto mode but want to send a single message in your baseline language.

```text
/send I'll explain in English
```

Result:

- that one message goes out in your baseline language
- your active translation mode stays unchanged

### 5. Clean reset
Use this when you want to clear the state and start fresh.

```text
/tclear
/toff
```

Result:

- last detected language cleared
- manual remote cleared
- translation fully disabled

---

## What You Will See In Chat

ChatTranslator rewrites local chat display with direction tags so you can tell what happened.

Examples:

```text
[ES->EN] Hola, ¿cómo estás?
[DE->EN] Wir gehen zur Basis.
[EN->ES] Hello everyone
```

These tags help you understand:

- what language the mod believes the source text used
- what language your local display is being translated into
- whether a line was part of your outgoing or incoming flow

---

## Logging

When translation is active, ChatTranslator can create a timestamped live log file under:

```text
!Mods\ChatTranslator\!Logs\
```

The file contains timestamped before/after translations in a readable format.

Example style:

```text
[HH:mm:ss] [ES->EN] PlayerName: "hola" -> "hello"
```

This is useful for:

- debugging translation flow
- reviewing multilingual sessions later
- testing command behavior during development

---

## Hot Reload Support

ChatTranslator includes an in-game hotkey hook that listens during player input and reloads the config on the main thread.

Default binding:

```text
Ctrl+Shift+R
```

After reloading, the mod sends local chat feedback showing that the config was re-applied.

This makes it easy to tweak your baseline language, default remote language, or hotkey without restarting the game.

---

## Technical Notes

<details>
<summary><strong>Expand technical behavior notes</strong></summary>

### Non-blocking translation path
When translation is active, outgoing chat is intercepted, translated off-thread, and then re-sent on the main game thread using a bypass guard so it does not recursively intercept itself.

### Incoming translation hook
Incoming broadcast chat is intercepted before final display so the player sees translated content locally.

### Auto-mode memory
Auto mode does **not** learn from your own outgoing messages. It updates the remembered reply language from **other players' incoming messages**.

### Sent-message cache
The mod stores a small recent cache of your translated outgoing messages so it can reconstruct the original baseline text when the translated message reappears locally.

### Timeout behavior
The translation service uses a short hard timeout and falls back to the original text if translation does not complete in time.

### Endpoint used
The translation service uses the Google Translate web endpoint:

```text
https://translate.googleapis.com/translate_a/single
```

### Translation service behavior
- known-source translation uses `sl=<source>` and `tl=<target>`
- auto mode uses `sl=auto`
- the detected source language is inferred from the response payload

### Leave-game cleanup
When leaving the game, the mod resets translation state and disables its live logger.

</details>

---

## Best Practices

- Set your baseline language first with `/baselang`
- Use manual mode for stable one-language conversations
- Use auto mode when joining mixed-language or unknown-language sessions
- Use `/sendlang` for occasional one-off translations without changing your main mode
- Use `/tstatus` whenever you are unsure what state the translator is in
- Use hot reload after editing the config file instead of restarting the whole game

---

## Troubleshooting

### Translation seems off or inactive
Check:

- whether translation is actually enabled with `/tstatus`
- whether your baseline and remote languages are accidentally the same
- whether auto mode has detected a language yet
- whether the mod has internet access to reach the translation endpoint

### My outgoing messages are not translating in auto mode
Auto mode needs a **last detected language from another player**. If nobody has spoken yet, or detection has not happened yet, there may be nothing valid to translate *to*.

### I want to start over cleanly
Use:

```text
/tclear
/toff
```

### I changed the config but nothing updated
Use the reload hotkey or restart the game.

Default reload hotkey:

```text
Ctrl+Shift+R
```

---

## Developer Notes

### Project structure

```text
ChatTranslator/
├─ ChatTranslator.cs
├─ ChatTranslator.csproj
├─ Core/
│  ├─ ChatTranslationState.cs
│  └─ TranslationService.cs
├─ Logging/
│  └─ CTTranslationLogger.cs
├─ Patching/
│  └─ GamePatches.cs
├─ Startup/
│  ├─ CTConfig.cs
│  └─ CTRuntimeConfig.cs
└─ Embedded/
   ├─ 0Harmony.dll
   ├─ EmbeddedExporter.cs
   └─ EmbeddedResolver.cs
```

### Dependencies observed in the project

- `ModLoader`
- `ModLoaderExtensions`
- `0Harmony`
- CastleMiner Z / DNA runtime assemblies
- `System.Web` for query encoding

### Startup flow

On startup the mod:

1. initializes embedded resource loading
2. applies Harmony patches
3. loads or creates the config
4. registers its chat commands with the command/help system
5. begins pumping queued main-thread translation work during `Tick(...)`

---

## Summary

**ChatTranslator** is a compact but polished communication mod built for multilingual CastleMiner Z sessions. It keeps the interface simple for players while still offering a surprisingly flexible workflow for manual translation, auto-detected translation, one-off translated sends, config hot reloads, and live session logging.

If you want a README that sells the mod honestly, this one’s strength is clear: **it makes mixed-language multiplayer conversations dramatically easier without forcing players out of the game.**