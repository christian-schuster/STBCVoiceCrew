using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace StbcAiCrew.Voice
{
    /// <summary>
    /// Französischer Regelsatz: dieselben Absichten, mit den Wendungen der französischen Fassung ("distorsion" = Warp,
    /// "Énergie" = Transporter, "Exécution" = Make it so). Der Text wird vorher ohne Akzente und Apostrophe vereinheitlicht
    /// ("l'ennemi" → "l ennemi", "énergie" → "energie"), daher stehen die Muster ohne Akzente.
    /// </summary>
    public static partial class CommandParser
    {
        const string ART_FR = @"(?:(?:le|la|les|l|un|une|du|des|de\s+la|de\s+l|de|d|au|aux|notre|nos|leur|leurs|ce|cette|cet|ces)\s+)?";
        const string TO_FR = @"(?:vers|a|au|aux|jusqu\s+a|jusqu\s+au|en\s+direction\s+(?:de|du|des|d))";

        static void InitFrench()
        {
            var H = Station.Helm; var T = Station.Tactical; var E = Station.Engineering; var C = Station.Captain;

            // ---------- Daueranweisungen und Manöver zuerst ----------
            R(Intent.StealthOn, E, @"(mode\s+furtif|furtivite|passe\w*\s+en\s+furtif|(tous\s+les\s+)?systemes\s+au\s+minimum|(coupe\w*|eteins|eteignez)\s+(tous\s+les\s+)?systemes|(pas|jamais)\s+(etre\s+)?(detecte|repere|vu|remarque)\w*|sans\s+etre\s+(detecte|repere|vu)\w*|inapercu\w*|discretion|silence\s+radio)", negation: true);
            R(Intent.StealthOff, E, @"(fin\s+(du\s+)?mode\s+furtif|(desactive|arrete|quitte|termine)\w*\s+(le\s+)?mode\s+furtif)", negation: true);
            R(Intent.StayOutOfSensors, H, @"((reste|restons|restez|garde\w*\s+nous)\s+(hors|en\s+dehors)\s+(de\s+)?(la\s+)?(portee\s+)?(de\s+|des\s+)?(leurs?\s+|ses\s+)?(capteurs|detection|scanners?)|hors\s+de\s+portee\s+(de\s+leurs?\s+|des\s+|de\s+ses\s+)?capteurs|evite\w*\s+(leurs?\s+|ses\s+)?(capteurs|detection))", negation: true);
            R(Intent.AvoidHazard, H, @"(evite\w*|contourne\w*|fais\s+le\s+tour|faites\s+le\s+tour|passe\w*\s+autour|vole\w*\s+autour|navigue\w*\s+autour)\s+(?!.*capteur)(de\s+|du\s+|des\s+|d\s+)?" + ART_FR + NAME + "?");
            R(Intent.KeepDistance, H, @"((garde\w*|maintien\w*|reste\w*|restons)\s+(a\s+)?(?<num>[\w.,]+)\s*(km|kilometres?)(\s+de\s+distance)?|garde\w*\s+(nos\s+|vos\s+|une\s+|la\s+|tes\s+)?distance\w*(\s+de\s+securite)?|garde\w*\s+(nous\s+)?a\s+distance|reste\w*\s+a\s+distance)");
            R(Intent.CircleTarget, H, @"((tourne|vole|gravite|orbite)\w*\s+autour|(en\s+)?orbite\s+autour|encercle\w*)\s*(de\s+|du\s+|des\s+|d\s+)?" + ART_FR + NAME + "?");
            R(Intent.HoldPosition, H, @"((maintien\w*|garde\w*|tiens|tenez)\s+(la\s+|notre\s+|cette\s+)?position|(reste\w*|restons)\s+ici|sur\s+place)");
            R(Intent.Flee, H, @"(sors\s+nous\s+d\s+ici|sortez\s+nous\s+d\s+ici|sortons\s+d\s+ici|retraite|repli\w*|on\s+se\s+replie|fuy\w*|fuir|filons)");
            R(Intent.HoldShieldsDown, T, @"((laisse|garde)\w*\s+(les\s+)?boucliers\s+(baisses|abaisses|desactives|bas)|boucliers\s+(restent|resteront)\s+(baisses|bas))", negation: true);
            R(Intent.HoldShieldsUp, T, @"((laisse|garde)\w*\s+(les\s+)?boucliers\s+(leves|actives|en\s+place|hauts?)|boucliers\s+(restent|resteront)\s+(leves|actives))");
            R(Intent.LimitSpeed, H, @"((seulement|uniquement)\s+(a\s+)?(?<num>[\w.,]+)\s*(%|pour\s*cent)|(pas\s+plus\s+(de|que)|au\s+maximum|maximum)\s+(?<num2>[\w.,]+\s*(%|pour\s*cent)|moitie|quart)|(vole|va|avance|allez|avancez)\w*\s+lentement|^\s*lentement\s*$)", negation: true);
            R(Intent.HoldFire, T, @"(ne\s+(tire|tirez|tirons)\s+pas|ne\s+faites\s+pas\s+feu|pas\s+de\s+tir|(retenez|retiens|retenons)\s+(votre\s+|ton\s+|le\s+)?feu|halte\s+au\s+feu|armes\s+en\s+attente)", negation: true);
            R(Intent.FireOnOrder, T, @"((tire\w*|faites\s+feu|feu)\s+(seulement|uniquement)\s+(sur|a)\s+(mon\s+)?(ordre|commandement|signal)|attend\w*\s+mon\s+(ordre|signal)|sans\s+mon\s+ordre|personne\s+ne\s+tire)", negation: true);
            R(Intent.ReleaseOrders, C, @"(retour\s+a\s+la\s+normale|reprise\s+normale|(vitesse|vol|fonctionnement)\s+normal\w*|annule\w*\s+(tous\s+les\s+|les\s+)?ordres|comme\s+avant)");

            // ---------- Fahrt ----------
            R(Intent.SpeedFull, H, @"((pleine|toute|vitesse)\s+(vitesse|puissance|impulsion|maximale|max)|en\s+avant\s+toute|impulsion\s+maximale|plus\s+vite|accelere\w*|a\s+fond)");
            R(Intent.SpeedHalf, H, @"((demi|mi)\s*(vitesse|impulsion|puissance)|moitie\s+(de\s+la\s+)?(vitesse|puissance|impulsion)|ralenti\w*|moins\s+vite)");
            R(Intent.SpeedQuarter, H, @"(quart\s+(de\s+)?(vitesse|impulsion|puissance)|un\s+quart)");
            R(Intent.SpeedStop, H, @"(arret\s+(complet|total|des\s+machines|moteurs)|^\s*(stop|stoppe|stoppez|arret|halte)\s*$|arrete\w*\s+(le\s+vaisseau|les\s+moteurs|tout|les\s+machines|nous)|stoppe\w*\s+(le\s+vaisseau|les\s+moteurs|les\s+machines))");
            R(Intent.SpeedReverse, H, @"(marche\s+arriere|recule\w*|en\s+arriere|machine\s+arriere)");

            // ---------- Warp / Impuls ----------
            R(Intent.WarpEngage, H, @"(^\s*(engagez|engage|enclenchez|enclenche|execution|allez\s+y|en\s+avant)\s*$|engage\w*\s+(la\s+)?distorsion|distorsion\s+maintenant|(passe|passez|passons)\s+en\s+distorsion\s+maintenant)");
            R(Intent.PrepareWarp, E, @"(prepare\w*\s+(la\s+|le\s+saut\s+en\s+)?distorsion|charge\w*\s+(les\s+|le\s+)?(bobines|noyau|moteurs?|reacteur)\s+(de\s+|a\s+)?distorsion|(bobines|noyau)\s+de\s+distorsion\s+(pret|charge)\w*)");
            R(Intent.Warp, H, @"((passe\w*|passons|va\w*|allons|saute\w*|emmene\w*\s+nous|mets\s+nous|mettez\s+nous)?\s*(en\s+)?distorsion(\s+\w+)?(\s+" + TO_FR + @"\s+" + ART_FR + NAME + @")?|\bwarp\b)");
            R(Intent.ImpulseEngage, H, @"(engage\w*\s+(l\s+)?impulsion|impulsion\s+maintenant|saut\s+(d\s+)?impulsion\s+maintenant|lance\w*\s+(le\s+)?saut\s+(d\s+)?impulsion)");
            R(Intent.Impulse, H, @"((passe\w*|passons|va\w*|allons|saute\w*)?\s*(en\s+)?(saut\s+(d\s+|en\s+)?)?impulsion(\s+" + TO_FR + @"\s+" + ART_FR + NAME + ")?)");

            // ---------- Anflug / Kurs ----------
            R(Intent.MoveAway, H, @"(eloigne\w*(\s+nous)?(\s+(de|du|des|d))?|manoeuvre\w*\s+(d\s+)?evite\w*|manoeuvre\w*\s+evasive\w*|degage\w*|decroche\w*)");
            R(Intent.MoveToTarget, H, @"((va|vas|allez|allons|vole\w*|dirige\w*|emmene\w*|conduis\w*|conduisez|amene\w*|pilote\w*|file\w*)\s+(nous\s+)?" + TO_FR + @"\s+" + ART_FR + NAME +
                                      @"|(cap|route)\s+(sur|vers)(\s+" + ART_FR + NAME + @")?|(mets|mettez|met|fixe\w*)\s+(le\s+)?cap\s+(sur|vers)(\s+" + ART_FR + NAME + @")?|approche\w*(\s+nous)?\s*(de\s+|du\s+|des\s+|d\s+)?" + ART_FR + NAME +
                                      @"?|rapproche\w*(\s+nous)?(\s+(de|du|des|d)\s+" + ART_FR + NAME + @")?|intercept\w*\s*" + ART_FR + NAME + "?)");

            // ---------- Taktik ----------
            R(Intent.ShieldsUp, T, @"(boucliers?\s+(leves|en\s+place|actives|haut|en\s+haut)|(leve|levez|active|activez|monte|montez)\s+(les\s+)?boucliers|alerte\s+rouge)");
            R(Intent.ShieldsDown, T, @"((baisse|baissez|abaisse|abaissez|desactive|desactivez|coupe|coupez)\s+(les\s+)?boucliers|boucliers?\s+(baisses|abaisses|desactives|bas|en\s+bas))");
            R(Intent.ModulateShields, T, @"(module\w*\s+(les\s+|la\s+frequence\s+des\s+)?boucliers|modulation)");
            R(Intent.CeaseFire, T, @"(cessez\s+le\s+feu|cesse\w*\s+(de\s+tirer|le\s+feu)|arrete\w*\s+de\s+tirer)");
            R(Intent.ArmTorpedoes, T, @"((arme|armez|charge|chargez|prepare|preparez)\s+(les\s+)?(\w+\s+)?torpilles?|torpilles?\s+(pretes?|armees?|chargees?))");
            R(Intent.FireTorpedoes, T, @"((feu|tire\w*|lance\w*)\s+(les\s+|des\s+|une\s+)?(\w+\s+)?torpilles?|^\s*(\w+\s+)?torpilles?(\s+(feu|maintenant|lancees?))?\s*$)");
            R(Intent.FirePhasers, T, @"(phaser\w*|^\s*feu\s*$|feu\s+a\s+volonte|ouvr\w*\s+le\s+feu|faites\s+feu|feu\s+sur\b|\btire\w*|\btirons)");
            R(Intent.DisarmTorpedoes, T, @"(desarm\w*)");
            R(Intent.TargetEngines, T, @"((cible\w*|vise\w*)\s+(leurs?\s+|les\s+|le\s+|ses\s+|son\s+)?(moteurs?|propulsion|reacteurs?))");
            R(Intent.TargetWeapons, T, @"((cible\w*|vise\w*)\s+(leurs?\s+|les\s+|ses\s+|son\s+)?(armes|armement|phasers))");
            R(Intent.TargetShields, T, @"((cible\w*|vise\w*)\s+(leurs?\s+|les\s+|ses\s+)?boucliers?)");
            R(Intent.CeaseTargeting, T, @"((annule\w*|arrete\w*)\s+(le\s+)?ciblage|efface\w*\s+(la\s+)?cible)");
            R(Intent.Analyze, T, @"(analys\w*)");
            R(Intent.Scan, T, @"((scann?\w*|balaye\w*)(\s+" + ART_FR + NAME + @")?|signes?\s+de\s+vie)");
            R(Intent.Attack, T, @"(attaqu\w*(\s+" + ART_FR + NAME + @")?|a\s+l\s+attaque|detrui\w*\s*" + ART_FR + NAME + @"?|engage\w*\s+(le\s+)?combat)");
            R(Intent.SelectTarget, T, @"((nouvelle\s+)?cible\s*[:,]?\s*(est\s+)?" + ART_FR + NAME + @"|(selectionne\w*|marque\w*|verrouille\w*(\s+sur)?|accroche\w*)\s+" + ART_FR + NAME + ")");
            R(Intent.CancelRedAlert, T, @"(annule\w*\s+(l\s+)?alerte(\s+rouge)?|fin\s+(de\s+(l\s+)?)?alerte|levee\s+(de\s+l\s+|d\s+)?alerte)");
            R(Intent.CloakHunt, T, @"((chass|traqu)\w*\s+(le\s+|les\s+)?(vaisseau\w*\s+)?(occult|camoufl)\w*|cherche\w*\s+(le\s+|les\s+)?(vaisseau\w*\s+)?(occult|camoufl)\w*)");

            // ---------- Technik ----------
            const string POWER = @"(toute\s+l\s+|plus\s+d\s+|le\s+maximum\s+d\s+|max\w*\s+d\s+)?(energie|puissance)\s+(aux|sur\s+les|vers\s+les|dans\s+les|pour\s+les|a\s+la|au)\s+";
            const string MAXED = @"\s+(au|a)\s+(max\w*|fond|cent\s+pour\s+cent)";
            R(Intent.PowerWeapons, E, @"(" + POWER + @"(armes|phasers)|(armes|phasers)" + MAXED + @"|offensi\w*)");
            R(Intent.PowerShields, E, @"(" + POWER + @"boucliers|boucliers" + MAXED + @"|defensi\w*)");
            R(Intent.PowerEngines, E, @"(" + POWER + @"(moteurs|propulsion|reacteurs)|(moteurs|propulsion)" + MAXED + ")");
            R(Intent.PowerBalanced, E, @"(equilibr\w*\s+(l\s+|la\s+)?(energie|puissance)|repartis\w*\s+(l\s+|la\s+)?(energie|puissance)|repartir\s+(l\s+|la\s+)?(energie|puissance))");
            R(Intent.RepairAll, E, @"(repar\w*\s+(tout|tous\s+les\s+systemes))");
            R(Intent.Repair, E, @"\b(repar\w*)(\s+" + ART_FR + @"(?<sub>\p{L}+))?");
            R(Intent.Transport, E, @"(teleport\w*|transport\w*|^\s*energie\s*$|beam\w*|(ramene|ramenez|fais\s+monter|faites\s+monter)\w*\s+.+\s+a\s+bord|a\s+bord)");
            R(Intent.ReduceSignature, E, @"((reduis|reduisez|reduire|diminue\w*|baisse\w*)\s+(notre\s+)?signature)");
            R(Intent.Hack, T, @"((pirate\w*|brouille\w*|perturbe\w*|prends?\s+le\s+controle\s+(de|des|du)|prenez\s+le\s+controle\s+(de|des|du))\s+(leurs?\s+|les\s+|le\s+|ses\s+|son\s+|la\s+)?(?<sub>moteurs?|propulsion|armes|boucliers?|comm\w*)|^\s*(pirate|brouille|perturbe)\w*(\s+(la\s+)?cible)?\s*$|intrusion|piratage)");

            // ---------- Captain / Gespräch ----------
            R(Intent.AnswerHail, C, @"(repond\w*\s+(au|a\s+l)\s+appel|(a|sur)\s+l\s+ecran|ouvr\w*\s+(un\s+|une\s+)?(canal|frequence))");
            R(Intent.LaunchProbe, C, @"(lance\w*\s+(une\s+)?sonde)");
            R(Intent.Confirm, C, @"^\s*(oui|affirmatif|exact|correct|confirme|d\s+accord|bien\s+recu|a\s+vos\s+ordres)\s*$");
            R(Intent.Deny, C, @"^\s*(non|negatif|annule|annulez|laisse\s+tomber)\s*$");
        }

        static readonly Regex AddressRxFr = new Regex(@"^\s*(?<who>barre|timonier|navigat\w*|pilote|enseigne\s+rao|monsieur\s+rao|rao|tactique|officier\s+tactique|lieutenant\s+hoyle|hoyle|ingenierie|ingenieur|machines|salle\s+des\s+machines|chef|t\s*nar|tenar|equipage|passerelle|tout\s+le\s+monde|toutes\s+les\s+stations)\s*[,:!]?\s+", O);

        static Station? StationOfFr(string who, out bool all)
        {
            all = false;
            string w = who.ToLowerInvariant();
            if (w == "barre" || w == "timonier" || w.StartsWith("navigat") || w == "pilote" || w.Contains("rao")) return Station.Helm;
            if (w.Contains("tactique") || w.Contains("hoyle")) return Station.Tactical;
            if (w.StartsWith("ingenie") || w.Contains("machines") || w == "chef" || w.Contains("nar")) return Station.Engineering;
            all = true;
            return null;
        }

        static readonly Regex NegationFr = new Regex(@"\b(ne|n|pas|jamais|aucun\w*)\b", O);
        static readonly Regex FillerFr = new Regex(@"^(s\s+il\s+(vous|te)\s+plait|ok|okay|d\s+accord|bon|alors|euh|bien|et|donc|maintenant|puis|ensuite|vite)\s+", O);
        static readonly Regex OrderWordsFr = new Regex(@"\b(vol|cap|cible|bouclier|feu|phaser|torpille|distorsion|impulsion|scan|teleport|energie|repar|vitesse|stop|arret|attaqu|evit|mine|distance)\w*", O);
        static readonly Regex NameNoiseFr = new Regex(@"^(nous\s+)?(vers|a|au|aux|sur|de|du|des|d|la|le|les|l|un|une|vaisseau|station|en\s+direction\s+de)\s+|\s+(et\s+.*|scann?\w*.*|pour\s+.*|a\s+bord.*|s\s+il\s+(vous|te)\s+plait|maintenant|tout\s+de\s+suite)$", O);
        static readonly Regex PronounFr = new Regex(@"^(le|la|les|lui|eux|la\s+bas|y|ca|cela|ceci|nous|ici|signes\s+de\s+vie|vie)$", O);

        static readonly Dictionary<string, int> WordsFr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "zero", 0 }, { "un", 1 }, { "une", 1 }, { "deux", 2 }, { "trois", 3 }, { "quatre", 4 }, { "cinq", 5 }, { "six", 6 },
            { "sept", 7 }, { "huit", 8 }, { "neuf", 9 }, { "dix", 10 }, { "onze", 11 }, { "douze", 12 }, { "quinze", 15 },
            { "vingt", 20 }, { "trente", 30 }, { "quarante", 40 }, { "cinquante", 50 }, { "soixante", 60 }, { "cent", 100 },
            { "moitie", 50 }, { "demi", 50 }, { "quart", 25 }
        };

        /// <summary>Französisch vereinheitlichen: Akzente weg, Apostrophe als Wortgrenze ("l'ennemi" → "l ennemi").</summary>
        static string NormalizeFr(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                switch (char.ToLowerInvariant(ch))
                {
                    case 'à': case 'â': case 'ä': sb.Append('a'); break;
                    case 'é': case 'è': case 'ê': case 'ë': sb.Append('e'); break;
                    case 'î': case 'ï': sb.Append('i'); break;
                    case 'ô': case 'ö': sb.Append('o'); break;
                    case 'ù': case 'û': case 'ü': sb.Append('u'); break;
                    case 'ç': sb.Append('c'); break;
                    case 'œ': sb.Append("oe"); break;
                    case '\'': case '’': sb.Append(' '); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
