using System;
using System.Collections.Generic;
using System.Linq;
using StbcAiCrew;
using StbcAiCrew.Voice;

// Testkorpus: echte Sätze aus den Spieltests (wie Whisper sie verstanden hat) und die gewünschten komplexen Anweisungen.
// Format: Satz | erwartete Absichten (Reihenfolge egal) | erwarteter aufgelöster Name (optional)
var names = new List<string> { "Ragusa", "Tallinn", "Nicholson", "Plasma-Kühlmittel A", "Plasma-Kühlmittel B", "Plasma-Kühlmittel C",
    "Typos", "Gamma Hydra", "Deep Space K-7", "Frachter Alpha 2", "Frachter Alpha 4", "Bird of Prey Alpha 1", "Gravitationsmine",
    "Asteroidenfeld", "Nebel", "Sektion 6", "Arna", "Borg-Kubus" };

var cases = new (string text, string intents, string name)[]
{
    // ---- Fahrt ----
    ("voller Impuls.", "SpeedFull", null),
    ("voller Schub.", "SpeedFull", null),
    ("Flieg schneller!", "SpeedFull", null),
    ("voller Stopp.", "SpeedStop", null),
    ("Holler Stop, bitte scannen.", "SpeedStop,Scan", null),
    ("Schilde hoch, volle Fahrt!", "ShieldsUp,SpeedFull", null),
    ("Halbe Kraft voraus.", "SpeedHalf", null),
    ("Rückwärts!", "SpeedReverse", null),
    // ---- Scannen / Ziel ----
    ("Bitte scanne die Ragusa.", "Scan", "Ragusa"),
    ("Crew: Bitte scanne die Rakuza.", "Scan", "Ragusa"),
    ("Scanne die Tallinn auf Leben.", "Scan", "Tallinn"),
    ("Bitte Scanner die Tallinn.", "Scan", "Tallinn"),
    ("Bitte nochmals scannen.", "Scan", null),
    ("Scannen nach Leben", "Scan", null),
    ("Neues Ziel: Sektion 6 Nebel.", "SelectTarget", null),
    ("Ziel Ragusa", "SelectTarget", "Ragusa"),
    // ---- Anflug ----
    ("Bitte fliegt uns zur Ragusa.", "MoveToTarget", "Ragusa"),
    ("Fliege zur Nicholson.", "MoveToTarget", "Nicholson"),
    ("Flieg zu Josef Nicholson.", "MoveToTarget", "Nicholson"),
    ("Fliege zum Schiff Tallinn.", "MoveToTarget", "Tallinn"),
    ("Flieg zu Plazmakülmittel A.", "MoveToTarget", "Plasma-Kühlmittel A"),
    ("Klick uns zu Plazmakühlmittel B und scanne es.", "MoveToTarget,Scan", "Plasma-Kühlmittel B"),
    ("Rao, bitte nimm Kurs auf, Frachter Alpha 2.", "MoveToTarget", "Frachter Alpha 2"),
    ("Bitte Kurs nehmen auf den Nebel in Sektion 6.", "MoveToTarget", null),
    ("Bitte das Schiff ausrichten zum Nebel.", "MoveToTarget", "Nebel"),
    ("Bitte näher an.", "MoveToTarget", null),
    ("Ja, flieg zur Akusa.", "MoveToTarget", "Ragusa"),
    // ---- Warp / Impuls ----
    ("Bitte. Geh auf Warp nach Gamma Hydra.", "Warp", "Gamma Hydra"),
    ("Flieg nach Arna, Warp, Energie!", "MoveToTarget,Warp,WarpEngage", null),
    ("Bitte die Warp-Spulen laden.", "PrepareWarp", null),
    ("Warp aktivieren.", "WarpEngage", null),
    ("Gehe auf Impuls.", "Impulse", null),
    ("Impuls.", "Impulse", null),
    ("Jetzt Impuls.", "ImpulseEngage", null),
    ("So, und jetzt bitte Impuls.", "ImpulseEngage", null),
    ("So, Fendrich Rao, geh doch bitte mal auf Impuls.", "Impulse", null),
    // ---- Taktik ----
    ("Schilde senken.", "ShieldsDown", null),
    ("Feuer einstellen.", "CeaseFire", null),
    ("Feuer!", "FirePhasers", null),
    ("Feuer auf mein Ziel", "FirePhasers", null),
    ("feuer meinen ziel", "FirePhasers", null),
    ("Feuer nur auf Befehl", "FireOnOrder", null),
    ("Torpedos feuern!", "FireTorpedoes", null),
    ("Nicht feuern!", "HoldFire", null),
    ("Ziel auf die Waffen!", "TargetWeapons", null),
    ("Antriebs-Subsystem auswählen", "TargetEngines", null),
    ("Antrieb übernehmen", "Hack", null),
    ("Übernimm den Antrieb", "Hack", null),
    ("Waffen hacken", "Hack", null),
    ("Ziel stören", "Hack", null),
    ("Stören!", "Hack", null),
    ("Schilde anvisieren", "TargetShields", null),
    ("Störe Antrieb", "Hack", null),
    ("Greif die Bird of Prey an.", "Attack", null),
    // ---- Technik ----
    ("Bitte beame die Überlebenden an Bord.", "Transport", null),
    ("Transportiere die Überlebenden an Bord.", "Transport", null),
    ("Wie met die Überlebenden anborg?", "Transport", null),
    ("Energie auf die Waffen", "PowerWeapons", null),
    ("Alles in den Antrieb!", "PowerEngines", null),
    ("Repariere die Schilde.", "Repair", null),
    // ---- Daueranweisungen / Manöver (Christians Wunschsätze) ----
    ("Umfliege das Minenfeld", "AvoidHazard", null),
    ("um fliege das minenfeld", "AvoidHazard", null),
    ("Weich den Minen aus!", "AvoidHazard", null),
    ("Fahre alle Systeme auf Minimum, damit wir nicht entdeckt werden", "StealthOn", null),
    ("Bleib außerhalb der gegnerischen Sensoren und nähere dich dem markierten Ziel", "StayOutOfSensors,MoveToTarget", null),
    ("Lass die Schilde unten und fliege nur mit 30% Schub, damit wir nicht entdeckt werden.", "HoldShieldsDown,LimitSpeed,StealthOn", null),
    ("Halte zehn Kilometer Abstand zum Feind", "KeepDistance", null),
    ("Feuert erst, wenn ich es sage.", "FireOnOrder", null),
    ("Haltet das Feuer, das ist ein Handelsschiff.", "HoldFire", null),
    ("Wir schleichen uns an, keiner darf uns sehen.", "StealthOn", null),
    ("Okay, Schilde wieder nach Bedarf und normale Fahrt.", "ReleaseOrders", null),
    ("Bring uns hier raus!", "Flee", null),
    ("Kreise um die Station.", "CircleTarget", null),
    // ---- Gespräch / kein Befehl ----
    ("Ja.", "Confirm", null),
    ("Nein.", "Deny", null),
    ("Das stimmt nicht.", "", null),
    ("Ich glaube, er ist beleidigt, weil ich ihn nervig genannt habe.", "", null),
    ("Glaubst du, wir schaffen die Borg mit der Aegis?", "", null),
};

int ok = 0, fail = 0;
foreach (var (text, intents, name) in cases)
{
    var r = CommandParser.Parse(text, names);
    var got = r.Commands.Select(c => c.Intent.ToString()).OrderBy(x => x).ToArray();
    var want = intents.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
    bool intentOk = got.SequenceEqual(want);
    bool nameOk = name == null || r.Commands.Any(c => c.Resolved == name);
    if (intentOk && nameOk) ok++;
    else
    {
        fail++;
        Console.WriteLine($"FEHLER  \"{text}\"\n        erwartet: {intents}{(name != null ? " (" + name + ")" : "")}\n        bekommen: {r}" +
                          (r.Addressed.HasValue ? $"  [an {r.Addressed}]" : ""));
    }
}

// Dieselben Sätze so, wie Vosk sie liefert: klein, ohne Satzzeichen (nur Bericht, zählt nicht als Fehler)
int vOk = 0;
foreach (var (text, intents, name) in cases)
{
    string vosk = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{Nd}' ]+", " ").Trim();
    var r = CommandParser.Parse(vosk, names);
    var got = r.Commands.Select(c => c.Intent.ToString()).OrderBy(x => x).ToArray();
    var want = intents.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
    if (got.SequenceEqual(want)) vOk++;
    else Console.WriteLine($"VOSK    \"{vosk}\"  erwartet: {intents}  bekommen: {r}");
}
Console.WriteLine($"Vosk-Form: {vOk}/{cases.Length}");

// Klangabgleich einzeln
foreach (var (heard, want) in new[] { ("Rekusa", "Ragusa"), ("Akusa", "Ragusa"), ("Typus", "Typos"), ("Plazmakülmittel C", "Plasma-Kühlmittel C"), ("Talin", "Tallinn") })
{
    float s; var got = Phonetics.BestMatch(heard, names, out s);
    if (got == want) ok++; else { fail++; Console.WriteLine($"FEHLER  Klang \"{heard}\" → {got ?? "nichts"} ({s:0.00}), erwartet {want}"); }
}

Console.WriteLine($"\n{ok} richtig, {fail} falsch");
return fail == 0 ? 0 : 1;
