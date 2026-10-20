# Plan: Watson-Ersatz ohne LLM

Ziel: Die Sprachsteuerung der Bot-Crew so, wie Watson sie damals versprochen hat, aber deutlich besser:
**sofort, zuverlässig, auf Deutsch (und Englisch), lokal, ohne Sprachmodell.** Der Captain sagt, was passieren
soll, und es passiert in unter einer Sekunde. Die Crew bestätigt mit ihren **originalen Spielstimmen**.

Ausgangspunkt: Tag `LLM-Version` (Commit 49e9162). Die LLM-Crew bleibt als eigener Modus erhalten.

---

## 1. Was Watson damals war und warum es nicht mehr geht

Das Spiel hatte eine Sprachsteuerung über IBM Watson (Cloud): Speech-to-Text plus ein
„Conversation“-Dienst, der Absichten (Intents) erkannt hat. Laut Spielcode (`VoiceRecognitionManager`):

| Intent | Parameter | Wirkung |
|---|---|---|
| `set_speed` | quarter, half, max, stop, reverse | Fahrt |
| `fire_weapons`, `cease_fire` | phasers, torpedoes | Feuer |
| `target`, `cease_target` | engines, shields, weapons | Subsystem anvisieren |
| `scan`, `analyze_target` | | Scannen |
| `engage`, `engage_target` | | Angreifen |
| `warp`, `impulse`, `prepare`, `choose_direction` | | Warp/Impuls |
| `power` | offense, defense, balance, engines, shields, phasers | Energie |
| `repair`, `repair_all` | shields, engines, phasers, warpcore, sensors … | Reparatur |
| `red_alert`, `cancel_red_alert` | | Alarm |
| `transport`, `rescue` | | Transporter |
| `answer_hail`, `launch_probe`, `cloak_hunt`, `stealth`, `system_intrusion`, `modulate` | | Sonderfunktionen |

Watson ist abgeschaltet, deshalb funktioniert die Sprachsteuerung im Spiel nicht mehr. Sie war außerdem nur auf
Englisch, brauchte Internet und war langsam: eine Cloud-Runde pro Befehl.

**Wir übernehmen genau diesen Befehlssatz** und führen ihn über `CommandManager.TriggerCommand` aus. Dieser Weg
ist im Mod schon erprobt (Sofort-Befehle, LLM-Version).

---

## 2. Architektur (kein LLM)

```
Mikrofon ──► Sprachende-Erkennung ──► Spracherkennung ──► Befehlsparser ──► Ausführung ──► Rückmeldung
 (Headset)    (VAD, < 300 ms)         (lokal, < 400 ms)    (Regeln +        (CommandManager,  (Original-Bot-
                                                            Fuzzy, < 5 ms)   Ziele, Abläufe)   stimmen, Konsole)
```

### 2.1 Spracherkennung: zwei Wege, wir messen und entscheiden

**A. Whisper (schon vorhanden), auf Befehle getrimmt**
- Kleineres, schnelleres Modell nur für Befehle (z. B. `small`/`base` Deutsch, quantisiert) oder das vorhandene
  `large-v3-turbo` mit kurzem Befehls-Prompt (Wortschatz, Kontaktnamen; ist schon eingebaut).
- whisper.cpp kann per **GBNF-Grammatik** auf erlaubte Sätze eingeschränkt werden. Ob `whisper-server` das
  anbietet, prüfen wir. Sonst schreiben wir einen kleinen eigenen Server mit `whisper.cpp`.

**B. Vosk mit fester Grammatik (empfohlen zu prüfen)**
- Offline, CPU, deutsches Modell ca. 50 MB, Latenz typischerweise unter 200 ms.
- Man gibt die **Liste erlaubter Wörter/Phrasen** vor (Befehle, Stationen, Zahlen, aktuelle Kontaktnamen). Dadurch
  kann die Erkennung praktisch nur noch Befehle verstehen. Zu Halluzinationen wie „Vielen Dank.“, „Hvað er að
  hljóða?“ oder „Flick it los“ kann es so nicht kommen.
- Die Grammatik lässt sich zur Laufzeit ändern, etwa wenn neue Kontakte erscheinen.
- Braucht einen Download (Vosk-Bibliothek und deutsches Modell). **Vorher fragen.**

Entscheidung per Messung (siehe 6.): Erkennungsrate und Latenz auf einem Korpus echter Befehle.

### 2.2 Wann wird zugehört?

Einstellbar, mit Voreinstellung „Stationsname oder Push-to-talk“:
1. **Push-to-talk**: Taste bzw. VR-Controller-Knopf. Am zuverlässigsten, auch in Gesprächen mit Mitspielern.
2. **Ansprache**: Ein Befehl gilt nur, wenn er mit Station oder Name beginnt („Steuer, …“, „Taktik, …“, „Rao, …“,
   „Computer, …“) oder ein eindeutiger Kurzbefehl ist („Roter Alarm!“, „Energie!“). Gespräche mit
   Mitspielern lösen dann nichts aus.
3. **Dauernd** (wie in der LLM-Version), für Einzelspieler.

### 2.3 Befehlsparser: Regeln, Synonyme, Fuzzy-Abgleich

- **Absichten wie bei Watson**, je mit Synonymlisten auf Deutsch und Englisch. Beispiele:
  - „volle Fahrt / volle Kraft / maximum impulse / full speed“ → `SetSpeedFull`
  - „Phaser / feuern / Feuer frei / fire phasers“ → `FirePhasers`
  - „Energie auf Waffen / Offensive / power to weapons“ → `PowerToPhasers`
- **Platzhalter**: Station, Subsystem (Antrieb/Schilde/Waffen …), Geschwindigkeit, **Zielname**, Zahl (Kurs/Grad).
- **Zielnamen per Fuzzy-Abgleich** gegen die aktuelle Kontaktliste. Sie stammt aus der LLM-Version (`Contacts`)
  und ist auf Deutsch lokalisiert. Wir nehmen Levenshtein-Distanz plus **Kölner Phonetik**, dann wird „Rekusa“
  zu „Ragusa“ und „Akusa“ ebenfalls zu „Ragusa“.
- **Mehrere Befehle in einem Satz**: „Schilde hoch, Ziel Ragusa, volle Fahrt!“
- **Verneinung und Aufhebung**: „Nicht feuern“, „Feuer einstellen“, „Alarm aufheben“.
- **Kontext**: „Noch mal“, „auch Torpedos“, „dasselbe auf die Schilde“ beziehen sich auf den letzten Befehl.
- **Bestätigungsschwelle**: Ist ein Befehl zu unsicher, etwa bei schlechter Erkennung oder mehreren ähnlichen
  Zielen, kommt eine kurze Rückfrage über die Original-Stimme bzw. einen Hinweis auf der Konsole. Es wird
  **niemals geraten**.

Alles deterministisch und in Unit-Tests abgedeckt.

### 2.4 Ausführung: Befehle, Ziele, Abläufe

- **Einzelbefehle** direkt über `CommandManager.TriggerCommand`, wie die Sofort-Befehle.
- **Ziel setzen per Sprache** („Ziel Ragusa“, „anvisieren: Bird of Prey“): Konsolenziel der zuständigen Station
  setzen. Den Weg dafür ermitteln wir im Spielcode (`ConsoleShipInterface`). Das ist neu und ein großer Gewinn,
  weil man dann nicht mehr erst auf der Konsole wählen muss.
- **Abläufe als Makros mit Zustandsprüfung**:
  - **Warp**: Ziel auf der Karte → Technik `PrepareWarp` → warten auf „Warpkern geladen“ (Zustand lesen) →
    Fahrt aufnehmen → Steuer `EngageWarp`.
  - **Impuls**: `PrepareImpulse` → warten auf „Impulskurs bereit“ → Fahrt sicherstellen → `EngageImpulse`.
  - **Angriff**: Ziel wählen → Schilde hoch → Waffen scharf → Feuer.
  
  Ein Satz („Warp nach Typos!“) löst die ganze Kette aus, und die Crew meldet die Zwischenschritte.
- **Daueranweisungen** (aus der LLM-Version übernommen), jetzt **per Schlüsselwörtern statt LLM**:
  „Schilde unten lassen“, „nur Viertel Fahrt“, „volle Energie auf die Waffen“, „nur auf Befehl feuern“,
  „unbemerkt bleiben“, „… wieder frei“. Reflexe und Befehle werden dagegen geprüft.
- **Reflexe** (aus der LLM-Version übernommen): Schilde hoch bei Beschuss, Energie auf Schilde, Reparatur des
  schwächsten Systems, immer gebunden an die Daueranweisungen. Konfigurierbar je Station.

### 2.5 Rückmeldung: die Original-Crew spricht

- Das Spiel hat für Rao, Hoyle und T'nar **hunderte professionell vertonte Sätze** in Deutsch und Englisch, etwa
  Bestätigungen, Statusmeldungen und Warnungen. Sie werden über `USSAegisCrew.CrewbotPlayVO` abgespielt. In der
  LLM-Version haben wir sie stummgeschaltet. **Im Watson-Ersatz sind sie die Stimme der Crew.** Das klingt
  besser als jede Sprachsynthese, und für die Antwort ist kein Modell nötig.
- Ausgelöst werden sie, wenn die Bots ihre Befehle ausführen. Wir prüfen im Spielcode, welche Sätze automatisch
  kommen und welche wir gezielt anstoßen müssen, etwa „Aye, Captain“ bei Erkennung.
- Zusätzlich: ein kurzer **Bestätigungston** bei „erkannt“, ein anderer bei „nicht verstanden“, und das
  Erkannte als Text im Overlay und auf der Captain-Konsole.

### 2.6 Mehrspieler

- Wie bisher: Die Mod läuft beim Host. Befehle gibt nur der Captain, wie bei Watson. Optional dürfen menschliche
  Offiziere die Bots ihrer Nachbarstationen ansprechen.
- Die Stimmen der Mitspieler werden über den Sprachchat abgegriffen. Das ist schon gebaut und getestet.
- Rückmeldungen über die Original-Bot-Stimmen hören alle Mitspieler, weil das Spiel sie selbst abspielt.

---

## 3. Was aus der LLM-Version bleibt und was wegfällt

| Bleibt (wiederverwenden) | Fällt weg bzw. optional |
|---|---|
| Spracheingabe, Mikrofon-Fix, Mehrspieler-Abgriff | llama-server, Prompts, Charaktere mit KI-Persönlichkeit |
| Sofort-Befehle (Wortlisten) → wird zum Parser ausgebaut | LLM-Daueranweisungen → Schlüsselwörter |
| Kontakte/Ziele je Konsole, deutsche Lokalisierung | Qwen-/Piper-TTS (Original-Stimmen stattdessen) |
| Reflexe und Daueranweisungen (Logik) | Witze, Anekdoten, Nachbesprechung (LLM-Modus) |
| Host-Erkennung, Hooks, Konfiguration | |

Umsetzung als **Modus-Schalter** `[Allgemein] Modus = watson | llm`: Beide Varianten teilen den Unterbau.
Watson ist die Voreinstellung. Ohne LLM entfallen ca. 6 GB Grafikspeicher und der Server-Start, das Spiel startet
sofort.

---

## 4. Meilensteine

1. **Befehlsparser + Tests** (ohne Spiel): Absichten, Synonyme DE/EN, Platzhalter, Fuzzy-Zielnamen,
   Mehrfachbefehle, Verneinung. Testkorpus aus allen bisherigen Spieltest-Logs (echte, auch verhörte Sätze).
2. **Erkennung messen**: Whisper (Befehls-Prompt, kleineres Modell, ggf. Grammatik) gegen Vosk mit Grammatik.
   Korpus: 100 bis 200 eingesprochene Befehle von Christian (WAV). Kennzahlen: Trefferquote und Latenz.
   Danach entscheiden. Vosk erst nach Zustimmung herunterladen.
3. **Ausführung + Abläufe**: Einzelbefehle, Ziel per Sprache setzen, Warp-/Impuls-/Angriffs-Makros mit
   Zustandsprüfung.
4. **Rückmeldung**: Original-Bot-Stimmen wieder einschalten und gezielt nutzen, Bestätigungstöne, Overlay.
5. **Daueranweisungen + Reflexe** ohne LLM (Schlüsselwörter), konfigurierbar.
6. **Zuhör-Modi**: Push-to-talk (Taste/VR-Knopf), Ansprache per Station, dauernd.
7. **Mehrspieler-Test** mit Abwasser.
8. **Feinschliff**: Synonyme aus Testprotokollen nachziehen, Schwellen justieren, Hilfe-Übersicht der Befehle
   (F1 im Spiel).

## 5. Erfolgskriterien („super funktionieren“)

- Vom Ende des gesprochenen Befehls bis zur Ausführung: **unter 0,8 s** (Ziel 0,5 s).
- **≥ 95 %** richtig erkannte Befehle im Testkorpus, **0 %** falsche Aktionen bei nicht erkannten Sätzen
  (lieber nachfragen als raten).
- Gespräche mit Mitspielern lösen **keine** Befehle aus (Ansprache-/Push-to-talk-Modus).
- Warp und Impuls mit **einem** Satz, ohne Konsolenklicks.
- Läuft ohne LLM, ohne Internet, mit wenig Grafikspeicher.

## 6. Offene Fragen an Christian

1. Zuhör-Modus als Voreinstellung: Push-to-talk, Ansprache per Station oder dauernd?
2. Sollen die Bots die **Original-Charaktere** (Rao, Hoyle, T'nar) bleiben, oder unsere Charaktere mit Namen und
   XP, dann aber ohne eigene Stimme?
3. Vosk für die Befehlserkennung ausprobieren (Download ca. 50 MB)?
4. Sollen menschliche Offiziere im Mehrspieler auch Bots ansprechen dürfen?
