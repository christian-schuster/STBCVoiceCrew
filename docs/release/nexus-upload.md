# Nexus upload: STBC Voice Crew 1.1.0

Everything the nexusmods.com upload form asks for, ready to copy.

## Mod page

- **Name:** STBC Voice Crew
- **Author:** Phuntasy Productions
- **Version:** 1.1.0
- **Category (suggestion):** Utilities (alternative: Gameplay)
- **Language:** English (voice commands: English, German, French)
- **Summary:**
  Voice commands are back! Fully local speech recognition replaces the defunct IBM Watson: Rao, Hoyle and T'nar follow
  spoken orders in English, German or French within a third of a second – no internet service, no GPU, works in multiplayer.
  New in 1.1: an AI First Officer who advises you, knows the missions and takes command when the chair is empty.
- **Description:** paste `nexus-description.txt` (BBCode).
- **Requirements:** none (BepInEx 5 is included). Nothing under "Off-site requirements".
- **Adult content:** no
- **Tags (suggestion):** Voice, Quality of Life, Multiplayer, Bots, AI
- **Source code:** link to the GitHub repository.

## File

- **File name on Nexus:** STBC Voice Crew
- **Version:** 1.1.0
- **Category:** Main Files
- **Archive:** `dist/STBC-Voice-Crew-1.1.0.zip` (built with `pwsh tools/Package.ps1`)
- **File description:** Mod + BepInEx 5.4.23. Unpack into the game folder. Speech models and voices are downloaded on
  first use. Alternatively use the setup or the one-line install command from GitHub.

## Permissions

- The mod is open source under the MIT license: modifying, re-uploading and reusing are allowed as long as the
  copyright notice (Phuntasy Productions) and the license text stay included.
- Assets by others: BepInEx (LGPL-2.1), HarmonyX/MonoMod/Mono.Cecil (MIT), Vosk and Vosk models (Apache 2.0), Piper
  (MIT), espeak-ng (GPL-3.0), ONNX Runtime (MIT), Piper voices (see notices, non-commercial only) – details in
  `THIRD-PARTY-NOTICES.txt`.
- Use in paid mods: no (because of the voices).
- Donation points: as you like.

## Images (still missing)

Title image 16:9 (at least 1280×720) and 3–5 screenshots, e.g.:
1. Bridge with the overlay ("Heard: … → Command …")
2. The crew carrying out a voice command (shields up, fire)
3. The First Officer in command (overlay line "First Officer: in command – …")

## Before uploading

- [ ] Fresh test install: run the setup on a clean game copy (without BepInEx), start the game, check the overlay
      shows "Vosk: … ready" after the speech model download, try a few orders in German and English.
- [ ] Switch the game to English and to French once and check `BefehlsSprache = spiel` follows it (French model download, commands).
- [ ] Multiplayer: host with the mod, a player without the mod gives orders.
- [ ] Git tag `v1.1.0`, GitHub release with the three files (see `github-release.md`).
