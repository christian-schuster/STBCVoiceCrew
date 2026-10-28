# STBC Voice Crew

**Voice commands and an AI First Officer for *Star Trek: Bridge Crew* – fully local, no internet service, no LLM.**

IBM shut down Watson, and with it the game's voice commands. This mod brings them back with fast offline speech
recognition (Vosk): the bot crew – Rao (helm), Hoyle (tactical) and T'nar (engineering) – follows your spoken orders
within about a third of a second and answers with their original voices. On top of that, an AI **First Officer**
joins you on the bridge: he advises you as captain, knows the missions, and takes command when the captain's chair is
empty.

**New in 1.1:** the First Officer – and the mod is now open source.

**Focus: single player.** The mod is made first of all for playing alone with the bot crew. It also works in
multiplayer (it runs on the host), and the game stays fully compatible with installations without the mod: players who
don't have it can still join you. In multiplayer a banner on the desktop screen shows who hosts and whether the mod
is active there.

> Unofficial fan mod. Star Trek is a trademark of CBS Studios Inc.; *Star Trek: Bridge Crew* is a game by Red Storm
> Entertainment / Ubisoft. This project is not affiliated with them and contains no game files.

## Features

**Voice commands**
- Supported languages: **English**, **German** and **French**. The command language follows the game's
  language by default and can be set in the config.
- Recognised on the CPU in ~0.3 s (Vosk). No graphics card, no account, no cloud.
- Every human on the bridge may give orders; in multiplayer the host runs the mod and understands the other players
  through the game's voice chat. Players without the mod can still join.
- Targets by name ("Scan the Ragusa") or the target marked on your console; friendly ships are only fired upon when named.
- Sequences: "Warp" / "Impulse" prepare and engage on their own (including turning onto the warp vector and recharging
  the warp coils), "Energize" lowers the shields first.
- Standing orders the crew's reflexes respect ("Hold your fire", "Keep the shields down", "Power down so they can't
  detect us"), experimental manoeuvres ("Fly around the minefield", "Keep ten kilometers distance", "Get us out of here").

**First Officer ("Number One")**
- Always with you – in the lobby and on the bridge. He speaks only when nobody else does, briefly, and now and then
  tells a Star Trek pun.
- While you are captain he **advises**: warnings when something is dangerous or wrong (shields down under fire, hull
  critical, friendly ship targeted, wrong warp destination, leaving before a required scan) and a hint when the mission
  stalls. His mission knowledge comes from rules derived from the objectives and step instructions of the campaign and
  the random missions.
- When the captain's chair is empty – or you say **"Number One, take the bridge"** – he **commands**: reads the
  objectives, sets warp and impulse destinations, searches the places named in the objectives, scans, fights, beams,
  and gives orders to the bot crew and to human officers (spoken and on their console), waiting for "Aye".
- Talk to him with "Number One", "Commander" or "First Officer": "suggestions?", "dismissed", "to the bridge",
  "tell me a joke", "I have the bridge".
- Speaks English, German and French; voices by Piper TTS.

**Fully automated testing with Claude Code**
- A test mode ([Test] AutoTest in the config) starts solo missions on its own – controller selection, main menu,
  briefing room – lets the First Officer take the bridge and play, and writes log lines and screenshots.
- Large parts of this mod were developed and tested this way with [Claude Code](https://claude.com/claude-code):
  it starts the game, watches the runs through the log and the screenshots, fixes what goes wrong and starts the next
  run – without anyone at the keyboard.

## Installation

Requirements: *Star Trek: Bridge Crew* on Windows 10/11 (64 bit, Steam or Ubisoft Connect) and a microphone.
If your microphone's name contains umlauts (ä, ö, ü), rename it in Windows – the game cannot use such devices.

Choose one:

1. **Setup (recommended):** download `STBC-Voice-Crew-Setup.exe` from the
   [latest release](../../releases/latest) and run it. It finds the game (all Steam libraries, Ubisoft Connect, or asks
   for `stbc.exe`), installs the latest version, and updates it when you run it again. "Remove" uninstalls.
2. **One command:** in PowerShell
   ```powershell
   powershell -ExecutionPolicy Bypass -c "irm https://github.com/__REPO__/releases/latest/download/install.ps1 | iex"
   ```
3. **ZIP:** download `STBC-Voice-Crew-<version>.zip` from the [latest release](../../releases/latest) and unpack it into
   the game folder (the folder with `stbc.exe`). It contains BepInEx 5 (mod loader) and the mod.

On first start the mod downloads what it needs for your game language once: the speech model (~45 MB, from
alphacephei.com) and, for the First Officer, Piper and a voice (~25 MB + ~60 MB, from GitHub / Hugging Face).
When a newer version is available, the overlay tells you; close the game and run the setup or the command again.

## Using it

- Short, complete orders work without addressing anyone: "Shields up", "Full stop", "Fire at will".
- Longer orders: address the station first ("Helm, …", "Tactical, …", "Engineering, …", "Crew, …") or hold **F4**.
- Two high beeps: understood. One low beep: not understood.
- Keys: **F4** push to talk, **F5** mute microphone, **F6** First Officer on/off, **F7** overlay (desktop only, not in VR).
  Back to the captain's chair: **Backspace** (the game's own station switch).
- Settings: `BepInEx\config\de.phuntasyproductions.stbcvoicecrew.cfg` (created on first start).
- More in [docs/release/README.txt](docs/release/README.txt).

## Building from source

- .NET SDK 8 or newer (the mod targets .NET 3.5 for the game's Mono, the setup targets .NET Framework 4.8).
- The game's own DLLs are referenced from your installation: copy `GameDir.props.example` to `GameDir.props` and set the
  game folder (or set `STBC_GAME_DIR`). BepInEx 5 must be installed in the game folder.
- `dotnet build src/StbcAiCrew -c Release` builds the mod and copies it into the game.
- `dotnet run --project tests/ParserTests` runs the command parser tests.
- `pwsh tools/Package.ps1 -Repo owner/name` builds the release files (ZIP, setup EXE, install.ps1) in `dist/`.

How it works, in short: Harmony patches hook into the game (console messages, crew commands, voice chat); commands go
through the game's own `CommandManager.TriggerCommand`, like Watson did. The First Officer reads the captain's
objectives, the ship's state and the tactical scanner directly from the running game. See
[docs/DESIGN.md](docs/DESIGN.md).

## License

MIT – see [LICENSE](LICENSE). You are explicitly welcome to use this mod or parts of it in your own mods and projects,
also modified, as long as you credit the origin: keep the copyright notice and the license text, and ideally link back
to this repository. Third-party components keep their own licenses: see
[THIRD-PARTY-NOTICES](docs/release/THIRD-PARTY-NOTICES.txt). The Piper voices are for non-commercial use.

Made by Phuntasy Productions – [phuntasyproductions.de](https://phuntasyproductions.de) – info@phuntasyproductions.de
