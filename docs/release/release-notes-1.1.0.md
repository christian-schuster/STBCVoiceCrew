Voice commands for *Star Trek: Bridge Crew* – fully local, no internet service, no language model. This release adds an AI **First Officer** and makes the mod **open source under the MIT license**.

## Install

- **Setup (recommended):** download `STBC-Voice-Crew-Setup.exe` below and run it. It finds the game (Steam or Ubisoft Connect), installs the mod and updates it when you run it again.
- **One command** in PowerShell:
  ```powershell
  powershell -ExecutionPolicy Bypass -c "irm https://github.com/christian-schuster/STBCVoiceCrew/releases/latest/download/install.ps1 | iex"
  ```
- **ZIP:** unpack `STBC-Voice-Crew-1.1.0.zip` into the game folder (the folder with `stbc.exe`). BepInEx 5 is included.

On first start the mod downloads the speech model for your game language (~45 MB) and the First Officer's voice (~85 MB) once.

## New in 1.1.0

**First Officer ("Number One")** – an AI first officer without a language model, always on the bridge and in the lobby.
- Advises you as captain: warnings when shields are down under fire, the hull is critical, a friendly ship is targeted or the warp destination leads away from the mission; hints when the mission stalls.
- Knows the mission flow from rules derived from the game's own mission scripts (campaign and random missions).
- Takes command when the captain's chair is empty – or when you say **"Number One, take the bridge"**: sets warp and impulse destinations, searches the places named in the objectives, scans, fights, beams, and gives orders one at a time, waiting for "Aye".
- Speaks only when nobody else does, now and then with a Star Trek pun. English, German and French, voiced by Piper TTS.
- Talk to him with "Number One", "Commander" or "First Officer": "suggestions?", "dismissed", "tell me a joke", "I have the bridge". F6 turns him on or off.

**Voice commands**
- French voice commands for the bot crew – now English, German and French.

**Open source and installation**
- Source code on GitHub under the MIT license – you are welcome to use the mod or parts of it in your own mods, as long as you credit the origin.
- Three ways to install: setup EXE, one PowerShell command, or the ZIP.
- Much smaller download: speech models and voices are downloaded on first use, only for your game language.
- The overlay shows when a newer version is available.

**Multiplayer**
- A banner on the desktop screen shows who hosts and whether the mod is active there (green: you host, yellow: the host has the mod, red: the host has no mod – no voice commands).
- The game stays compatible with players who don't have the mod.

**Fixes and changes**
- Fixes a black screen on fresh installs: the package now brings the BepInEx setting the game needs, and the mod repairs it itself if it is missing. **Black screen with 1.0.0?** Set `Type = Camera` under `[Preloader.Entrypoint]` in `BepInEx\config\BepInEx.cfg`, or simply install 1.1.0.
- Plugin folder is now `BepInEx\plugins\STBCVoiceCrew`, plugin id and DLL `de.phuntasyproductions.stbcvoicecrew`. Older folders and the 1.0 settings are taken over automatically.
- The Phuntasy Productions logo appears briefly at game start (`[Allgemein] LogoBeimStart = false` turns it off).
- Test mode for unattended runs with the First Officer in command – parts of this release were tested this way with Claude Code.

## 1.0.0

- First public release.
- Offline voice commands (Vosk) in English and German, replacing the defunct IBM Watson service.
- The bot crew (Rao, Hoyle, T'nar) executes orders with their original voices; every human on the bridge may command, multiplayer voice chat is understood on the host.
- Targets by name or from the console of the person giving the order; friendly ships are only fired upon when named.
- Warp and impulse sequences, transporter lowers shields first, standing orders respected by crew reflexes.
- Experimental manoeuvres: avoid hazards, keep distance, circle, flee, stay out of enemy sensor range.

---
Unofficial fan mod. Star Trek is a trademark of CBS Studios Inc.; *Star Trek: Bridge Crew* is a game by Red Storm Entertainment / Ubisoft. Made by [Phuntasy Productions](https://phuntasyproductions.de).
