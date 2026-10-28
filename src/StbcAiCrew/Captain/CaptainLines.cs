using System;
using System.Collections.Generic;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// Was der KI-Captain sagt, je Sprache mehrere Fassungen (damit es nicht nach Ansage klingt). {0} = Ziel/System/Name.
    /// Später durch Aufnahmen ersetzbar: der Schlüssel bleibt, nur die Stimme kommt dann aus einer Datei.
    /// </summary>
    public static class CaptainLines
    {
        static readonly Random Rng = new Random();

        static readonly Dictionary<string, string[]> De = new Dictionary<string, string[]>
        {
            ["take_command"] = new[] { "Hier spricht der Captain. Ich führe das Schiff. {0}, Sie übernehmen Ihre Station.", "Captain an Brücke: Ich habe das Kommando. {0}, auf Ihren Posten." },
            ["new_goal"] = new[] { "Neue Order: {0}.", "Unser Auftrag: {0}.", "Wir haben ein neues Ziel: {0}." },
            ["goal_done"] = new[] { "Auftrag erfüllt. Gute Arbeit, alle zusammen.", "Ziel erreicht. Weiter so.", "Erledigt. Ich bin stolz auf diese Crew." },
            ["warp_human"] = new[] { "Steuermann, Warpkurs nach {0} ist eingegeben. Bringen Sie uns auf Warp.", "Steuer, Kurs {0}, Warp. Energie, sobald bereit." },
            ["warp_bot"] = new[] { "Mister Rao, Warp nach {0}. Energie!", "Steuer, setzen Sie Kurs auf {0}, Warp. Energie!" },
            ["course_human"] = new[] { "Steuermann, bringen Sie uns zu {0}.", "Steuer, Kurs auf {0}, volle Fahrt.", "Fliegen Sie uns zu {0}." },
            ["course_bot"] = new[] { "Mister Rao, Kurs auf {0}.", "Steuer, bringen Sie uns zu {0}." },
            ["impulse_human"] = new[] { "Steuermann, Impulssprung zu {0}.", "Steuer, springen Sie mit Impuls zu {0}." },
            ["arrived"] = new[] { "Wir sind da.", "Gut geflogen. Wir haben {0} erreicht.", "Ziel erreicht, Steuer. Halten Sie Position." },
            ["stop_human"] = new[] { "Steuer, voller Stopp.", "Alle Maschinen stopp." },
            ["scan_human"] = new[] { "Taktik, scannen Sie {0}.", "Scannen Sie {0}, ich will wissen, was da los ist." },
            ["scan_bot"] = new[] { "Lieutenant Hoyle, scannen Sie {0}.", "Taktik, Scan von {0}." },
            ["attack_human"] = new[] { "Taktik, Ziel {0}. Feuer frei!", "Feuer auf {0}!", "Waffen auf {0}. Feuer!" },
            ["attack_bot"] = new[] { "Lieutenant Hoyle, Feuer auf {0}!", "Taktik, Ziel {0}, Feuer frei!" },
            ["disable_human"] = new[] { "Taktik, legen Sie den Antrieb von {0} lahm. Nicht zerstören!", "Stören Sie den Antrieb von {0}." },
            ["disable_bot"] = new[] { "Lieutenant Hoyle, Antrieb von {0} lahmlegen.", "Taktik, stören Sie den Antrieb von {0}." },
            ["beam_human"] = new[] { "Technik, Transporter bereit. Beamen Sie sie an Bord.", "T'nar-Station, beamen Sie die Leute von {0} herüber." },
            ["beam_bot"] = new[] { "Technik, beamen Sie sie an Bord. Energie!", "Lieutenant T'nar, Transporter auf {0}. Energie!" },
            ["shields_human"] = new[] { "Taktik, Schilde hoch!", "Schilde hoch, sofort!" },
            ["shields_bot"] = new[] { "Schilde hoch!", "Lieutenant Hoyle, Schilde!" },
            ["power_shields_human"] = new[] { "Technik, Energie auf die Schilde!", "Mehr Energie auf die Schilde, Technik!" },
            ["repair_human"] = new[] { "Technik, Schadenskontrolle! Reparieren Sie {0}.", "Technik, wir brauchen {0}. Reparieren!" },
            ["red_alert"] = new[] { "Alarmstufe Rot!", "Roter Alarm, alle auf Gefechtsstation!" },
            ["all_clear"] = new[] { "Alarm aufheben. Gute Arbeit.", "Entwarnung. Durchatmen." },
            ["hail"] = new[] { "Auf den Schirm.", "Kanal öffnen.", "Antworten Sie. Auf den Schirm." },
            ["defend_human"] = new[] { "Steuer, bleiben Sie bei {0}. Wir beschützen sie.", "Halten Sie uns nah an {0}." },
            ["remind"] = new[] { "{0}, ich warte!", "{0}? Bitte bestätigen.", "{0}, haben Sie mich verstanden?" },
            ["give_up"] = new[] { "Na gut, dann eben anders.", "Vergessen Sie es. Weiter." },
            ["ack"] = new[] { "Danke.", "Gut.", "Sehr gut." },
            ["praise"] = new[] { "Gut gemacht, {0}.", "Ausgezeichnet, {0}.", "Sauber, {0}. So kenne ich Sie." },
        };

        static readonly Dictionary<string, string[]> En = new Dictionary<string, string[]>
        {
            ["take_command"] = new[] { "This is the captain. I have the conn. {0}, man your station.", "Captain to bridge: I'm in command. {0}, to your post." },
            ["new_goal"] = new[] { "New orders: {0}.", "Our mission: {0}.", "We have a new objective: {0}." },
            ["goal_done"] = new[] { "Objective complete. Good work, everyone.", "Done. Keep it up.", "That's how it's done. Proud of this crew." },
            ["warp_human"] = new[] { "Helm, warp course for {0} is laid in. Take us to warp.", "Helm, heading {0}, warp. Engage when ready." },
            ["warp_bot"] = new[] { "Mister Rao, warp to {0}. Engage!", "Helm, set course for {0}, warp. Engage!" },
            ["course_human"] = new[] { "Helm, take us to {0}.", "Helm, set course for {0}, full speed.", "Bring us to {0}." },
            ["course_bot"] = new[] { "Mister Rao, set course for {0}.", "Helm, take us to {0}." },
            ["impulse_human"] = new[] { "Helm, impulse jump to {0}.", "Helm, jump to {0} on impulse." },
            ["arrived"] = new[] { "We've arrived.", "Nice flying. We've reached {0}.", "That'll do, helm. Hold position." },
            ["stop_human"] = new[] { "Helm, full stop.", "All stop." },
            ["scan_human"] = new[] { "Tactical, scan {0}.", "Scan {0}, I want to know what's going on." },
            ["scan_bot"] = new[] { "Lieutenant Hoyle, scan {0}.", "Tactical, run a scan on {0}." },
            ["attack_human"] = new[] { "Tactical, target {0}. Fire at will!", "Fire on {0}!", "Weapons on {0}. Fire!" },
            ["attack_bot"] = new[] { "Lieutenant Hoyle, fire on {0}!", "Tactical, target {0}, fire at will!" },
            ["disable_human"] = new[] { "Tactical, disable {0}'s engines. Don't destroy it!", "Disrupt {0}'s engines." },
            ["disable_bot"] = new[] { "Lieutenant Hoyle, disable {0}'s engines.", "Tactical, disrupt {0}'s engines." },
            ["beam_human"] = new[] { "Engineering, transporter ready. Beam them aboard.", "Engineering, beam the people over from {0}." },
            ["beam_bot"] = new[] { "Engineering, beam them aboard. Energize!", "Lieutenant T'nar, lock on to {0}. Energize!" },
            ["shields_human"] = new[] { "Tactical, shields up!", "Shields up, now!" },
            ["shields_bot"] = new[] { "Shields up!", "Lieutenant Hoyle, shields!" },
            ["power_shields_human"] = new[] { "Engineering, power to shields!", "More power to the shields, engineering!" },
            ["repair_human"] = new[] { "Engineering, damage control! Repair {0}.", "Engineering, we need {0}. Repair it!" },
            ["red_alert"] = new[] { "Red alert!", "Red alert, all hands to battle stations!" },
            ["all_clear"] = new[] { "Stand down from red alert. Good work.", "All clear. Breathe." },
            ["hail"] = new[] { "On screen.", "Open a channel.", "Answer them. On screen." },
            ["defend_human"] = new[] { "Helm, stay close to {0}. We protect them.", "Keep us near {0}." },
            ["remind"] = new[] { "{0}, I'm waiting!", "{0}? Please acknowledge.", "{0}, did you copy?" },
            ["give_up"] = new[] { "Fine, we'll do it another way.", "Never mind. Moving on." },
            ["ack"] = new[] { "Thank you.", "Good.", "Very good." },
            ["praise"] = new[] { "Well done, {0}.", "Excellent, {0}.", "Smooth, {0}. That's what I like to see." },
        };

        /// <summary>Kalauer für ruhige Momente (selbst geschrieben, keine Zitate).</summary>
        static readonly string[] JokesDe =
        {
            "Wissen Sie, warum Klingonen nie Diät halten? Weil Gagh nun mal nicht stillhält.",
            "Ich habe dem Replikator gesagt, er soll mich überraschen. Jetzt habe ich Tee mit Earl-Grey-Geschmack. Ohne Tee.",
            "Was sagt ein Vulkanier im Fitnessstudio? Widerstand ist logisch.",
            "Die Borg haben eine Party geschmissen. Jeder kam. Und blieb.",
            "Warum gehen Tribbles nie zum Arzt? Die vermehren sich lieber ungestört.",
            "Ich wollte einen Witz über Warpgeschwindigkeit erzählen. Aber der war schon vor mir da.",
            "Wie nennt man einen Romulaner ohne Tarnvorrichtung? Ehrlich.",
            "Mein Lieblingsessen auf der Aegis? Alles, was der Replikator nicht falsch ausspricht.",
            "Ein Photonentorpedo kommt in eine Bar. Der Barkeeper fragt: Was darf es sein? Der Torpedo sagt: Ein Knall.",
            "Wussten Sie, dass Schilde auch beim Smalltalk helfen? Einfach hochfahren und nicken.",
            "Ich habe meinem Steuermann gesagt, er soll den Kurs halten. Seitdem trägt er ihn überall mit sich herum.",
            "Warum spielen Vulkanier nie Verstecken? Sie würden sich logischerweise sofort finden.",
            "Der Warpkern und ich haben etwas gemeinsam: Beide brauchen morgens erst mal Antimaterie. Ich meine Kaffee.",
            "Was ist rot, trägt eine Uniform und überlebt nie die erste Szene? Fragen Sie besser nicht.",
            "Ich wollte die Holodeck-Sicherungen abschalten. Dann fiel mir ein, dass ich die Episode kenne.",
            "Wie viele Ingenieure braucht man, um eine Glühbirne zu wechseln? Einen, aber er sagt, es dauert vier Stunden, und schafft es in einer.",
            "Mein Arzt sagt, ich soll mehr Gemüse essen. Ich bin ein Captain, kein Botaniker.",
            "Was macht ein Klingone im Urlaub? Er kämpft gegen die Langeweile. Mit dem Bat'leth.",
            "Ein Andorianer, ein Tellarit und ein Vulkanier kommen in eine Bar. Der Tellarit beschwert sich über die Bar.",
        };

        static readonly string[] JokesEn =
        {
            "Why don't Klingons ever diet? Because gagh just won't sit still.",
            "I told the replicator to surprise me. Now I have tea that tastes of Earl Grey. Without the tea.",
            "What does a Vulcan say at the gym? Resistance is logical.",
            "The Borg threw a party. Everyone came. And stayed.",
            "Why don't tribbles ever see a doctor? They prefer to multiply in peace.",
            "I wanted to tell a joke about warp speed. But it got there before me.",
            "What do you call a Romulan without a cloaking device? Honest.",
            "A photon torpedo walks into a bar. The bartender asks what it'll have. The torpedo says: a blast.",
            "Did you know shields help with small talk too? Just raise them and nod.",
            "I told my helmsman to hold the course. Now he carries it everywhere.",
            "Why do Vulcans never play hide and seek? They'd logically find themselves at once.",
            "The warp core and I have something in common: neither of us works before the morning antimatter. I mean coffee.",
            "What's red, wears a uniform and never survives the first scene? Better not ask.",
            "How many engineers does it take to change a light bulb? One, but they say four hours and do it in one.",
            "My doctor says I should eat more vegetables. I'm a captain, not a botanist.",
            "What does a Klingon do on vacation? Fight boredom. With a bat'leth.",
            "An Andorian, a Tellarite and a Vulcan walk into a bar. The Tellarite complains about the bar.",
            "I asked the computer for a joke. It said: insufficient data. Story of my life.",
            "Why did the shuttlecraft break up with the starbase? It needed space.",
            "Engineering says the ship can't take much more. Engineering has said that every day for three years.",
        };

        public static string Say(string key, params object[] args)
        {
            var table = Loc.German ? De : En;
            string[] variants;
            if (!table.TryGetValue(key, out variants) && !En.TryGetValue(key, out variants)) return null;
            string s = variants[Rng.Next(variants.Length)];
            try { return args != null && args.Length > 0 ? string.Format(s, args) : s; } catch { return s; }
        }

        static readonly List<int> _usedJokes = new List<int>();

        /// <summary>Nächster Kalauer, ohne Wiederholung, bis alle durch sind.</summary>
        public static string Joke()
        {
            var list = Loc.German ? JokesDe : JokesEn;
            if (_usedJokes.Count >= list.Length) _usedJokes.Clear();
            int i;
            do i = Rng.Next(list.Length); while (_usedJokes.Contains(i));
            _usedJokes.Add(i);
            return list[i];
        }

        /// <summary>Ansprache einer Station ("Steuermann", "Helm" …) für Erinnerungen und Lob.</summary>
        public static string Title(Station st)
        {
            switch (st)
            {
                case Station.Helm: return Loc.T("Steuermann", "Helm");
                case Station.Tactical: return Loc.T("Taktik", "Tactical");
                case Station.Engineering: return Loc.T("Technik", "Engineering");
                default: return "Captain";
            }
        }

        /// <summary>Bestätigungen des Spielers ("Aye", "Jawohl", "Verstanden" …).</summary>
        public static bool IsAcknowledgement(string heard)
        {
            string s = " " + (heard ?? "").ToLowerInvariant() + " ";
            foreach (var w in new[] { " aye", " jawohl", " ja ", " verstanden", " zu befehl", " wird gemacht", " erledigt", " bin dabei", " sofort",
                                      " okay", " ok ", " roger", " copy", " on it", " done", " yes", " understood", " will do", " affirmative",
                                      " oui", " compris", " à vos ordres", " a vos ordres", " bien reçu", " c'est fait" })
                if (s.Contains(w)) return true;
            return false;
        }
    }
}
