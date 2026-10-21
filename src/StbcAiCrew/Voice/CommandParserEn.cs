using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StbcAiCrew.Voice
{
    /// <summary>
    /// Englischer Regelsatz: dieselben Absichten wie auf Deutsch, mit den Wendungen aus Star Trek
    /// ("Engage" = Warp, "Energize" = Transporter, "Make it so", "Fire at will").
    /// </summary>
    public static partial class CommandParser
    {
        const string ART_EN = @"(?:(?:the|a|an|that|this|our|their)\s+)?";

        static void InitEnglish()
        {
            var H = Station.Helm; var T = Station.Tactical; var E = Station.Engineering; var C = Station.Captain;

            // ---------- Standing orders and maneuvers first ----------
            R(Intent.StealthOn, E, @"(all\s+systems\s+(to\s+|down\s+to\s+)?(minimum|minimal)|power\s+down\s+(all\s+)?(the\s+)?systems|stealth\s+mode|go\s+stealth|silent\s+running|run\s+silent|run(ning)?\s+dark|(not|never)\s+(be\s+)?(detected|seen|noticed|spotted)|(don'?t|can'?t|won'?t)\s+(see|detect|notice|spot)\s+us|undetected|unnoticed|sneak\w*)", negation: true);
            R(Intent.StealthOff, E, @"(end\s+stealth|stealth\s+off|cancel\s+stealth|stop\s+sneaking)", negation: true);
            R(Intent.StayOutOfSensors, H, @"((stay|keep)\s+(us\s+)?(out\s+of|outside(\s+of)?|clear\s+of)\s+(their\s+|the\s+|enemy\s+|the\s+enemy'?s?\s+|hostile\s+)*(sensor|detection|scanner)\w*(\s+range)?|out\s+of\s+(their\s+|enemy\s+)?sensor\s+range|avoid\s+(their\s+|enemy\s+)?(sensors|detection))", negation: true);
            R(Intent.AvoidHazard, H, @"(avoid|steer\s+clear\s+of|go\s+around|fly\s+around|navigate\s+around|route\s+around|evade)\s+" + ART_EN + NAME + "?");
            R(Intent.KeepDistance, H, @"((keep|hold|stay|maintain)\s+(?<num>[\w.]+)\s*(km|kilometers?|kilometres?|clicks?)\s*(away|distance|from|range)?|(keep|maintain)\s+(your\s+|our\s+|a\s+)?(safe\s+)?distance|keep\s+(us\s+)?away\s+from|stay\s+back)");
            R(Intent.CircleTarget, H, @"(circle|orbit)\w*\s*(around\s+)?" + ART_EN + NAME + "?");
            R(Intent.HoldPosition, H, @"(hold\s+(our\s+|this\s+)?position|station\s+keeping|stay\s+(right\s+)?here)");
            R(Intent.Flee, H, @"(get\s+us\s+out(\s+of\s+here)?|retreat|run\s+away|fall\s+back|flee|bug\s+out)");
            R(Intent.HoldShieldsDown, T, @"((keep|leave)\s+(the\s+)?shields\s+(down|off|lowered)|shields\s+stay\s+down)", negation: true);
            R(Intent.HoldShieldsUp, T, @"((keep|leave)\s+(the\s+)?shields\s+(up|on|raised)|shields\s+stay\s+up)");
            R(Intent.LimitSpeed, H, @"(only\s+(at\s+|with\s+|use\s+)?(?<num>[\w.]+)\s*(%|percent)|(no\s+more\s+than|at\s+most|not\s+faster\s+than|maximum\s+of)\s+(?<num2>[\w.]+\s*(%|percent)|half|quarter)|(fly|go|move)\s+slow(ly)?)", negation: true);
            R(Intent.HoldFire, T, @"(hold\s+(your\s+)?fire|(don'?t|do\s+not)\s+(fire|shoot)|no\s+(firing|shooting)|weapons\s+hold)", negation: true);
            R(Intent.FireOnOrder, T, @"(only\s+(fire|shoot)\s+(on|at|when)\b|(fire|shoot)\s+only\s+(on|at)\s+my\s+(order|command|mark)|wait\s+for\s+my\s+(order|command|mark)|(fire|shoot)\s+(only\s+)?when\s+i\s+(say|tell)|nobody\s+(fires|shoots)\s+without|without\s+my\s+(order|command))", negation: true);
            R(Intent.ReleaseOrders, C, @"(as\s+you\s+were|back\s+to\s+normal|normal\s+(operations|speed|flight)|cancel\s+(all\s+)?(standing\s+)?orders|resume\s+normal\w*)");

            // ---------- Speed ----------
            R(Intent.SpeedFull, H, @"(full\s+(speed|impulse|ahead|throttle)|maximum\s+(speed|impulse)|max\s+speed|all\s+ahead\s+full|top\s+speed|faster|speed\s+up|floor\s+it)");
            R(Intent.SpeedHalf, H, @"(half\s+(speed|impulse|power|ahead|throttle)|slow\s+(it\s+|us\s+)?down|slower)");
            R(Intent.SpeedQuarter, H, @"((one\s+)?quarter\s+(speed|impulse|power|ahead|throttle)?)");
            R(Intent.SpeedStop, H, @"(all\s+stop|full\s+stop|^\s*stop\s*$|stop\s+(the\s+)?(ship|engines)|come\s+to\s+a\s+(full\s+)?stop|^\s*halt\s*$)");
            R(Intent.SpeedReverse, H, @"(reverse|back\s+up|full\s+astern|astern)");

            // ---------- Warp / impulse ----------
            R(Intent.WarpEngage, H, @"(^\s*engage\s*$|engage\s+(the\s+)?warp|warp\s+now|punch\s+it|make\s+it\s+so)");
            R(Intent.PrepareWarp, E, @"(prepare\s+(for\s+)?(the\s+)?warp|charge\s+(the\s+)?warp\s*(core|coils|drive)?|spin\s+up\s+(the\s+)?warp\s*(core|drive)?|warp\s+(core|coils)\s+(online|charge\w*|ready))");
            R(Intent.Warp, H, @"((go\s+to|take\s+us\s+to|jump\s+to)?\s*warp(\s+(speed|factor)\s+\w+)?(\s+to\s+" + ART_EN + NAME + @")?|\bwarp\b)");
            R(Intent.ImpulseEngage, H, @"(engage\s+impulse|impulse\s+(now|go))");
            R(Intent.Impulse, H, @"((go\s+to|take\s+us\s+to|jump\s+to)?\s*impulse(\s+jump)?(\s+to\s+" + ART_EN + NAME + ")?)");

            // ---------- Course ----------
            R(Intent.MoveAway, H, @"(move\s+away(\s+from)?|get\s+away\s+from|back\s+away|evasive(\s+maneuvers?)?|break\s+off|veer\s+off)");
            R(Intent.MoveToTarget, H, @"((fly|head|move|go|take\s+us|bring\s+us|steer|proceed|get\s+us)\s+(us\s+)?(to|toward|towards|over\s+to)\s+" + ART_EN + NAME +
                                      @"|(set|lay\s+in|plot)\s+(a\s+)?course\s+(for|to)(\s+" + ART_EN + NAME + @")?|approach\s*" + ART_EN + NAME + @"?|close\s+(in|the\s+distance)(\s+(on|to|with)\s+" + ART_EN + NAME +
                                      @")?|get\s+(us\s+)?closer(\s+to\s+" + ART_EN + NAME + @")?|intercept\s*" + ART_EN + NAME + "?)");

            // ---------- Tactical ----------
            R(Intent.ShieldsUp, T, @"(shields\s+up|raise\s+(the\s+)?shields|shields\s+on|activate\s+(the\s+)?shields|red\s+alert)");
            R(Intent.ShieldsDown, T, @"(shields\s+down|lower\s+(the\s+)?shields|drop\s+(the\s+)?shields|shields\s+off|deactivate\s+(the\s+)?shields)");
            R(Intent.ModulateShields, T, @"(modulate\s+(the\s+)?shields?|shield\s+modulation|remodulate)");
            R(Intent.CeaseFire, T, @"(cease\s+fire|stop\s+(firing|shooting))");
            R(Intent.ArmTorpedoes, T, @"((arm|load|ready)\s+(the\s+)?(photon\s+)?torpedo\w*|torpedo\w*\s+(ready|armed|loaded|standing\s+by))");
            R(Intent.FireTorpedoes, T, @"(fire\s+(the\s+)?(photon\s+)?torpedo\w*|torpedo\w*\s+(away|fire|now|launch)|launch\s+(the\s+)?torpedo\w*|^\s*(photon\s+)?torpedo\w*\s*$)");
            R(Intent.FirePhasers, T, @"(fire\s+(the\s+)?phasers?|^\s*phasers?(\s+fire|\s+now)?\s*$|^\s*fire\s*$|fire\s+at\s+will|open\s+fire|fire\s+(at|on)\b|weapons\s+free|shoot\w*)");
            R(Intent.DisarmTorpedoes, T, @"((disarm|unload)\s+(the\s+)?torpedo\w*|stand\s+down\s+(the\s+)?torpedo\w*)");
            R(Intent.TargetEngines, T, @"((target|aim\s+(for|at))\s+(their\s+|the\s+)?(engines?|propulsion|drive))");
            R(Intent.TargetWeapons, T, @"((target|aim\s+(for|at))\s+(their\s+|the\s+)?(weapons?|weapon\s+systems?|phasers?))");
            R(Intent.TargetShields, T, @"((target|aim\s+(for|at))\s+(their\s+|the\s+)?shields?)");
            R(Intent.CeaseTargeting, T, @"((cease|stop)\s+targeting|clear\s+(the\s+)?target(ing)?)");
            R(Intent.Analyze, T, @"(analy[sz]\w*)");
            R(Intent.Scan, T, @"((scan\w*|run\s+a\s+scan(\s+on)?)(\s+(for\s+life\s+signs\s+on\s+)?" + ART_EN + NAME + @")?|life\s+signs)");
            R(Intent.Attack, T, @"(attack\w*(\s+" + ART_EN + NAME + @")?|engage\s+(the\s+)?(enemy|target|hostiles?)|take\s+(them|it)\s+out|destroy\s*" + ART_EN + NAME + "?)");
            R(Intent.SelectTarget, T, @"((new\s+)?target\s*[:,]?\s*(is\s+)?" + ART_EN + NAME + @"|(select|mark|lock\s+on(to)?)\s+" + ART_EN + NAME + ")");
            R(Intent.CancelRedAlert, T, @"(cancel\s+red\s+alert|stand\s+down\s+(from\s+)?red\s+alert|all\s+clear)");
            R(Intent.CloakHunt, T, @"(cloak\w*\s+hunt|hunt\s+(for\s+)?(the\s+)?cloak\w*|find\s+(the\s+)?cloaked)");

            // ---------- Engineering ----------
            const string MAXED = @"\s+(to\s+)?(max(imum)?|full(\s+power)?|a\s+hundred\s+percent|one\s+hundred\s+percent|hundred\s+percent)";
            R(Intent.PowerWeapons, E, @"((all\s+|full\s+|more\s+|maximum\s+)?power\s+to\s+(the\s+)?(weapons?|phasers?)|(weapons?|phasers?)" + MAXED + ")");
            R(Intent.PowerShields, E, @"((all\s+|full\s+|more\s+|maximum\s+)?power\s+to\s+(the\s+)?shields?|shields?" + MAXED + ")");
            R(Intent.PowerEngines, E, @"((all\s+|full\s+|more\s+|maximum\s+)?power\s+to\s+(the\s+)?(engines?|propulsion|impulse\s+engines?|drive)|(engines?|propulsion)" + MAXED + ")");
            R(Intent.PowerBalanced, E, @"(balance\s+(the\s+)?power|even\s+out\s+(the\s+)?power|distribute\s+(the\s+)?power\s+evenly|power\s+balanced)");
            R(Intent.RepairAll, E, @"(repair\s+(all|everything)(\s+systems)?|fix\s+everything)");
            R(Intent.Repair, E, @"(repair|fix)(\s+" + ART_EN + @"(?<sub>\p{L}+))?");
            R(Intent.Transport, E, @"(beam\w*|transport\w*|energi[sz]e|bring\s+(them|the\s+survivors|.+?)\s+(aboard|on\s+board))");
            R(Intent.ReduceSignature, E, @"((reduce|lower|minimi[sz]e)\s+(our\s+)?signature)");
            R(Intent.Hack, T, @"((disrupt|hack|jam|scramble|take\s+over|take\s+control\s+of|override)\s+(their\s+|the\s+|its\s+)?(?<sub>engines?|propulsion|weapons?|shields?|comm\w*)|^\s*disrupt(\s+(the\s+)?target)?\s*$|system\s+intrusion|hack\w*)");

            // ---------- Captain / conversation ----------
            R(Intent.AnswerHail, C, @"(answer\s+(the\s+)?hail|on\s+screen|open\s+(a\s+)?channel)");
            R(Intent.LaunchProbe, C, @"(launch\s+(a\s+)?probe)");
            R(Intent.Confirm, C, @"^\s*(yes|yeah|affirmative|aye|correct|confirmed)\s*$");
            R(Intent.Deny, C, @"^\s*(no|negative|cancel|belay\s+that)\s*$");
        }

        static readonly Regex AddressRxEn = new Regex(@"^\s*(?<who>helm|conn|navigation|navigator|ensign\s+rao|mister\s+rao|mr\.?\s+rao|rao|tactical|weapons\s+officer|lieutenant\s+hoyle|hoyle|engineering|engineer|chief|t'?nar|tenar|crew|bridge|everyone|everybody|all\s+stations)\s*[,:!]?\s+", O);

        static Station? StationOfEn(string who, out bool all)
        {
            all = false;
            string w = who.ToLowerInvariant();
            if (w == "helm" || w == "conn" || w.StartsWith("navigat") || w.Contains("rao")) return Station.Helm;
            if (w == "tactical" || w.StartsWith("weapons") || w.Contains("hoyle")) return Station.Tactical;
            if (w.StartsWith("engineer") || w == "chief" || w.Contains("nar")) return Station.Engineering;
            all = true;
            return null;
        }

        static readonly Regex NegationEn = new Regex(@"\b(not|don'?t|do\s+not|never|no)\b", O);
        static readonly Regex FillerEn = new Regex(@"^(please|ok|okay|alright|all\s+right|so|uh|um|well|and|just|right)\s+", O);
        static readonly Regex OrderWordsEn = new Regex(@"\b(fly|course|target|shield|fire|phaser|torpedo|warp|impulse|scan|beam|power|repair|speed|stop|attack|evade|mine|distance)\w*", O);
        static readonly Regex NameNoiseEn = new Regex(@"^(us\s+)?(to|toward|towards|for|at|on|the|a|an|ship|station|please|over\s+to)\s+|\s+(and\s+.*|scan.*|for\s+life.*|aboard.*|around|please|now)$", O);
        static readonly Regex PronounEn = new Regex(@"^(it|them|her|him|there|that|this|us|here|life\s+signs|select|mark|lock)$", O);

        static readonly Dictionary<string, int> WordsEn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", 0 }, { "one", 1 }, { "a", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 }, { "six", 6 },
            { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 }, { "twelve", 12 }, { "fifteen", 15 },
            { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 }, { "fifty", 50 }, { "sixty", 60 }, { "seventy", 70 },
            { "eighty", 80 }, { "ninety", 90 }, { "hundred", 100 }, { "half", 50 }, { "quarter", 25 }
        };
    }
}
