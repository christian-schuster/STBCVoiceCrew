STBC Voice Crew – voice commands for Star Trek: Bridge Crew
Version 1.1.0 – by Phuntasy Productions – https://phuntasyproductions.de
=============================================================
(Deutsch weiter unten)

Replaces the old IBM Watson voice commands (no longer available) with fast, fully local speech
recognition. The bot crew – Rao (helm), Hoyle (tactical) and T'nar (engineering) – follows your spoken
orders and answers with their original voices. No internet, no account, no graphics card needed.

FEATURES
- Commands in English, German or French, recognised in about 0.3 seconds on the CPU (Vosk).
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
Easiest: run STBC-Voice-Crew-Setup.exe (from the GitHub release page) - it finds the game, installs and updates.
Or by hand:
1. Unpack the archive into the game folder (the folder that contains stbc.exe).
   The archive contains BepInEx 5.4.23 (mod loader). If you already use BepInEx 5, copying the folder
   BepInEx\plugins\STBCVoiceCrew is enough.
2. Start the game as usual. On first start the mod downloads the speech model for your game language (~45 MB)
   and, for the First Officer, Piper and a voice - once. Then the overlay in the top left corner shows
   "Vosk: en ready" (German game: "de bereit").
   The overlay is shown in German if the game is set to German, otherwise in English. It appears on the desktop
   monitor only, not in the VR headset – in VR you get feedback through the beeps and the crew's answers.
3. Uninstall: delete BepInEx\plugins\STBCVoiceCrew (and, if no other mods use it, winhttp.dll,
   doorstop_config.ini, .doorstop_version and the BepInEx folder).

HOW TO SPEAK TO THE CREW
- Short, complete orders work without addressing anyone: "Shields up", "Full stop", "Fire at will".
- Longer orders or conversation: address the station first – "Helm, …", "Rao, …", "Tactical, …",
  "Engineering, …", "Crew, …" – or hold F4 (push to talk) while speaking.
- Two high beeps: understood. One low beep: not understood.
- Keys: F4 push to talk, F5 mute/unmute microphone, F6 First Officer on/off, F7 overlay on/off.

FIRST OFFICER ("NUMBER ONE")
An AI first officer who is always with you – in the lobby and on the bridge. No language model, no internet.
- While you are the captain he advises you: short warnings when something is dangerous or wrong (shields down
  under fire, hull critical, friendly ship targeted, wrong warp destination, leaving before a required scan) and a
  hint when the mission is not moving forward. He knows the mission flow from the game's own mission scripts.
- If nobody sits in the captain's chair, he takes command: he reads the objectives, sets the warp destination and
  gives orders – the bots execute them at once, human officers hear them and see them on their console, and he
  waits for "Aye".
- He only speaks when nobody else does, and only briefly. Now and then, in quiet moments, a Star Trek pun.
- Talk to him with "Number One", "Commander" or "First Officer":
    "Number One, suggestions?"      – his recommendation for the current situation
    "You have the bridge"           – you stay in the captain's chair and watch, he runs the whole ship
                                      (also "Number One, take the bridge", "You are in command")
    "I have the bridge"             – you are in command again
    "Number One, dismissed"         – he stays quiet; "Number One, to the bridge" brings him back
    "Number One, tell me a joke"    – a Star Trek pun on request (not during combat)
  Acknowledge his orders with "Aye", "Acknowledged", "Understood" …
- On/off also with F6 or the button in the overlay. Languages: English, German, French (game language).

EXAMPLES
Helm:        full speed / half impulse / all stop / reverse / set a course for the Tallinn / approach the target /
             go to impulse / warp / engage / make it so
Tactical:    shields up / lower the shields / red alert / fire at will / fire torpedoes / arm the torpedoes /
             cease fire / target their engines / disrupt their engines / scan the Ragusa / attack
Engineering: power to shields / all power to the engines / weapons to maximum / repair the shields /
             beam the survivors aboard / energize / prepare for warp

SETTINGS (BepInEx\config\de.phuntasyproductions.stbcvoicecrew.cfg, created on first start)
- [Sprache] BefehlsSprache = spiel | de | en | fr | auto   (spiel = same as the game language, auto = German and English
  at once, or a list such as de,fr)
- [Sprache] ZuhoerModus = beides | ansprache | ptt | dauernd
- [Tasten] PushToTalk = F4
- [Watson] Bestaetigungstoene = true
- [Crew] Reflexe = true

KNOWN LIMITATIONS
- Warp and impulse destinations must still be selected on the navigation map (the First Officer sets the warp
  destination himself when he is in command).
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
Am einfachsten: STBC-Voice-Crew-Setup.exe (von der GitHub-Release-Seite) starten - findet das Spiel, installiert
und aktualisiert. Oder von Hand:
1. Archiv in den Spielordner entpacken (der Ordner mit stbc.exe).
   Enthalten ist BepInEx 5.4.23 (Mod-Lader). Wer BepInEx 5 schon nutzt, kopiert nur den Ordner
   BepInEx\plugins\STBCVoiceCrew.
2. Spiel wie gewohnt starten. Beim ersten Start lädt die Mod einmalig das Sprachmodell für die Spielsprache (ca. 45 MB)
   und für den Ersten Offizier Piper und eine Stimme. Danach zeigt das Overlay oben links „Vosk: de bereit“.
   Das Overlay ist deutsch, wenn das Spiel auf Deutsch steht, sonst englisch. Es erscheint nur auf dem Monitor,
   nicht in der VR-Brille – dort kommt die Rückmeldung über die Töne und die Antworten der Crew.
3. Entfernen: BepInEx\plugins\STBCVoiceCrew löschen (und, falls keine anderen Mods es brauchen, winhttp.dll,
   doorstop_config.ini, .doorstop_version und den Ordner BepInEx).

SO SPRICHST DU MIT DER CREW
- Kurze, vollständige Befehle gehen ohne Anrede: „Schilde hoch“, „Voller Stopp“, „Feuer frei“.
- Längere Befehle: Station ansprechen – „Steuer, …“, „Rao, …“, „Taktik, …“, „Technik, …“, „Crew, …“ – oder beim
  Sprechen F4 halten (Push-to-talk).
- Zwei hohe Töne: verstanden. Ein tiefer Ton: nicht verstanden.
- Tasten: F4 Push-to-talk, F5 Mikrofon stumm/an, F6 Erster Offizier an/aus, F7 Overlay an/aus.

ERSTER OFFIZIER („NUMMER EINS“)
Ein KI-Erster-Offizier, der immer dabei ist – in der Lobby und auf der Brücke. Ohne Sprachmodell, ohne Internet.
- Solange du Captain bist, berät er dich: kurze Warnungen bei Gefahr oder Fehlern (Schilde unten unter Beschuss,
  Hülle kritisch, Verbündeter im Ziel, falsches Warpziel, Abflug vor dem nötigen Scan) und ein Hinweis, wenn es
  beim Missionsziel nicht weitergeht. Den Missionsablauf kennt er aus den Missionsskripten des Spiels.
- Sitzt niemand auf dem Captain-Stuhl, übernimmt er das Kommando: Er liest die Missionsziele, setzt das Warpziel und
  gibt Befehle – die Bots führen sie sofort aus, menschliche Offiziere hören sie und sehen sie auf ihrer Konsole,
  und er wartet auf ein „Aye“.
- Er spricht nur, wenn sonst niemand spricht, und kurz. Ab und zu, in ruhigen Momenten, ein Star-Trek-Kalauer.
- Ansprache mit „Nummer Eins“, „Commander“ oder „Erster Offizier“:
    „Nummer Eins, Vorschlag?“       – seine Empfehlung zur Lage
    „Sie haben die Brücke“          – du bleibst auf dem Captain-Stuhl und schaust zu, er führt das ganze Schiff
                                      (auch „Nummer Eins, übernehmen Sie“)
    „Ich habe die Brücke“           – du hast wieder das Kommando
    „Nummer Eins, wegtreten“        – er schweigt; „Nummer Eins, auf die Brücke“ holt ihn zurück
    „Nummer Eins, erzähl einen Witz“ – ein Kalauer auf Zuruf (nicht im Gefecht)
  Seine Befehle bestätigst du mit „Aye“, „Jawohl“, „Verstanden“ …
- Ein/aus auch mit F6 oder dem Knopf im Overlay. Sprachen: Deutsch, Englisch, Französisch (wie das Spiel).

BEISPIELE
Steuer:  volle Fahrt / halbe Kraft / voller Stopp / rückwärts / fliege zur Tallinn / fliege zum Ziel / Impuls /
         Warp / Energie
Taktik:  Schilde hoch / Schilde runter / roter Alarm / Feuer frei / Torpedos / Torpedos aktivieren / Feuer einstellen /
         Ziel Antrieb / Antrieb stören / scanne die Ragusa / greif an
Technik: Energie auf die Schilde / alles in den Antrieb / Waffen auf Maximum / repariere die Schilde /
         beam die Überlebenden an Bord / Warpkern laden

EINSTELLUNGEN (BepInEx\config\de.phuntasyproductions.stbcvoicecrew.cfg, entsteht beim ersten Start)
- [Sprache] BefehlsSprache = spiel | de | en | fr | auto   (spiel = wie das Spiel, auto = Deutsch und Englisch gleichzeitig,
  oder eine Liste wie de,fr)
- [Sprache] ZuhoerModus = beides | ansprache | ptt | dauernd
- [Tasten] PushToTalk = F4
- [Watson] Bestaetigungstoene = true
- [Crew] Reflexe = true

BEKANNTE GRENZEN
- Warp- und Impulsziele werden weiterhin auf der Navigationskarte gewählt (führt der Erste Offizier, setzt er das
  Warpziel selbst).
- Rufe annehmen, Sonden starten und Alarm aufheben gehen nur an der Captain-Konsole.
- Manöver steuern die Bot-Steuer direkt und sind experimentell.

Inoffizielle Fan-Mod. Star Trek ist eine Marke von CBS Studios Inc.; Star Trek: Bridge Crew ist ein Spiel von
Red Storm Entertainment / Ubisoft. Beide stehen in keiner Verbindung zu dieser Mod.
