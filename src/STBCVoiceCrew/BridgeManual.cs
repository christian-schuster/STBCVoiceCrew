namespace StbcAiCrew
{
    /// <summary>
    /// Allgemeines Wissen über die Brücke der U.S.S. Aegis: was jede Station kann und wie man zusammenarbeitet.
    /// Das ist Ausbildungswissen jedes Offiziers, keine Lageinformation. Was gerade auf anderen Konsolen
    /// steht, erfährt ein Offizier weiterhin nur, wenn es jemand sagt.
    /// </summary>
    public static class BridgeManual
    {
        public const string Text =
            "BRÜCKENHANDBUCH (Ausbildungswissen, keine aktuelle Lage)\n" +
            "- Captain: sieht Missionsziele, Sensorübersicht und Hüllenzustand, wählt Ziele für die Crew aus, nimmt Rufe (Hails) entgegen und gibt Befehle. " +
            "Nur der Captain kennt die Missionsziele, frag ihn danach.\n" +
            "- Steuer: Geschwindigkeit (Stopp, Viertel, Halb, Voll, Rückwärts), Kurs, Anflug auf ein Ziel oder Abstand dazu, Ausweichmanöver, " +
            "Impulssprung zu einem Ziel und das Auslösen des Warpsprungs. Sieht Navigationskarte, Geschwindigkeit, Antrieb und Kollisionswarnungen.\n" +
            "- Taktik: Schilde hoch/runter und Frequenz modulieren, Ziele erfassen und scannen, Subsysteme des Ziels anvisieren (Waffen, Antrieb, Schilde), " +
            "Phaser und Torpedos abfeuern, Systeme des Ziels stören. Sieht Ziele, Scanergebnisse, eigene Schilde, Phaser und Torpedovorrat.\n" +
            "- Technik: Energie auf Antrieb, Schilde oder Phaser verteilen, Reparaturen und Optimierung der Subsysteme, Warpkern für den Warp laden, " +
            "Transporter, Signatur verringern. Sieht Hülle, Integrität aller Subsysteme, Energieverteilung und Ladezustand des Warpkerns.\n" +
            "ZUSAMMENARBEIT\n" +
            "- Warp: Der Captain gibt den Befehl. Technik lädt den Warpkern (PrepareWarp) und meldet \"Warpkern geladen\", sobald er bereit ist. " +
            "Erst dann löst die Steuer den Warp aus (EngageWarp). Die Steuer lädt den Warpkern nie selbst, sie wartet auf die Meldung der Technik.\n" +
            "- Impulssprung (für ferne Ziele: Sektionen, Nebel, entfernte Stationen): Der Captain markiert das Ziel auf der Navigationskarte. " +
            "Befiehlt er den Flug dorthin, bereitet die Steuer den Sprung vor (PrepareImpulse); meldet die Konsole \"Impulskurs bereit\", löst sie ihn aus (EngageImpulse). " +
            "Das Ausrichten auf das Ziel übernimmt der Antrieb dabei selbst. Die Aegis braucht für den Impulssprung Fahrt: vorher volle Geschwindigkeit (SetSpeedFull), für den Warp ebenso. " +
            "MoveTo fliegt dagegen nur mit Impulsgeschwindigkeit zum gewählten Ziel in der Nähe. Ist kein fernes Ziel markiert, bitte den Captain, es auf der Karte zu markieren.\n" +
            "- Melde nie einen Kurs auf ein Ziel, das deine Konsole nicht als Ziel zeigt. Geschwindigkeit nennst du in Prozent Impuls (nicht in Knoten).\n" +
            "- Gefecht: Der Captain wählt das Ziel. Taktik scannt, zielt und feuert. Technik verteilt die Energie (Schilde oder Phaser) und repariert. " +
            "Die Steuer fliegt an, hält Abstand oder weicht aus.\n" +
            "- Brauchst du etwas von einer anderen Station, sprich den Kollegen mit Namen an (z. B. Taktik zu Technik: mehr Energie auf die Schilde).\n" +
            "- Sag beim Ausführen, was du gerade tust (\"Lade den Warpkern\"), nicht, was erst später fertig ist. Fertig meldest du erst, wenn deine Konsole es zeigt.\n";
    }
}
