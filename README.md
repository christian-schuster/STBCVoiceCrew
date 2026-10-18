# STBC AI Crew

Mod für **Star Trek Bridge Crew**: Die Bot-Offiziere (Rao an der Steuer, Hoyle an Taktik, T'nar an Technik)
werden von einem lokalen LLM (llama.cpp) gesteuert. Sie hören auf den Captain, melden sich selbst zu Wort,
handeln eigenständig und lernen über ein Langzeitgedächtnis aus jeder Mission.

## Wie es funktioniert

```
Spiel (Unity 5.4 / Mono)                          llama-server.exe (eigener Prozess)
┌───────────────────────────────────────┐          ┌──────────────────────────┐
│ BepInEx-Plugin StbcAiCrew             │  HTTP    │ llama.cpp + GGUF-Modell  │
│  Harmony-Hooks ──► EventLog           │ ───────► │ JSON-Schema erzwingt     │
│  CrewBrain: Prompt, Entscheidung      │ ◄─────── │ gültige Befehle          │
│  GameBridge ──► CommandManager.       │          └──────────────────────────┘
│                 TriggerCommand(...)   │
└───────────────────────────────────────┘
```

- **Wahrnehmung:** Hooks auf Konsolenmeldungen (`Bridge.SendConsoleMessage`), Befehle des Captains
  (`CommandManager.TriggerCommand`), roten Alarm und Treffer am Schiff.
- **Handeln:** Die Befehle laufen über denselben Einstieg, den früher die IBM-Watson-Sprachsteuerung nutzte.
  Zielen und Kurshalten übernehmen weiter die eingebauten Bots, das LLM entscheidet, *was* getan wird.
- **Sprechen:** Funksprüche erscheinen auf der Captain-Konsole (in VR sichtbar) und im Overlay.
- **Lernen:** Nach jeder Mission läuft eine Nachbesprechung, in der das LLM seine Lektionen und ein
  Profil des Captains in `BepInEx\config\StbcAiCrew\gedaechtnis.json` fortschreibt. Das passiert ohne Training
  der Modellgewichte, die Erfahrung fließt in jede spätere Entscheidung ein.
- **llama.cpp** läuft als Kindprozess, den der Mod startet und beim Beenden mitnimmt (Windows-Job-Objekt).
  Stürzt der LLM-Server ab, läuft das Spiel weiter.

## Installation

1. BepInEx 5 (x64) in den Spielordner entpacken (`winhttp.dll` liegt dann neben `stbc.exe`).
2. Plugin bauen (siehe unten), das legt `StbcAiCrew.dll` nach `BepInEx\plugins\StbcAiCrew\`.
3. llama.cpp für Windows (CUDA oder Vulkan) nach `BepInEx\plugins\StbcAiCrew\llama\` entpacken,
   sodass dort `llama-server.exe` liegt.
4. Ein GGUF-Modell als `BepInEx\plugins\StbcAiCrew\models\crew.gguf` ablegen (getestet wird mit Qwen3.5-9B Q4_K_M).
5. Spiel starten. Beim ersten Start legt BepInEx `BepInEx\config\de.schuster.stbc.aicrew.cfg` an.

## Bedienung

| Taste | Funktion |
|---|---|
| F8  | Text an die Crew (Enter senden, Esc abbrechen) |
| F7  | Overlay ein/aus |
| F9  | Mission für beendet erklären und Nachbesprechung starten |
| F10 | Zustandsdump des Spielerschiffs (für Reverse Engineering) |

**Autonomie** (`[Crew] Autonomie` in der Config): `0` = nur auf Befehl, `1` = meldet sich selbst,
handelt aber nur auf Befehl, `2` = spielt selbst mit (Standard).

Stationen, die ein Mensch bedient, steuert die KI nie. Dort macht sie nur Vorschläge.

## Bauen

```bash
dotnet build src/StbcAiCrew -c Release
```

Spielpfad bei Bedarf überschreiben: `-p:GameDir="D:\...\Star Trek Bridge Crew"`. Kopieren ins Spiel abschalten: `-p:DeployToGame=false`.

## Stand und nächste Schritte

- [x] Plugin-Grundgerüst, llama-server-Host, Crew-Gehirn, Gedächtnis
- [ ] Im Spiel testen: Laden die Hooks? Kommen Befehle bei den Bots an?
- [ ] Spielzustand (Hülle, Schilde, Energie, Ziele) über F10-Dumps finden und in den Prompt geben
- [ ] Spracheingabe (whisper.cpp) statt F8-Texteingabe, damit es in VR funktioniert
- [ ] Sprachausgabe (TTS) für die Offiziere
- [ ] NPCs bei Rufen (Hailing) per LLM

## Hinweise zum Spielcode

`Assembly-CSharp.dll` ist obfuskiert. Viele Member heißen zufällig (z. B. das Befehls-Enum `AI.BHPPPGKNAHK`),
und es gibt Köder-Methoden mit Unsinnscode. Die echten Einstiegspunkte stehen gesammelt in `GameBridge.cs` und `GamePatches.cs`.
Weil das Spiel keine Updates mehr bekommt, bleiben die Namen stabil.
