# Nexus-Upload: STBC Voice Crew 1.1.0 (in Arbeit)

Alles, was das Upload-Formular auf nexusmods.com abfragt, zum Kopieren.

## Mod-Seite

- **Name:** STBC Voice Crew
- **Autor:** Phuntasy Productions
- **Version:** 1.1.0
- **Kategorie (Vorschlag):** Utilities (alternativ: Gameplay)
- **Sprache:** English (Mod-Befehle: English, German; First Officer spricht auch Französisch)
- **Summary (Kurzbeschreibung):**
  Voice commands are back! Fully local speech recognition replaces the defunct IBM Watson: Rao, Hoyle and T'nar follow
  spoken orders in English or German within a third of a second – no internet, no GPU, works in multiplayer.
  New in 1.1: an AI First Officer who advises you, knows the missions and takes command when the chair is empty.
- **Beschreibung:** Inhalt von `nexus-description.txt` (BBCode) einfügen.
- **Requirements:** keine (BepInEx 5 ist im Archiv enthalten). Unter „Off-site requirements“ nichts nötig.
- **Adult content:** nein
- **Tags (Vorschlag):** Voice, Quality of Life, Multiplayer, Bots, AI

## Datei

- **Dateiname auf Nexus:** STBC Voice Crew
- **Version:** 1.1.0
- **Kategorie:** Main Files
- **Archiv:** `dist/STBC-Voice-Crew-1.1.0.zip` (mit `pwsh tools/Package.ps1` erzeugen)
- **Dateibeschreibung:** Mod + BepInEx 5.4.23 + offline speech models (English, German) + Piper TTS for the First Officer. Unpack into the game folder.

## Rechte (Permissions)

- Assets anderer Autoren: BepInEx (LGPL-2.1), HarmonyX/MonoMod/Mono.Cecil (MIT), Vosk und Vosk-Modelle (Apache 2.0), Piper (MIT), espeak-ng (GPL-3.0), ONNX Runtime (MIT), Piper-Stimmen (siehe Notices, nur nicht-kommerziell) –
  Hinweise in `THIRD-PARTY-NOTICES.txt` im Archiv.
- Upload auf andere Seiten: nur mit Erlaubnis.
- Änderungen/Verbesserungen: erlaubt, mit Nennung von Phuntasy Productions.
- Umwandlung für andere Spiele: nicht zutreffend.
- Nutzung in kostenpflichtigen Mods: nein.
- Donation Points: nach Wunsch.

## Bilder (fehlen noch)

Titelbild 16:9 (mind. 1280×720) und 3–5 Screenshots, z. B.:
1. Brücke mit eingeblendetem Overlay („Gehört: … → Befehl …“)
2. Crew führt einen Sprachbefehl aus (Schilde hoch, Feuer)
3. Overlay mit Daueranweisung (Schleichfahrt)

## Vor dem Hochladen

- [ ] Frische Testinstallation: Zip in eine saubere Spielkopie (ohne BepInEx) entpacken, Spiel starten,
      Overlay zeigt „Vosk: … bereit“, ein paar Befehle auf Deutsch und Englisch ausprobieren.
- [ ] Spiel einmal auf Englisch umstellen und `BefehlsSprache = spiel` prüfen.
- [ ] Mehrspieler: Host mit Mod, Mitspieler ohne Mod gibt Befehle.
- [ ] Git-Tag `v1.0.0` setzen.
