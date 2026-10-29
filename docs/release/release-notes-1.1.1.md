Patch release for the **First Officer**: he sets courses (warp and impulse) reliably now. Found and fixed in unattended test runs of the random missions with Claude Code.

## Install / update

- **Setup:** download `STBC-Voice-Crew-Setup.exe` below and run it – it also updates an existing installation.
- **One command** in PowerShell:
  ```powershell
  powershell -ExecutionPolicy Bypass -c "irm https://github.com/christian-schuster/STBCVoiceCrew/releases/latest/download/install.ps1 | iex"
  ```
- **ZIP:** unpack `STBC-Voice-Crew-1.1.1.zip` into the game folder (the folder with `stbc.exe`). BepInEx 5 is included.

Your settings and crew data are kept. The "Source code" archives GitHub adds are not needed to play.

## Fixed in 1.1.1

- **Warp and impulse destinations got lost.** The game cleared them when another target was selected, which ended in "No warp – is a warp destination selected on the map?". The destination is now set again while the jump is prepared.
- **A warp counted as done too early.** Flying at full speed moves the ship more than 5 km, which was taken for a jump – the warp was never engaged. Success is now the actual change of the star system.
- **Warp sequence in cycles:** charge, engage once aligned with the course, retry if nothing happens. Engines get their power back before a jump (power to the shields in a fight starved the impulse drive).
- The First Officer no longer interrupts a jump in progress, and the retry counter starts fresh in every mission.
- **Fights:** only the fight is ordered while it lasts. Course and target steps no longer fought over the target, so the enemy now gets all the phaser fire.
- **Scans:** the same object is no longer scanned over and over; the scan time limit expires as intended.
- **Search for places:** contacts with identical names (for example 31 "Anomaly") are selected one by one; objectives that name a station, anomaly or buoy find the matching contact; after a full round without a find the search starts over; stars are not used as impulse destinations.

## Known limits

- Some impulse jumps are refused by the game for reasons not yet understood (for example inside cosmic storms). The First Officer moves on to the next place.
- Objectives at places without a name on the map (for example "Rift Tempest, Cosmic Storm") can take a long time to find.
- The test runs so far covered the random missions with the USS Aegis; the other ships and the campaign are still to be tested.

---
Unofficial fan mod. Star Trek is a trademark of CBS Studios Inc.; *Star Trek: Bridge Crew* is a game by Red Storm Entertainment / Ubisoft. Made by [Phuntasy Productions](https://phuntasyproductions.de).
