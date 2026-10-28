using System;
using System.Collections.Generic;
using System.Linq;
using STBCVoiceCrew;
using STBCVoiceCrew.Voice;

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
    ("Senke die Schilde", "ShieldsDown", null),
    ("senken schilde", "ShieldsDown", null),
    ("Hebe die Schilde", "ShieldsUp", null),
    ("Feuer einstellen.", "CeaseFire", null),
    ("Feuer!", "FirePhasers", null),
    ("Feuer auf mein Ziel", "FirePhasers", null),
    ("danach feuer auf ragusa", "FirePhasers", null),
    ("feuer meinen ziel", "FirePhasers", null),
    ("Feuer nur auf Befehl", "FireOnOrder", null),
    ("Torpedos feuern!", "FireTorpedoes", null),
    ("Torpedos aktivieren", "ArmTorpedoes", null),
    ("Mach die Torpedos scharf", "ArmTorpedoes", null),
    ("Torpedo abschießen", "FireTorpedoes", null),
    ("Torpedos", "FireTorpedoes", null),
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
    ("Schilde auf Maximum", "PowerShields", null),
    ("Energie zu den Schilden", "PowerShields", null),
    ("energie zur schilde", "PowerShields", null),
    ("Energie zum Antrieb", "PowerEngines", null),
    ("Waffen auf volle Leistung", "PowerWeapons", null),
    ("Antrieb auf hundert Prozent", "PowerEngines", null),
    ("Repariere die Schilde.", "Repair", null),
    // ---- Daueranweisungen / Manöver (gewünschte Beispielsätze) ----
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

// ---- Englisch ----
var casesEn = new (string text, string intents, string name)[]
{
    ("Full speed ahead.", "SpeedFull", null),
    ("Half impulse.", "SpeedHalf", null),
    ("All stop!", "SpeedStop", null),
    ("Full stop, scan the target.", "SpeedStop,Scan", null),
    ("Shields up!", "ShieldsUp", null),
    ("Red alert!", "ShieldsUp", null),
    ("Lower the shields.", "ShieldsDown", null),
    ("Scan the Ragusa.", "Scan", "Ragusa"),
    ("Scan for life signs.", "Scan", null),
    ("Helm, set a course for the Tallinn.", "MoveToTarget", "Tallinn"),
    ("Take us to the Nicholson.", "MoveToTarget", "Nicholson"),
    ("fly two ragusa", "MoveToTarget", "Ragusa"),
    ("can the target", "Scan", null),
    ("Fly to Plasma Coolant A", "MoveToTarget", null),
    ("Approach the target.", "MoveToTarget", null),
    ("Engage!", "WarpEngage", null),
    ("Make it so.", "WarpEngage", null),
    ("Warp to Gamma Hydra.", "Warp", "Gamma Hydra"),
    ("Prepare for warp.", "PrepareWarp", null),
    ("Go to impulse.", "Impulse", null),
    ("Fire at will!", "FirePhasers", null),
    ("Fire phasers.", "FirePhasers", null),
    ("Fire torpedoes!", "FireTorpedoes", null),
    ("Torpedoes.", "FireTorpedoes", null),
    ("Arm the torpedoes.", "ArmTorpedoes", null),
    ("Cease fire.", "CeaseFire", null),
    ("Hold your fire, that's a freighter.", "HoldFire", null),
    ("Target their engines.", "TargetEngines", null),
    ("Disrupt their engines.", "Hack", null),
    ("Attack the Bird of Prey.", "Attack", null),
    ("Energize.", "Transport", null),
    ("Beam the survivors aboard.", "Transport", null),
    ("Power to shields.", "PowerShields", null),
    ("All power to the engines!", "PowerEngines", null),
    ("Weapons to maximum.", "PowerWeapons", null),
    ("Repair the shields.", "Repair", null),
    ("Fly around the minefield.", "AvoidHazard", null),
    ("Avoid the mines.", "AvoidHazard", null),
    ("Power down all systems so they can't detect us.", "StealthOn", null),
    ("Stay out of their sensor range and approach the marked target.", "StayOutOfSensors,MoveToTarget", null),
    ("Keep ten kilometers distance from the enemy.", "KeepDistance", null),
    ("Circle the station.", "CircleTarget", null),
    ("Get us out of here!", "Flee", null),
    ("Only fire on my order.", "FireOnOrder", null),
    ("As you were.", "ReleaseOrders", null),
    ("Yes.", "Confirm", null),
    ("I think we should have pizza tonight.", "", null),
    ("Did you see that game yesterday?", "", null),
};
foreach (var (text, intents, name) in casesEn)
{
    var r = CommandParser.Parse(text, names, Lang.En);
    var got = r.Commands.Select(c => c.Intent.ToString()).OrderBy(x => x).ToArray();
    var want = intents.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
    bool nameOk = name == null || r.Commands.Any(c => c.Resolved == name);
    if (got.SequenceEqual(want) && nameOk) ok++;
    else { fail++; Console.WriteLine($"FEHLER  EN \"{text}\"\n        erwartet: {intents}{(name != null ? " (" + name + ")" : "")}\n        bekommen: {r}"); }
}

var casesFr = new (string text, string intents, string name)[]
{
    ("En avant toute.", "SpeedFull", null),
    ("Pleine impulsion !", "SpeedFull", null),
    ("Demi-vitesse.", "SpeedHalf", null),
    ("Arrêt complet.", "SpeedStop", null),
    ("Stop.", "SpeedStop", null),
    ("Marche arrière.", "SpeedReverse", null),
    ("Arrêt complet, scannez la cible.", "SpeedStop,Scan", null),
    ("Levez les boucliers !", "ShieldsUp", null),
    ("Alerte rouge.", "ShieldsUp", null),
    ("Baissez les boucliers.", "ShieldsDown", null),
    ("Feu à volonté.", "FirePhasers", null),
    ("Tirez les phasers.", "FirePhasers", null),
    ("Lancez les torpilles !", "FireTorpedoes", null),
    ("Torpilles.", "FireTorpedoes", null),
    ("Armez les torpilles à photons.", "ArmTorpedoes", null),
    ("Cessez le feu.", "CeaseFire", null),
    ("Ne tirez pas, c'est un cargo.", "HoldFire", null),
    ("Ciblez leurs moteurs.", "TargetEngines", null),
    ("Ciblez leurs armes.", "TargetWeapons", null),
    ("Brouillez leurs moteurs.", "Hack", null),
    ("Attaquez l'oiseau de proie.", "Attack", null),
    ("Énergie.", "Transport", null),
    ("Téléportez les survivants à bord.", "Transport", null),
    ("Énergie aux boucliers.", "PowerShields", null),
    ("Toute l'énergie aux moteurs !", "PowerEngines", null),
    ("Armes au maximum.", "PowerWeapons", null),
    ("Réparez les boucliers.", "Repair", null),
    ("Passez en distorsion.", "Warp", null),
    ("Préparez la distorsion.", "PrepareWarp", null),
    ("Engagez.", "WarpEngage", null),
    ("Saut d'impulsion vers la Ragusa.", "Impulse", "Ragusa"),
    ("Cap sur la Ragusa.", "MoveToTarget", "Ragusa"),
    ("Emmène-nous vers la Tallinn.", "MoveToTarget", "Tallinn"),
    ("Scannez la Tallinn.", "Scan", "Tallinn"),
    ("Contournez le champ de mines.", "AvoidHazard", null),
    ("Évitez les mines.", "AvoidHazard", null),
    ("Mode furtif, ils ne doivent pas nous détecter.", "StealthOn", null),
    ("Restez hors de portée de leurs capteurs.", "StayOutOfSensors", null),
    ("Gardez dix kilomètres de distance.", "KeepDistance", null),
    ("Tournez autour de la station.", "CircleTarget", null),
    ("Sortez-nous d'ici !", "Flee", null),
    ("Tirez seulement sur mon ordre.", "FireOnOrder", null),
    ("Retour à la normale.", "ReleaseOrders", null),
    ("Barre, cap sur la Ragusa et scannez-la.", "MoveToTarget,Scan", "Ragusa"),
    ("Répondez à l'appel.", "AnswerHail", null),
    ("Oui.", "Confirm", null),
    ("Je crois qu'on devrait manger une pizza ce soir.", "", null),
    ("Tu as vu le match hier ?", "", null),
};
foreach (var (text, intents, name) in casesFr)
{
    var r = CommandParser.Parse(text, names, Lang.Fr);
    var got = r.Commands.Select(c => c.Intent.ToString()).OrderBy(x => x).ToArray();
    var want = intents.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();
    bool nameOk = name == null || r.Commands.Any(c => c.Resolved == name);
    if (got.SequenceEqual(want) && nameOk) ok++;
    else { fail++; Console.WriteLine($"FEHLER  FR \"{text}\"\n        erwartet: {intents}{(name != null ? " (" + name + ")" : "")}\n        bekommen: {r}"); }
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
