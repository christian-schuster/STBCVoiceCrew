# Backlog

## Release & Verteilung
- **Open-Source-Release auf GitHub** mit **One-Click-Installer (EXE)** zum Download und **Auto-Update über GitHub Releases**.
  - Installer findet den Spielordner selbst (Steam-Bibliotheken), entpackt BepInEx + Mod, legt Konfiguration an.
  - Auto-Update: beim Spielstart (oder im Installer) neueste GitHub-Release-Version prüfen, nur mit Zustimmung laden.
  - Vorher klären: Lizenz der Mod; Stimmen (Piper-Voices auf lessac/ryan-Basis, nur nicht-kommerziell) evtl. nicht
    mitliefern, sondern vom Installer bei huggingface laden lassen; espeak-ng (GPL-3.0) Quellverweis.
- Version 1.1 packen (Piper + Stimmen, Notices, Nexus-Texte) – sobald der First Officer gut genug ist.

## First Officer
- Ungetestet: Scan über den Scanner der Taktik (Reichweite, Fortschritt), Anhalten am Ziel, Warp unter Beschuss.
- Sprachereignisse der Bots (Testlauf-Log „Spielstimme: …“) zuordnen, falls direkte Werte nicht reichen.

## Sprachen
- Französisch: Vosk-Modell, Befehlsregeln, Tests (1.2).
