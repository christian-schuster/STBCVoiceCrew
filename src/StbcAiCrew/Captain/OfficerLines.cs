using System;
using System.Collections.Generic;

namespace StbcAiCrew.Captain
{
    /// <summary>
    /// Was der First Officer sagt, in allen Sprachen des Spiels (Deutsch, Englisch, Französisch), je Anlass mehrere Fassungen
    /// (damit es nicht nach Ansage klingt). {0} = Ziel/System/Name. Kurz und knapp, wie auf einer Brücke.
    /// Später durch Aufnahmen ersetzbar: der Schlüssel bleibt, nur die Stimme kommt dann aus einer Datei.
    /// </summary>
    public static class OfficerLines
    {
        static readonly Random Rng = new Random();

        static readonly Dictionary<string, string[]> De = new Dictionary<string, string[]>
        {
            // Melden, Übergabe
            ["greet"] = new[] { "Erster Offizier in Bereitschaft, Captain.", "Erster Offizier meldet sich zum Dienst, Captain.", "Nummer Eins zur Stelle, Captain." },
            ["bridge_ready"] = new[] { "Alle Stationen besetzt, Captain. Die Brücke gehört Ihnen.", "Brücke bereit, Captain.", "Alle Systeme bereit, Captain. Ihre Befehle?" },
            ["take_command"] = new[] { "Der Captain-Stuhl ist frei. Ich übernehme die Brücke. {0}, Sie bleiben auf Ihrem Posten.", "Erster Offizier übernimmt das Kommando. {0}, auf Ihren Posten." },
            ["conn_ack"] = new[] { "Aye, Captain. Ich habe die Brücke.", "Verstanden. Ich übernehme." },
            ["hand_back"] = new[] { "Captain auf der Brücke. Sie haben das Kommando.", "Willkommen zurück, Captain. Die Brücke gehört Ihnen." },
            ["on"] = new[] { "Erster Offizier zurück auf der Brücke.", "Zur Stelle, Captain." },
            ["off"] = new[] { "Aye, Captain. Ich halte mich zurück.", "Verstanden. Ich bin still." },
            ["status"] = new[] { "Wir sind dabei, Captain: {0}.", "Lage: {0}." },
            ["no_hint"] = new[] { "Keine Einwände, Captain. Wir sind auf Kurs.", "Nichts hinzuzufügen, Captain. Weiter so." },
            ["lobby_idle"] = new[] { "Die Brücke wartet, Captain. Der Replikator übrigens auch.", "Bereit, wenn Sie es sind, Captain.", "Die Crew scharrt schon mit den Füßen, Captain." },

            // Kommando
            ["new_goal"] = new[] { "Neue Order: {0}.", "Unser Auftrag: {0}.", "Wir haben ein neues Ziel: {0}." },
            ["goal_done"] = new[] { "Auftrag erfüllt. Gute Arbeit, alle zusammen.", "Ziel erreicht. Weiter so.", "Erledigt. Starke Crew." },
            ["warp_human"] = new[] { "Steuer, Warpkurs nach {0} ist eingegeben. Bringen Sie uns auf Warp.", "Steuer, Kurs {0}, Warp. Energie, sobald bereit." },
            ["warp_bot"] = new[] { "Mister Rao, Warp nach {0}. Energie!", "Steuer, Kurs auf {0}, Warp. Energie!" },
            ["course_human"] = new[] { "Steuer, bringen Sie uns zu {0}.", "Steuer, Kurs auf {0}, volle Fahrt.", "Fliegen Sie uns zu {0}." },
            ["course_bot"] = new[] { "Mister Rao, Kurs auf {0}.", "Steuer, bringen Sie uns zu {0}." },
            ["impulse_human"] = new[] { "Steuer, Impulssprung zu {0}.", "Steuer, springen Sie mit Impuls zu {0}." },
            ["arrived"] = new[] { "Wir sind da.", "Gut geflogen. Wir haben {0} erreicht.", "Ziel erreicht, Steuer. Position halten." },
            ["stop_human"] = new[] { "Steuer, voller Stopp.", "Alle Maschinen stopp." },
            ["scan_human"] = new[] { "Taktik, scannen Sie {0}.", "Scannen Sie {0}, ich will wissen, was da los ist." },
            ["scan_bot"] = new[] { "Lieutenant Hoyle, scannen Sie {0}.", "Taktik, Scan von {0}." },
            ["attack_human"] = new[] { "Taktik, Ziel {0}. Feuer frei!", "Feuer auf {0}!", "Waffen auf {0}. Feuer!" },
            ["attack_bot"] = new[] { "Lieutenant Hoyle, Feuer auf {0}!", "Taktik, Ziel {0}, Feuer frei!" },
            ["disable_human"] = new[] { "Taktik, legen Sie den Antrieb von {0} lahm. Nicht zerstören!", "Stören Sie den Antrieb von {0}." },
            ["disable_bot"] = new[] { "Lieutenant Hoyle, Antrieb von {0} lahmlegen.", "Taktik, stören Sie den Antrieb von {0}." },
            ["disable_fire_human"] = new[] { "Taktik, Phaser auf den Antrieb von {0}. Vorsichtig!", "Feuer auf den Antrieb von {0}, aber nicht zerstören." },
            ["disable_fire_bot"] = new[] { "Lieutenant Hoyle, Phaser auf den Antrieb von {0}.", "Taktik, schießen Sie {0} den Antrieb weg." },
            ["beam_human"] = new[] { "Technik, Transporter bereit. Beamen Sie sie an Bord.", "Technik, beamen Sie die Leute von {0} herüber." },
            ["beam_bot"] = new[] { "Technik, beamen Sie sie an Bord. Energie!", "Lieutenant T'nar, Transporter auf {0}. Energie!" },
            ["shields_human"] = new[] { "Taktik, Schilde hoch!", "Schilde hoch, sofort!" },
            ["shields_bot"] = new[] { "Schilde hoch!", "Lieutenant Hoyle, Schilde!" },
            ["power_shields_human"] = new[] { "Technik, Energie auf die Schilde!", "Mehr Energie auf die Schilde, Technik!" },
            ["defend_human"] = new[] { "Steuer, bleiben Sie bei {0}. Wir beschützen sie.", "Halten Sie uns nah an {0}." },
            ["red_alert"] = new[] { "Alarmstufe Rot!", "Roter Alarm, alle auf Gefechtsstation!" },
            ["all_clear"] = new[] { "Alarm aufheben. Gute Arbeit.", "Entwarnung. Durchatmen." },
            ["hail"] = new[] { "Auf den Schirm.", "Kanal öffnen.", "Antworten Sie. Auf den Schirm." },
            ["remind"] = new[] { "{0}, ich warte!", "{0}? Bitte bestätigen.", "{0}, haben Sie mich verstanden?" },
            ["give_up"] = new[] { "Na gut, dann eben anders.", "Vergessen Sie es. Weiter." },
            ["ack"] = new[] { "Danke.", "Gut.", "Sehr gut." },
            ["praise"] = new[] { "Gut gemacht, {0}.", "Ausgezeichnet, {0}.", "Sauber, {0}." },

            // Beratung: Warnungen
            ["warn_shields"] = new[] { "Captain, wir werden beschossen und die Schilde sind unten!", "Schilde hoch, Captain, wir nehmen Treffer!" },
            ["warn_hull"] = new[] { "Hülle kritisch, Captain. Ich empfehle den Rückzug.", "Captain, viel mehr hält die Hülle nicht aus. Abdrehen?" },
            ["warn_red_alert"] = new[] { "Captain, ich empfehle Alarmstufe Rot.", "Wir werden angegriffen, Captain. Roter Alarm?" },
            ["warn_friendly"] = new[] { "Vorsicht, Captain: {0} ist auf unserer Seite.", "Captain, {0} ist ein Verbündeter. Nicht feuern." },
            ["warn_wrong_warp"] = new[] { "Captain, das Warpziel stimmt nicht. Unser Auftrag führt nach {0}.", "Einspruch, Captain: Wir müssen nach {0}, nicht dorthin." },

            // Beratung: Hinweise
            ["hint_power_shields"] = new[] { "Captain, die Schilde brechen ein. Mehr Energie auf die Schilde?", "Schilde schwach, Captain. Technik sollte umverteilen." },
            ["hint_hail"] = new[] { "Captain, wir werden gerufen.", "Ein Ruf wartet, Captain." },
            ["hint_disable_only"] = new[] { "Erinnerung, Captain: {0} nur lahmlegen, nicht zerstören.", "Captain, wir brauchen {0} heil. Nur den Antrieb." },
            ["hint_timer"] = new[] { "Captain, die Uhr läuft.", "Wir sind auf Zeit, Captain." },
            ["hint_warp"] = new[] { "Captain, unser Ziel liegt im System {0}. Ich empfehle Warp.", "Captain, der Auftrag wartet in {0}. Warpkurs setzen?" },
            ["hint_course"] = new[] { "Captain, ich schlage Kurs auf {0} vor.", "Captain, wir sollten näher an {0} heran." },
            ["hint_impulse"] = new[] { "Captain, {0} ist weit weg. Ein Impulssprung spart Zeit.", "Captain, Impulssprung zu {0}?" },
            ["hint_search"] = new[] { "Captain, das Ziel ist noch nicht auf den Sensoren. Wir sollten das System absuchen.", "Captain, nichts auf den Sensoren. Vielleicht näher an die Planeten?" },
            ["hint_scan"] = new[] { "Captain, wir sollten {0} scannen.", "Captain, ein Scan von {0} würde uns weiterbringen." },
            ["hint_attack"] = new[] { "Captain, {0} ist in Reichweite. Feuer frei?", "Captain, {0} wartet nicht. Angreifen?" },
            ["hint_disable"] = new[] { "Captain, wir müssen {0} lahmlegen. Taktik kann den Antrieb stören.", "Captain, Antrieb von {0} stören, dann haben wir sie." },
            ["hint_disable_fire"] = new[] { "Captain, das Stören klappt nicht. Phaser gezielt auf den Antrieb von {0}?", "Captain, versuchen wir es mit Phasern auf den Antrieb von {0}." },
            ["hint_stop"] = new[] { "Captain, für den Transporter müssen wir anhalten.", "Captain, voller Stopp, dann können wir beamen." },
            ["hint_beam"] = new[] { "Captain, wir sind in Transporterreichweite von {0}. Beamen?", "Captain, Technik kann jetzt beamen." },
            ["hint_defend"] = new[] { "Captain, wir sollten bei {0} bleiben.", "Captain, {0} braucht unseren Schutz. Nicht zu weit weg." },
        };

        static readonly Dictionary<string, string[]> En = new Dictionary<string, string[]>
        {
            ["greet"] = new[] { "First Officer standing by, Captain.", "First Officer reporting for duty, Captain.", "Number One, at your service, Captain." },
            ["bridge_ready"] = new[] { "All stations manned, Captain. The bridge is yours.", "Bridge is ready, Captain.", "All systems ready, Captain. Your orders?" },
            ["take_command"] = new[] { "The captain's chair is empty. I have the bridge. {0}, stay at your post.", "First Officer taking command. {0}, to your post." },
            ["conn_ack"] = new[] { "Aye, Captain. I have the bridge.", "Understood. I have the conn." },
            ["hand_back"] = new[] { "Captain on the bridge. You have the conn.", "Welcome back, Captain. The bridge is yours." },
            ["on"] = new[] { "First Officer back on the bridge.", "Standing by, Captain." },
            ["off"] = new[] { "Aye, Captain. I'll keep quiet.", "Understood. Standing down." },
            ["status"] = new[] { "We're on it, Captain: {0}.", "Status: {0}." },
            ["no_hint"] = new[] { "Nothing to add, Captain. We're on track.", "No objections, Captain. Carry on." },
            ["lobby_idle"] = new[] { "The bridge is waiting, Captain. So is the replicator.", "Ready when you are, Captain.", "The crew is getting restless, Captain." },

            ["new_goal"] = new[] { "New orders: {0}.", "Our mission: {0}.", "We have a new objective: {0}." },
            ["goal_done"] = new[] { "Objective complete. Good work, everyone.", "Done. Keep it up.", "That's how it's done. Fine crew." },
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
            ["disable_fire_human"] = new[] { "Tactical, phasers on {0}'s engines. Carefully!", "Fire on {0}'s engines, but don't destroy it." },
            ["disable_fire_bot"] = new[] { "Lieutenant Hoyle, phasers on {0}'s engines.", "Tactical, shoot out {0}'s engines." },
            ["beam_human"] = new[] { "Engineering, transporter ready. Beam them aboard.", "Engineering, beam the people over from {0}." },
            ["beam_bot"] = new[] { "Engineering, beam them aboard. Energize!", "Lieutenant T'nar, lock on to {0}. Energize!" },
            ["shields_human"] = new[] { "Tactical, shields up!", "Shields up, now!" },
            ["shields_bot"] = new[] { "Shields up!", "Lieutenant Hoyle, shields!" },
            ["power_shields_human"] = new[] { "Engineering, power to shields!", "More power to the shields, engineering!" },
            ["defend_human"] = new[] { "Helm, stay close to {0}. We protect them.", "Keep us near {0}." },
            ["red_alert"] = new[] { "Red alert!", "Red alert, all hands to battle stations!" },
            ["all_clear"] = new[] { "Stand down from red alert. Good work.", "All clear. Breathe." },
            ["hail"] = new[] { "On screen.", "Open a channel.", "Answer them. On screen." },
            ["remind"] = new[] { "{0}, I'm waiting!", "{0}? Please acknowledge.", "{0}, did you copy?" },
            ["give_up"] = new[] { "Fine, we'll do it another way.", "Never mind. Moving on." },
            ["ack"] = new[] { "Thank you.", "Good.", "Very good." },
            ["praise"] = new[] { "Well done, {0}.", "Excellent, {0}.", "Smooth, {0}." },

            ["warn_shields"] = new[] { "Captain, we're taking fire and our shields are down!", "Shields, Captain, we're taking hits!" },
            ["warn_hull"] = new[] { "Hull critical, Captain. I recommend we withdraw.", "Captain, the hull won't take much more. Break off?" },
            ["warn_red_alert"] = new[] { "Captain, I recommend red alert.", "We're under attack, Captain. Red alert?" },
            ["warn_friendly"] = new[] { "Careful, Captain: {0} is on our side.", "Captain, {0} is friendly. Hold fire." },
            ["warn_wrong_warp"] = new[] { "Captain, that warp destination is wrong. Our mission is in {0}.", "Objection, Captain: we need to go to {0}, not there." },

            ["hint_power_shields"] = new[] { "Captain, shields are failing. More power to shields?", "Shields are weak, Captain. Engineering should reroute power." },
            ["hint_hail"] = new[] { "Captain, we're being hailed.", "Incoming hail, Captain." },
            ["hint_disable_only"] = new[] { "Reminder, Captain: disable {0}, don't destroy it.", "Captain, we need {0} intact. Engines only." },
            ["hint_timer"] = new[] { "Captain, the clock is ticking.", "We're on a timer, Captain." },
            ["hint_warp"] = new[] { "Captain, our objective is in the {0} system. I recommend warp.", "Captain, the mission awaits in {0}. Set a warp course?" },
            ["hint_course"] = new[] { "Captain, I suggest a course for {0}.", "Captain, we should close in on {0}." },
            ["hint_impulse"] = new[] { "Captain, {0} is far away. An impulse jump would save time.", "Captain, impulse jump to {0}?" },
            ["hint_search"] = new[] { "Captain, the objective isn't on sensors yet. We should search the system.", "Captain, nothing on sensors. Maybe closer to the planets?" },
            ["hint_scan"] = new[] { "Captain, we should scan {0}.", "Captain, a scan of {0} would move us forward." },
            ["hint_attack"] = new[] { "Captain, {0} is in range. Fire at will?", "Captain, {0} won't wait. Attack?" },
            ["hint_disable"] = new[] { "Captain, we need to disable {0}. Tactical can disrupt their engines.", "Captain, disrupt {0}'s engines and we have them." },
            ["hint_disable_fire"] = new[] { "Captain, the disruption isn't working. Phasers on {0}'s engines?", "Captain, let's try phasers on {0}'s engines." },
            ["hint_stop"] = new[] { "Captain, we need to stop for the transporter.", "Captain, full stop, then we can beam." },
            ["hint_beam"] = new[] { "Captain, we're in transporter range of {0}. Beam them?", "Captain, engineering can beam now." },
            ["hint_defend"] = new[] { "Captain, we should stay with {0}.", "Captain, {0} needs our protection. Don't stray too far." },
        };

        static readonly Dictionary<string, string[]> Fr = new Dictionary<string, string[]>
        {
            ["greet"] = new[] { "Premier officier à votre disposition, capitaine.", "Premier officier au rapport, capitaine.", "Numéro un, présent, capitaine." },
            ["bridge_ready"] = new[] { "Tous les postes sont occupés, capitaine. La passerelle est à vous.", "Passerelle prête, capitaine.", "Tous les systèmes sont prêts, capitaine. Vos ordres ?" },
            ["take_command"] = new[] { "Le fauteuil du capitaine est libre. Je prends la passerelle. {0}, restez à votre poste.", "Le premier officier prend le commandement. {0}, à votre poste." },
            ["conn_ack"] = new[] { "À vos ordres, capitaine. J'ai la passerelle.", "Compris. Je prends le relais." },
            ["hand_back"] = new[] { "Capitaine sur la passerelle. Vous avez le commandement.", "Bon retour, capitaine. La passerelle est à vous." },
            ["on"] = new[] { "Premier officier de retour sur la passerelle.", "Présent, capitaine." },
            ["off"] = new[] { "Bien, capitaine. Je reste en retrait.", "Compris. Je me tais." },
            ["status"] = new[] { "Nous y travaillons, capitaine : {0}.", "Situation : {0}." },
            ["no_hint"] = new[] { "Rien à ajouter, capitaine. Nous sommes sur la bonne voie.", "Aucune objection, capitaine. Continuez." },
            ["lobby_idle"] = new[] { "La passerelle vous attend, capitaine. Le réplicateur aussi.", "Prêt quand vous l'êtes, capitaine.", "L'équipage s'impatiente, capitaine." },

            ["new_goal"] = new[] { "Nouveaux ordres : {0}.", "Notre mission : {0}.", "Nouvel objectif : {0}." },
            ["goal_done"] = new[] { "Objectif accompli. Beau travail, tout le monde.", "C'est fait. Continuez comme ça.", "Voilà du travail bien fait." },
            ["warp_human"] = new[] { "Timonier, le cap pour {0} est entré. Passez en distorsion.", "Timonier, cap sur {0}, distorsion. Engagez dès que prêt." },
            ["warp_bot"] = new[] { "Monsieur Rao, distorsion vers {0}. Engagez !", "Timonier, cap sur {0}, distorsion. Engagez !" },
            ["course_human"] = new[] { "Timonier, amenez-nous à {0}.", "Timonier, cap sur {0}, pleine vitesse.", "Conduisez-nous à {0}." },
            ["course_bot"] = new[] { "Monsieur Rao, cap sur {0}.", "Timonier, amenez-nous à {0}." },
            ["impulse_human"] = new[] { "Timonier, saut d'impulsion vers {0}.", "Timonier, sautez vers {0} en impulsion." },
            ["arrived"] = new[] { "Nous y sommes.", "Joli pilotage. Nous avons atteint {0}.", "Parfait, timonier. Maintenez la position." },
            ["stop_human"] = new[] { "Timonier, arrêt complet.", "Arrêt des machines." },
            ["scan_human"] = new[] { "Tactique, scannez {0}.", "Scannez {0}, je veux savoir ce qui se passe." },
            ["scan_bot"] = new[] { "Lieutenant Hoyle, scannez {0}.", "Tactique, scan de {0}." },
            ["attack_human"] = new[] { "Tactique, cible {0}. Feu à volonté !", "Feu sur {0} !", "Armes sur {0}. Feu !" },
            ["attack_bot"] = new[] { "Lieutenant Hoyle, feu sur {0} !", "Tactique, cible {0}, feu à volonté !" },
            ["disable_human"] = new[] { "Tactique, neutralisez les moteurs de {0}. Ne le détruisez pas !", "Perturbez les moteurs de {0}." },
            ["disable_bot"] = new[] { "Lieutenant Hoyle, neutralisez les moteurs de {0}.", "Tactique, perturbez les moteurs de {0}." },
            ["disable_fire_human"] = new[] { "Tactique, phaseurs sur les moteurs de {0}. Prudence !", "Feu sur les moteurs de {0}, sans le détruire." },
            ["disable_fire_bot"] = new[] { "Lieutenant Hoyle, phaseurs sur les moteurs de {0}.", "Tactique, visez les moteurs de {0}." },
            ["beam_human"] = new[] { "Ingénierie, téléporteur prêt. Téléportez-les à bord.", "Ingénierie, téléportez les gens de {0}." },
            ["beam_bot"] = new[] { "Ingénierie, téléportez-les à bord. Énergie !", "Lieutenant T'nar, verrouillez sur {0}. Énergie !" },
            ["shields_human"] = new[] { "Tactique, boucliers !", "Levez les boucliers, maintenant !" },
            ["shields_bot"] = new[] { "Boucliers !", "Lieutenant Hoyle, boucliers !" },
            ["power_shields_human"] = new[] { "Ingénierie, énergie aux boucliers !", "Plus d'énergie aux boucliers, ingénierie !" },
            ["defend_human"] = new[] { "Timonier, restez près de {0}. Nous les protégeons.", "Gardez-nous près de {0}." },
            ["red_alert"] = new[] { "Alerte rouge !", "Alerte rouge, tout le monde aux postes de combat !" },
            ["all_clear"] = new[] { "Fin de l'alerte. Beau travail.", "Tout est calme. Respirez." },
            ["hail"] = new[] { "À l'écran.", "Ouvrez un canal.", "Répondez. À l'écran." },
            ["remind"] = new[] { "{0}, j'attends !", "{0} ? Confirmez, s'il vous plaît.", "{0}, vous m'avez reçu ?" },
            ["give_up"] = new[] { "Bon, on fera autrement.", "Laissez tomber. On continue." },
            ["ack"] = new[] { "Merci.", "Bien.", "Très bien." },
            ["praise"] = new[] { "Bien joué, {0}.", "Excellent, {0}.", "Impeccable, {0}." },

            ["warn_shields"] = new[] { "Capitaine, on nous tire dessus et les boucliers sont baissés !", "Les boucliers, capitaine, nous encaissons !" },
            ["warn_hull"] = new[] { "Coque critique, capitaine. Je recommande le repli.", "Capitaine, la coque ne tiendra plus longtemps. On décroche ?" },
            ["warn_red_alert"] = new[] { "Capitaine, je recommande l'alerte rouge.", "Nous sommes attaqués, capitaine. Alerte rouge ?" },
            ["warn_friendly"] = new[] { "Attention, capitaine : {0} est de notre côté.", "Capitaine, {0} est un allié. Ne tirez pas." },
            ["warn_wrong_warp"] = new[] { "Capitaine, la destination de distorsion est fausse. Notre mission est à {0}.", "Objection, capitaine : nous devons aller à {0}, pas là-bas." },

            ["hint_power_shields"] = new[] { "Capitaine, les boucliers faiblissent. Plus d'énergie aux boucliers ?", "Boucliers faibles, capitaine. L'ingénierie devrait rediriger l'énergie." },
            ["hint_hail"] = new[] { "Capitaine, on nous appelle.", "Un appel en attente, capitaine." },
            ["hint_disable_only"] = new[] { "Rappel, capitaine : neutraliser {0}, pas le détruire.", "Capitaine, il nous faut {0} intact. Seulement les moteurs." },
            ["hint_timer"] = new[] { "Capitaine, le temps presse.", "Nous sommes chronométrés, capitaine." },
            ["hint_warp"] = new[] { "Capitaine, notre objectif est dans le système {0}. Je recommande la distorsion.", "Capitaine, la mission nous attend à {0}. Un cap en distorsion ?" },
            ["hint_course"] = new[] { "Capitaine, je propose un cap sur {0}.", "Capitaine, nous devrions nous approcher de {0}." },
            ["hint_impulse"] = new[] { "Capitaine, {0} est loin. Un saut d'impulsion ferait gagner du temps.", "Capitaine, saut d'impulsion vers {0} ?" },
            ["hint_search"] = new[] { "Capitaine, l'objectif n'est pas encore sur les capteurs. Fouillons le système.", "Capitaine, rien sur les capteurs. Plus près des planètes, peut-être ?" },
            ["hint_scan"] = new[] { "Capitaine, nous devrions scanner {0}.", "Capitaine, un scan de {0} nous ferait avancer." },
            ["hint_attack"] = new[] { "Capitaine, {0} est à portée. Feu à volonté ?", "Capitaine, {0} n'attendra pas. On attaque ?" },
            ["hint_disable"] = new[] { "Capitaine, il faut neutraliser {0}. La tactique peut perturber ses moteurs.", "Capitaine, perturbons les moteurs de {0} et nous l'aurons." },
            ["hint_disable_fire"] = new[] { "Capitaine, la perturbation ne marche pas. Phaseurs sur les moteurs de {0} ?", "Capitaine, essayons les phaseurs sur les moteurs de {0}." },
            ["hint_stop"] = new[] { "Capitaine, il faut s'arrêter pour le téléporteur.", "Capitaine, arrêt complet, puis nous pourrons téléporter." },
            ["hint_beam"] = new[] { "Capitaine, nous sommes à portée de téléportation de {0}. On téléporte ?", "Capitaine, l'ingénierie peut téléporter maintenant." },
            ["hint_defend"] = new[] { "Capitaine, nous devrions rester près de {0}.", "Capitaine, {0} a besoin de notre protection. Ne nous éloignons pas." },
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
            "Mein Lieblingsessen an Bord? Alles, was der Replikator nicht falsch ausspricht.",
            "Ein Photonentorpedo kommt in eine Bar. Der Barkeeper fragt: Was darf es sein? Der Torpedo sagt: Ein Knall.",
            "Wussten Sie, dass Schilde auch beim Smalltalk helfen? Einfach hochfahren und nicken.",
            "Ich habe dem Steuermann gesagt, er soll den Kurs halten. Seitdem trägt er ihn überall mit sich herum.",
            "Warum spielen Vulkanier nie Verstecken? Sie würden sich logischerweise sofort finden.",
            "Der Warpkern und ich haben etwas gemeinsam: Beide brauchen morgens erst mal Antimaterie. Ich meine Kaffee.",
            "Was ist rot, trägt eine Uniform und überlebt nie die erste Szene? Fragen Sie besser nicht.",
            "Ich wollte die Holodeck-Sicherungen abschalten. Dann fiel mir ein, dass ich die Episode kenne.",
            "Wie viele Ingenieure braucht man, um eine Glühbirne zu wechseln? Einen, aber er sagt, es dauert vier Stunden, und schafft es in einer.",
            "Mein Arzt sagt, ich soll mehr Gemüse essen. Ich bin Erster Offizier, kein Botaniker.",
            "Was macht ein Klingone im Urlaub? Er kämpft gegen die Langeweile. Mit dem Bat'leth.",
            "Ein Andorianer, ein Tellarit und ein Vulkanier kommen in eine Bar. Der Tellarit beschwert sich über die Bar.",
            "Ich habe den Computer nach einem Witz gefragt. Er sagte: unzureichende Daten. Kenne ich.",
            "Warum hat das Shuttle mit der Sternenbasis Schluss gemacht? Es brauchte Raum.",
            "Die Technik sagt, das Schiff hält nicht mehr viel aus. Das sagt die Technik seit drei Jahren jeden Tag.",
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
            "I told the helmsman to hold the course. Now he carries it everywhere.",
            "Why do Vulcans never play hide and seek? They'd logically find themselves at once.",
            "The warp core and I have something in common: neither of us works before the morning antimatter. I mean coffee.",
            "What's red, wears a uniform and never survives the first scene? Better not ask.",
            "How many engineers does it take to change a light bulb? One, but they say four hours and do it in one.",
            "My doctor says I should eat more vegetables. I'm a first officer, not a botanist.",
            "What does a Klingon do on vacation? Fight boredom. With a bat'leth.",
            "An Andorian, a Tellarite and a Vulcan walk into a bar. The Tellarite complains about the bar.",
            "I asked the computer for a joke. It said: insufficient data. Story of my life.",
            "Why did the shuttlecraft break up with the starbase? It needed space.",
            "Engineering says the ship can't take much more. Engineering has said that every day for three years.",
        };

        static readonly string[] JokesFr =
        {
            "Pourquoi les Klingons ne font jamais de régime ? Parce que le gagh ne tient pas en place.",
            "J'ai demandé au réplicateur de me surprendre. J'ai eu du thé au goût d'Earl Grey. Sans le thé.",
            "Que dit un Vulcain à la salle de sport ? La résistance est logique.",
            "Les Borgs ont organisé une fête. Tout le monde est venu. Et personne n'est reparti.",
            "Pourquoi les tribbles ne vont jamais chez le médecin ? Ils préfèrent se multiplier en paix.",
            "Je voulais raconter une blague sur la distorsion. Mais elle est arrivée avant moi.",
            "Comment appelle-t-on un Romulien sans dispositif d'occultation ? Honnête.",
            "Une torpille à photons entre dans un bar. Le barman demande ce qu'elle prend. La torpille répond : un grand boum.",
            "Saviez-vous que les boucliers aident aussi à faire la conversation ? On les lève et on hoche la tête.",
            "Pourquoi les Vulcains ne jouent jamais à cache-cache ? Logiquement, ils se trouveraient tout de suite.",
            "Qu'est-ce qui est rouge, porte un uniforme et ne survit jamais à la première scène ? Mieux vaut ne pas demander.",
            "Combien d'ingénieurs faut-il pour changer une ampoule ? Un seul, mais il annonce quatre heures et le fait en une.",
            "Mon médecin dit que je devrais manger plus de légumes. Je suis premier officier, pas botaniste.",
            "Que fait un Klingon en vacances ? Il combat l'ennui. Au bat'leth.",
            "Un Andorien, un Tellarite et un Vulcain entrent dans un bar. Le Tellarite se plaint du bar.",
            "J'ai demandé une blague à l'ordinateur. Il a répondu : données insuffisantes. L'histoire de ma vie.",
            "Pourquoi la navette a-t-elle quitté la base stellaire ? Elle avait besoin d'espace.",
            "L'ingénierie dit que le vaisseau ne tiendra plus longtemps. Elle le dit tous les jours depuis trois ans.",
        };

        static Dictionary<string, string[]> Table => Loc.Lang == "de" ? De : Loc.Lang == "fr" ? Fr : En;

        public static string Say(string key, params object[] args)
        {
            string[] variants;
            if (!Table.TryGetValue(key, out variants) && !En.TryGetValue(key, out variants)) return null;
            string s = variants[Rng.Next(variants.Length)];
            try { return args != null && args.Length > 0 ? string.Format(s, args) : s; } catch { return s; }
        }

        static readonly List<int> _usedJokes = new List<int>();
        static string _jokeLang;

        /// <summary>Nächster Kalauer, ohne Wiederholung, bis alle durch sind.</summary>
        public static string Joke()
        {
            var list = Loc.Lang == "de" ? JokesDe : Loc.Lang == "fr" ? JokesFr : JokesEn;
            if (_jokeLang != Loc.Lang || _usedJokes.Count >= list.Length) { _usedJokes.Clear(); _jokeLang = Loc.Lang; }
            int i;
            do i = Rng.Next(list.Length); while (_usedJokes.Contains(i));
            _usedJokes.Add(i);
            return list[i];
        }

        /// <summary>Ansprache einer Station ("Steuermann", "Helm" …) für Erinnerungen und Lob.</summary>
        public static string Title(Station st)
        {
            bool fr = Loc.Lang == "fr";
            switch (st)
            {
                case Station.Helm: return fr ? "Timonier" : Loc.T("Steuer", "Helm");
                case Station.Tactical: return fr ? "Tactique" : Loc.T("Taktik", "Tactical");
                case Station.Engineering: return fr ? "Ingénierie" : Loc.T("Technik", "Engineering");
                default: return fr ? "Capitaine" : "Captain";
            }
        }

        /// <summary>Name, unter dem er spricht (Konsole, Overlay).</summary>
        public static string Name => Loc.Lang == "de" ? "Erster Offizier" : Loc.Lang == "fr" ? "Premier officier" : "First Officer";

        /// <summary>Bestätigungen des Spielers ("Aye", "Jawohl", "Verstanden" …).</summary>
        public static bool IsAcknowledgement(string heard)
        {
            string s = " " + (heard ?? "").ToLowerInvariant() + " ";
            return Has(s, " aye", " jawohl", " ja ", " verstanden", " zu befehl", " wird gemacht", " erledigt", " bin dabei", " sofort",
                       " okay", " ok ", " roger", " copy", " on it", " done", " yes", " understood", " will do", " affirmative",
                       " oui", " compris", " à vos ordres", " a vos ordres", " bien reçu", " c'est fait");
        }

        // ---------------- Sprachbefehle an den First Officer ----------------

        /// <summary>Spricht der Satz den First Officer an ("Nummer Eins", "Erster Offizier", "Number One" …)? s klein, mit Leerzeichen gerahmt.</summary>
        public static bool IsAddressed(string s) =>
            Has(s, "nummer eins", "nummer 1", "erster offizier", "ersten offizier", "first officer", "number one", "number 1",
                "premier officier", "numéro un", "numero un");

        /// <summary>
        /// Befehl an den First Officer: "off", "give_conn", "take_conn", "hint" oder null. Übergabe und Rückkehr der Brücke
        /// sind eindeutige Wendungen und gelten auch ohne Anrede, alles andere nur mit.
        /// </summary>
        public static string Command(string s, bool addressed)
        {
            if (Has(s, "sie haben die brücke", "sie haben das kommando", "sie haben die brücke", "übernehmen sie das kommando", "übernehmen sie die brücke",
                    "you have the bridge", "you have the conn", "take the conn", "take the bridge", "take command",
                    "vous avez la passerelle", "vous avez le commandement", "prenez le commandement", "prenez la passerelle"))
                return "give_conn";
            if (Has(s, "ich habe die brücke", "ich habe das kommando", "ich übernehme die brücke", "ich übernehme das kommando", "ich übernehme wieder",
                    "i have the bridge", "i have the conn", "i'm taking command", "i am taking command", "i relieve you", "i'll take it from here",
                    "j'ai la passerelle", "je prends le commandement", "je reprends le commandement"))
                return "take_conn";
            if (!addressed) return null;
            if (Has(s, " übernehmen sie ", " take over ", " prenez le relais ")) return "give_conn";
            if (Has(s, " ich übernehme ", " i'll take over ", " je reprends ")) return "take_conn";
            if (Has(s, " wegtreten ", " weggetreten ", " ruhe bitte ", " seien sie still ", " schweigen sie ", "offizier aus ", " eins aus ", " ausschalten ",
                    " dismissed ", " stand down ", " be quiet ", " silence ", "officer off ", " shut up ", " that will be all ",
                    " rompez ", " repos ", " taisez-vous ", " silence "))
                return "off";
            // Ist er aus, schaltet ihn jede andere Anrede wieder ein ("Nummer Eins, auf die Brücke"), sonst: Empfehlung zur Lage
            return "hint";
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }
    }
}
