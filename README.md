# STBC AI Crew

Mod für **Star Trek Bridge Crew**: Die Bot-Stationen (Steuer, Taktik, Technik) werden von KI-Charakteren
besetzt, die ein lokales LLM (llama.cpp) steuert. Jeder Charakter hat Namen, Spezies, Persönlichkeit und
Charakterzüge sowie ein eigenes Gedächtnis. Er sammelt Erfahrung, steigt im Rang auf und entwickelt sich von
Mission zu Mission weiter, auch über Spielsessions hinweg.

## Grundregel: Jeder weiß nur, was er wissen kann

Jeder Offizier, ob Mensch oder KI, kennt nur, was **seine eigene Konsole** anzeigt, was er an Bord spürt
(Alarm, Erschütterungen) und was **auf der Brücke gesagt** wird. Dafür bekommt jeder KI-Offizier einen eigenen
Agenten mit eigenem LLM-Gespräch und eigener Wahrnehmung:

| Station | sieht auf der Konsole |
|---|---|
| Taktik | Schildstärke und -status, Phaser, Torpedovorrat, Meldungen an Taktik |
| Technik | Hülle, Antrieb, Warpkern, Phaser (Integrität), Meldungen an Technik |
| Steuer | Antrieb, Meldungen an die Steuer |
| alle | Meldungen an alle Konsolen, roter Alarm, Erschütterungen, alles Gesprochene |

Was die Captain-Konsole zeigt, sieht kein KI-Offizier. Abstimmen können sich die Offiziere nur, indem sie
miteinander reden („T'nar, ich brauche mehr Energie für die Schilde!“).

## Wie es funktioniert

```
Spiel (Unity 5.4 / Mono)                                 llama-server.exe (eigener Prozess)
┌──────────────────────────────────────────────┐          ┌─────────────────────────────┐
│ Harmony-Hooks ─► Wahrnehmung je Station      │          │ llama.cpp + GGUF-Modell     │
│ ShipStatus (Hülle, Schilde …) ─► je Station  │  HTTP    │ 3 parallele Slots           │
│ OfficerAgent ×3 (Charakter, Gedächtnis) ────►│ ───────► │ (ein Slot je Offizier)      │
│ CrewBrain: Brückenfunk, wer spricht wann     │ ◄─────── │ JSON-Schema erzwingt        │
│ GameBridge ─► CommandManager.TriggerCommand  │          │ gültige Befehle             │
│ Debriefer: Nachbesprechung, XP, Beförderung  │          └─────────────────────────────┘
└──────────────────────────────────────────────┘
```

- **Handeln:** Die Befehle laufen über denselben Einstieg, den früher die IBM-Watson-Sprachsteuerung nutzte.
  Zielen und Kurshalten übernehmen weiter die eingebauten Bots, die KI entscheidet, *was* getan wird.
- **Eigeninitiative wie in den Serien:** Die Offiziere melden Veränderungen unaufgefordert, bestätigen Befehle,
  erledigen Routine selbst, sprechen Kollegen an, widersprechen respektvoll und machen in ruhigen Momenten auch
  mal eine persönliche Bemerkung. Wie viel und wie forsch, hängt von den Charakterzügen ab.
- **Rang regelt die Eigenständigkeit:** Ein Fähnrich erledigt nur Routine selbst und fragt bei weitreichenden
  Schritten (Feuer, Warp, Schilde runter) nach. Lieutenants handeln bei Gefahr selbst, Stabsoffiziere auch ohne Gefahr.
- **Sprechen:** Immer nur einer spricht. Gesprochenes erscheint auf allen Konsolen (in VR sichtbar), bis die
  Sprachausgabe da ist. Die vorgefertigten Bot-Sprachzeilen sind standardmäßig stumm.

## Audiogeräte und Lautstärke: wie das Spiel

Der Mod nutzt dieselben Kanäle wie das Spiel und braucht normalerweise keine eigene Einstellung:

- **Geräte:** Läuft die Oculus-Runtime, nimmt das Spiel (Wwise) das in der Oculus-App gewählte Audiogerät
  (`AkInitializer.OculusAudioOutID` bzw. `OculusAudioInID`). Der Mod liest diese Werte aus und nutzt dasselbe
  Mikrofon und denselben Lautsprecher. Ohne Oculus nehmen beide das Windows-Standardgerät. Ein Wechsel des
  Standardgeräts während des Spiels wird erkannt: beim Lautsprecher mit dem nächsten Satz, beim Mikrofon nach spätestens 5 s.
- **Lautstärke:** Die Crew folgt den Reglern **Master × Sprachchat** im Optionsmenü des Spiels, wie menschliche Mitspieler.
- Die Config-Einträge `[Sprache] Mikrofon` und `[Stimme] Lautsprecher` sind nur für Sonderfälle gedacht und bleiben normalerweise leer.

## Spracheingabe (dauerhaftes Zuhören)

- Das Mikrofon wird direkt über die Windows-Audio-API aufgenommen, nicht über Unity, damit es nicht mit dem
  Sprachchat des Spiels kollidiert. Eine Sprachaktivitätserkennung lernt das Grundrauschen und schneidet Sätze heraus.
  **whisper.cpp** (large-v3-turbo, GPU) erkennt sie in ca. 0,2–0,4 s und braucht ca. 1 GB VRAM.
- **Wer ist gemeint?** Name („Kerrigan, …“), Station („Taktik, …“) oder Fachbegriff („Schilde hoch“ → Taktik,
  „Kurs …“ → Steuer, „Schadensbericht“ → Technik). Ohne Treffer gilt der Satz allen. Die anderen hören mit.
- Typische Whisper-Halluzinationen bei Rauschen („Untertitel im Auftrag des ZDF“, „Vielen Dank.“) werden verworfen.
- **F5** schaltet das Zuhören stumm, etwa wenn du mit jemandem im Raum sprichst.
- Wird die Brille erst später verbunden oder getrennt, findet der Mod das Mikrofon von selbst wieder (alle 5 s).

- Sitzt du nicht auf dem Captain-Stuhl: `[Sprache] MeineStation`. Dann gelten deine Worte nicht als Befehl, sondern als Wort eines Kollegen.

## Sprachausgabe (eigene Stimme je Charakter)

- **Piper** (lokal, CPU, keine GPU-Last) erzeugt einen Satz in ca. 0,4 s. Er wird schon vorbereitet, während
  der vorige Kollege noch spricht. Je Stimme läuft ein dauerhafter piper-Prozess.
- 8 deutsche Stimmen: thorsten, kerstin, karlsson, ramona, pavoque, eva_k, thorsten_emotional (8 Gefühlslagen,
  Brannok spricht „wütend“) und mls mit 236 Sprechern. Neu Angeworbene bekommen automatisch einen eigenen mls-Sprecher.
- **Tempo und Tonhöhe** je Charakter. Die Tonhöhe ändert sich, ohne dass sich das Tempo ändert.
- Dienstgrade werden wie in der deutschen Synchronisation ausgesprochen („Käptn“, „Lutenant“, „Kommander“).
- Wiedergabe über dasselbe Gerät und mit derselben Lautstärke wie das Spiel (siehe oben). Fehlt ein Ausgabegerät,
  spricht die Crew als Text und versucht es beim nächsten Satz erneut.
- **Kein Echo:** Solange die Crew spricht, ignoriert das Mikrofon alles (`MikroTaubWennCrewSpricht`).
  Sonst würde es die Crew aus den Brillenlautsprechern als Captain erkennen.
- Im Crew-Quartier: Stimme, Sprecher, Tempo und Tonhöhe einstellen und **probehören**.

## Charaktere, Erfahrung, Lernen

- Die Charaktere liegen als JSON in `BepInEx\config\StbcAiCrew\crew\`. Beim ersten Start entsteht eine
  Stammbesetzung: Rao, Hoyle und T'nar sowie Zhal sh'Vehra (Andorianerin), Tomas Kerrigan (Veteran), Lira Venn
  (Betazoid, Fähnrich) und Brannok (Tellarit).
- **Charakterzüge** von 0 bis 10: Humor, Mut, Aggressivität, Beredsamkeit, Disziplin, Empathie, Ehrgeiz, Gelassenheit.
- **Nachbesprechung nach jeder Mission**, für jeden Charakter einzeln und nur mit dem, was er selbst erlebt hat:
  Lektionen, persönliche Entwicklung, Eindruck vom Captain und von den Kollegen, ein Eintrag in der Dienstchronik.
  Durch Erlebnisse können sich Charakterzüge um ±1 verschieben.
- **Erfahrungspunkte:** Teilnahme, Dienstzeit, ausgeführte Befehle, umgesetzte Captain-Befehle, Lob (+75 und
  Belobigung) oder Tadel (−25). Ränge: Fähnrich → Lt. j.g. (300) → Lieutenant (800) → Lt. Cmdr. (1600) → Commander (3000).
  Beförderungen werden zu Beginn der nächsten Mission auf der Brücke verkündet.
- **Absturzsicher:** Der laufende Einsatz wird jede Minute gesichert. Nach einem Absturz oder Alt+F4 wird die
  Nachbesprechung beim nächsten Start nachgeholt.

## Humor: Witze und Reaktionen

- In **ruhigen Momenten** (kein Alarm, kein Beschuss seit 90 s, der Captain hat 30 s nichts gesagt) und in der
  **Lobby** (mit Sprachausgabe; die für die nächste Mission eingeteilte Crew) erzählt gern jemand einen Witz.
  Je höher der Humor, desto wahrscheinlicher. Ganz Humorlose erzählen nie welche.
- **Jeder Witz ist neu erfunden.** Für Abwechslung bekommt das LLM nur eine zufällige Form („Treffen sich …“,
  „Wie viele … braucht man …?“, Wortspiel …) und ein Thema (Ferengi, Tribbles, Holodeck …, oft auch die Spezies der
  Anwesenden) als Anstoß. Bereits erzählte Witze werden in `BepInEx\config\StbcAiCrew\witze.json` gespeichert und
  als „nicht wiederholen“ mitgegeben, auch über Sessions hinweg.
- **Reaktionen nach Charakter:** Wer viel Humor hat, lacht hörbar und legt nach. Mittlerer Humor schmunzelt oder
  kommentiert trocken. Wer wenig Humor hat, reagiert genervt oder verständnislos, und wer viel Disziplin hat,
  ermahnt im Dienst den Kollegen. Höchstens zwei reagieren, manche schweigen.
- **Der Captain bestimmt:** „Ruhe!“, „keine Witze“, „konzentriert euch“ usw. stellt die Witze für 15 Minuten ab.
  Ob der Captain Humor mag, lernt die Crew über die Nachbesprechung.
- Erfahrung mit dem 9B-Modell: Die Witze sind eher Kalauer als Pointenfeuerwerk. Ohne JSON-Zwang und bei Temperatur 0,7
  sind sie deutlich besser als mit Formatvorgabe oder Denkmodus. Ein größeres Modell würde hier am meisten bringen.

## Crew-Quartier (F6)

Im Spiel, zum Beispiel im Hauptmenü, als Fenster auf dem Desktop:

- **Besetzung:** Lieblinge fest einer Station zuweisen oder „auto“ (wechselt durch, damit alle Erfahrung sammeln).
- **Crew:** Charaktere bearbeiten (Texte, Stationen, Charakterzüge per Schieberegler) und Gedächtnis ansehen.
- **Anwerben:** Kurzbeschreibung eingeben („nervöser Bolianer, Witzbold, will Steuermann werden“), und das LLM
  erstellt die Personalakte. Neue fangen als Fähnrich an. Alternativ lässt sich ein leerer Charakter von Hand ausfüllen.
- **Entlassen:** Die Datei wandert nach `crew\entlassen\` und lässt sich im Tab „Entlassene“ zurückholen.

## Installation

1. BepInEx 5 (x64) in den Spielordner entpacken (`winhttp.dll` liegt dann neben `stbc.exe`).
2. Plugin bauen (siehe unten), das legt `StbcAiCrew.dll` nach `BepInEx\plugins\StbcAiCrew\`.
3. llama.cpp für Windows (CUDA oder Vulkan) nach `BepInEx\plugins\StbcAiCrew\llama\` entpacken.
4. Ein GGUF-Modell als `BepInEx\plugins\StbcAiCrew\models\crew.gguf` ablegen (getestet: Qwen3.5-9B Q4_K_M,
   ca. 5,6 GB VRAM mit 3 Slots, Antwort aller drei Offiziere parallel in ca. 2–2,5 s).
5. whisper.cpp für Windows (CUDA) nach `BepInEx\plugins\StbcAiCrew\whisper\` entpacken (`whisper\Release\whisper-server.exe`)
   und `ggml-large-v3-turbo-q5_0.bin` daneben legen. Auf RTX-50-Karten dauert der allererste Start ca. 30 s, weil
   der Treiber den GPU-Code übersetzt. Der Mod wärmt das beim Start vor.
6. Piper für Windows nach `BepInEx\plugins\StbcAiCrew\piper\` entpacken (`piper\piper\piper.exe`) und die
   Stimmen (`*.onnx` + `*.onnx.json` von huggingface.co/rhasspy/piper-voices) nach `piper\voices\` legen.
7. Spiel starten. Die Config liegt in `BepInEx\config\de.schuster.stbc.aicrew.cfg`.

## Tasten

| Taste | Funktion |
|---|---|
| F5  | Mikrofon stumm / wieder an |
| F6  | Crew-Quartier |
| F7  | Overlay ein/aus (zeigt nur Gesprochenes, keine Konsolenwerte) |
| F8  | Text statt Sprache an die Brücke (Enter senden) |
| F9  | Mission für beendet erklären (Nachbesprechung) |
| F10 | Zustandsdump des Spielerschiffs (Reverse Engineering) |

**Autonomie** (`[Crew] Autonomie`): `0` = nur auf Ansprache, `1` = meldet sich selbst, handelt aber nur auf Befehl,
`2` = spielt selbst mit (Standard).

## Bauen

```bash
dotnet build src/StbcAiCrew -c Release
```

Spielpfad bei Bedarf überschreiben: `-p:GameDir="D:\...\Star Trek Bridge Crew"`. Kopieren ins Spiel abschalten: `-p:DeployToGame=false`.

## Stand und nächste Schritte

- [x] Ein Agent je Station, Wahrnehmung strikt je Konsole, Brückenfunk
- [x] Charaktere mit Charakterzügen, Gedächtnis, XP, Rängen, Crew-Quartier
- [x] Schiffswerte (Hülle, Schilde, Subsysteme, Torpedos) aus dem Spielcode
- [ ] Im Spiel testen: Hooks, Befehle, Besetzungserkennung (Bot oder Mensch)
- [x] Spracheingabe mit dauerhaftem Zuhören (whisper.cpp), Stations-Routing über Fachbegriffe
- [x] Sprachausgabe mit eigener Stimme, Tempo und Tonhöhe je Charakter (Piper), Echo-Sperre fürs Mikrofon
- [ ] Aussehen und Namensschild der Bots an den Charakter anpassen (Avatar-System des Spiels erschließen)
- [ ] Weitere Konsolenwerte: Ziele/Kontakte für Taktik, Kurs/Geschwindigkeit für die Steuer, Energieverteilung für Technik
- [ ] NPCs bei Rufen (Hailing) per LLM

## Hinweise zum Spielcode

`Assembly-CSharp.dll` ist obfuskiert. Viele Member heißen zufällig (z. B. das Befehls-Enum `AI.BHPPPGKNAHK`),
und es gibt Köder-Methoden mit Unsinnscode. Die echten Einstiegspunkte stehen gesammelt in `GameBridge.cs`,
`GamePatches.cs` und `ShipStatus.cs`. Weil das Spiel keine Updates mehr bekommt, bleiben die Namen stabil.
