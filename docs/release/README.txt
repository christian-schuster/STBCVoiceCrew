STBC Voice Crew – voice commands for Star Trek: Bridge Crew
by Phuntasy Productions – https://phuntasyproductions.de
=============================================================
(Deutsch weiter unten)

Replaces the old IBM Watson voice commands (no longer available) with fast, fully local speech
recognition. The bot crew – Rao (helm), Hoyle (tactical) and T'nar (engineering) – follows your spoken
orders and answers with their original voices. No internet, no account, no graphics card needed.

FEATURES
- Commands in English or German, recognised in about 0.3 seconds on the CPU (Vosk).
- Every human on the bridge can give orders (multiplayer: the host runs the mod and hears the others
  through the game's voice chat).
- Targets by name: "Scan the Ragusa", "Take us to the Nicholson". The target you marked on your own
  console is used for "Fire!", "Scan", "Beam the survivors aboard" …
- Sequences: "Warp" / "Impulse" prepare and engage on their own, "Energize" lowers the shields first.
- Standing orders: "Power down all systems so they can't detect us", "Hold your fire", "Only fire on my order",
  "Keep the shields down" – reflexes (shields up under fire etc.) respect them. "As you were" lifts them.
- Experimental manoeuvres: "Fly around the minefield", "Keep ten kilometers distance from the enemy",
  "Circle the station", "Stay out of their sensor range and approach the marked target", "Get us out of here".

REQUIREMENTS
- Star Trek: Bridge Crew on Windows (64 bit), Steam or Ubisoft Connect.
- A microphone. Rename it in Windows if its name contains umlauts (ä, ö, ü) – the game cannot use such devices.

INSTALLATION
1. Unpack the archive into the game folder (the folder that contains stbc.exe).
   The archive contains BepInEx 5.4.23 (mod loader). If you already use BepInEx 5, copying the folder
   BepInEx\plugins\StbcAiCrew is enough.
2. Start the game as usual. The overlay in the top left corner shows "Vosk: de bereit" / "en bereit".
3. Uninstall: delete BepInEx\plugins\StbcAiCrew (and, if no other mods use it, winhttp.dll,
   doorstop_config.ini, .doorstop_version and the BepInEx folder).

HOW TO SPEAK TO THE CREW
- Short, complete orders work without addressing anyone: "Shields up", "Full stop", "Fire at will".
- Longer orders or conversation: address the station first – "Helm, …", "Rao, …", "Tactical, …",
  "Engineering, …", "Crew, …" – or hold F4 (push to talk) while speaking.
- Two high beeps: understood. One low beep: not understood.
- Keys: F4 push to talk, F5 mute/unmute microphone, F7 overlay on/off.

EXAMPLES
Helm:        full speed / half impulse / all stop / reverse / set a course for the Tallinn / approach the target /
             go to impulse / warp / engage / make it so
Tactical:    shields up / lower the shields / red alert / fire at will / fire torpedoes / arm the torpedoes /
             cease fire / target their engines / disrupt their engines / scan the Ragusa / attack
Engineering: power to shields / all power to the engines / weapons to maximum / repair the shields /
             beam the survivors aboard / energize / prepare for warp

SETTINGS (BepInEx\config\de.phuntasyproductions.stbc.voicecrew.cfg, created on first start)
- [Sprache] BefehlsSprache = spiel | de | en | auto   (spiel = same as the game language, auto = both at once)
- [Sprache] ZuhoerModus = beides | ansprache | ptt | dauernd
- [Tasten] PushToTalk = F4
- [Watson] Bestaetigungstoene = true
- [Crew] Reflexe = true

KNOWN LIMITATIONS
- Warp and impulse destinations must still be selected on the navigation map.
- Answering hails, launching probes and cancelling red alert only work at the captain's console.
- Manoeuvres steer the bot helm directly and are experimental.

This is an unofficial fan mod. Star Trek is a trademark of CBS Studios Inc.; Star Trek: Bridge Crew is a game
by Red Storm Entertainment / Ubisoft. Neither is affiliated with this mod.


-----------------------------------------------------------------------------------------------------------
DEUTSCH
-----------------------------------------------------------------------------------------------------------

Ersetzt die abgeschaltete IBM-Watson-Sprachsteuerung durch eine schnelle, komplett lokale Spracherkennung.
Die Bot-Crew – Rao (Steuer), Hoyle (Taktik) und T'nar (Technik) – befolgt gesprochene Befehle und antwortet mit
ihren Originalstimmen. Kein Internet, kein Konto, keine Grafikkarte nötig.

FUNKTIONEN
- Befehle auf Deutsch oder Englisch, erkannt in etwa 0,3 Sekunden auf der CPU (Vosk).
- Jeder Mensch auf der Brücke darf Befehle geben (Mehrspieler: Der Host hat die Mod und hört die anderen über
  den Sprachchat des Spiels).
- Ziele per Name: „Scanne die Ragusa“, „Fliege zur Nicholson“. Für „Feuer!“, „Scannen“, „Beamen“ … gilt das Ziel,
  das du an deiner eigenen Konsole markiert hast.
- Abläufe: „Warp“ / „Impuls“ bereiten vor und lösen selbst aus, „Beamen“ senkt vorher die Schilde.
- Daueranweisungen: „Fahre alle Systeme auf Minimum, damit wir nicht entdeckt werden“, „Nicht feuern“,
  „Feuert erst, wenn ich es sage“, „Lass die Schilde unten“ – Reflexe (Schilde hoch unter Beschuss usw.) halten
  sich daran. „Normale Fahrt“ hebt sie auf.
- Manöver (experimentell): „Umfliege das Minenfeld“, „Halte zehn Kilometer Abstand zum Feind“,
  „Kreise um die Station“, „Bleib außerhalb der gegnerischen Sensoren und nähere dich dem markierten Ziel“,
  „Bring uns hier raus“.

VORAUSSETZUNGEN
- Star Trek: Bridge Crew unter Windows (64 Bit), Steam oder Ubisoft Connect.
- Ein Mikrofon. Enthält sein Name Umlaute (ä, ö, ü), in Windows umbenennen – das Spiel kann solche Geräte nicht nutzen.

INSTALLATION
1. Archiv in den Spielordner entpacken (der Ordner mit stbc.exe).
   Enthalten ist BepInEx 5.4.23 (Mod-Lader). Wer BepInEx 5 schon nutzt, kopiert nur den Ordner
   BepInEx\plugins\StbcAiCrew.
2. Spiel wie gewohnt starten. Oben links zeigt das Overlay „Vosk: de bereit“.
3. Entfernen: BepInEx\plugins\StbcAiCrew löschen (und, falls keine anderen Mods es brauchen, winhttp.dll,
   doorstop_config.ini, .doorstop_version und den Ordner BepInEx).

SO SPRICHST DU MIT DER CREW
- Kurze, vollständige Befehle gehen ohne Anrede: „Schilde hoch“, „Voller Stopp“, „Feuer frei“.
- Längere Befehle: Station ansprechen – „Steuer, …“, „Rao, …“, „Taktik, …“, „Technik, …“, „Crew, …“ – oder beim
  Sprechen F4 halten (Push-to-talk).
- Zwei hohe Töne: verstanden. Ein tiefer Ton: nicht verstanden.
- Tasten: F4 Push-to-talk, F5 Mikrofon stumm/an, F7 Overlay an/aus.

BEISPIELE
Steuer:  volle Fahrt / halbe Kraft / voller Stopp / rückwärts / fliege zur Tallinn / fliege zum Ziel / Impuls /
         Warp / Energie
Taktik:  Schilde hoch / Schilde runter / roter Alarm / Feuer frei / Torpedos / Torpedos aktivieren / Feuer einstellen /
         Ziel Antrieb / Antrieb stören / scanne die Ragusa / greif an
Technik: Energie auf die Schilde / alles in den Antrieb / Waffen auf Maximum / repariere die Schilde /
         beam die Überlebenden an Bord / Warpkern laden

EINSTELLUNGEN (BepInEx\config\de.phuntasyproductions.stbc.voicecrew.cfg, entsteht beim ersten Start)
- [Sprache] BefehlsSprache = spiel | de | en | auto   (spiel = wie das Spiel, auto = beide gleichzeitig)
- [Sprache] ZuhoerModus = beides | ansprache | ptt | dauernd
- [Tasten] PushToTalk = F4
- [Watson] Bestaetigungstoene = true
- [Crew] Reflexe = true

BEKANNTE GRENZEN
- Warp- und Impulsziele werden weiterhin auf der Navigationskarte gewählt.
- Rufe annehmen, Sonden starten und Alarm aufheben gehen nur an der Captain-Konsole.
- Manöver steuern die Bot-Steuer direkt und sind experimentell.

Inoffizielle Fan-Mod. Star Trek ist eine Marke von CBS Studios Inc.; Star Trek: Bridge Crew ist ein Spiel von
Red Storm Entertainment / Ubisoft. Beide stehen in keiner Verbindung zu dieser Mod.
