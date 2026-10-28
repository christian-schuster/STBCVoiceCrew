using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace StbcAiCrew
{
    /// <summary>
    /// Testpilot für unbeaufsichtigte Testläufe (nur mit [Test] AutoTest): wählt Maus/Tastatur, überspringt die
    /// Hinweisbildschirme, startet im Hauptmenü ein Solo-Spiel, wählt im Besprechungsraum Missionsart und Schiff und
    /// startet die Mission. Auf der Brücke übernimmt der First Officer (AutoKommando). Danach die nächste Mission.
    /// Bedient die Menüs über ihre eigenen Unity-Knöpfe und -Schalter (Felder nach Namen), wie ein Klick.
    /// Jeder Schritt steht im Log ("Testpilot: …").
    /// </summary>
    public sealed class TestPilot
    {
        public string MissionType = "zufall";   // zufall | kampagne
        public string Ship = "aegis";           // aegis | enterprise | tng
        public int MaxMissions = 10;
        /// <summary>Ordner für Bildschirmfotos (alle 15 s latest.png, dazu je Mission nummerierte), null = keine.</summary>
        public string ShotDir;

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        float _next, _menuSince = -1f, _briefingSince = -1f, _lastClick, _nextShot;
        int _shot;
        bool _shotLogged;
        int _missions;
        bool _inMission;

        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 1.5f;
            try
            {
                Screenshot(now);
                if (GameBridge.InMission)
                {
                    if (!_inMission) { _inMission = true; _missions++; Log("Testlauf-Mission " + _missions + " läuft: " + MissionName()); }
                    _menuSince = _briefingSince = -1f;
                    return;
                }
                if (_inMission) { _inMission = false; Log("Testlauf-Mission " + _missions + " beendet"); }
                if (ControllerSelect() || Intro()) return;
                if (_missions >= MaxMissions) return;
                if (MainMenu(now)) return;
                BriefingRoom(now);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Testpilot: " + e.Message); }
        }

        // ---------------- Startbildschirme ----------------

        /// <summary>"Select Controller": Maus und Tastatur wählen (wie ein Tastendruck) und weiter.</summary>
        bool ControllerSelect()
        {
            var el = UnityEngine.Object.FindObjectOfType<ControllerCheckElement>();
            if (el == null || !el.isActiveAndEnabled) return false;
            var im = SVR.InputManager.IEFBKOMBFGO;
            if (im != null)
            {
                var set = typeof(SVR.InputManager).GetMethod("AHBEDKIFJDL", All, null, new[] { typeof(bool) }, null);
                if (set != null) set.Invoke(im, new object[] { false });      // false = kein VR-Controller
                var kb = typeof(SVR.InputManager).GetField("IHNHENLGKBO", All);
                if (kb != null) kb.SetValue(im, true);                         // Tastatur/Maus
            }
            Send("NAMOPFFJHDB");
            el.Continue();
            Log("Eingabe: Maus und Tastatur");
            return true;
        }

        /// <summary>Weitere Hinweisbildschirme (Gesundheit, Logos): überspringen, sobald sie es erlauben.</summary>
        bool Intro()
        {
            foreach (var el in UnityEngine.Object.FindObjectsOfType<IntroElement>())
            {
                if (el == null || !el.isActiveAndEnabled || el is ControllerCheckElement) continue;
                el.Continue();
                Log("Hinweisbildschirm übersprungen: " + el.GetType().Name);
                return true;
            }
            return false;
        }

        // ---------------- Hauptmenü ----------------

        bool MainMenu(float now)
        {
            var menu = UnityEngine.Object.FindObjectOfType<ConsoleMainMenu>();
            if (menu == null || !menu.isActiveAndEnabled) { _menuSince = -1f; return false; }
            if (_menuSince < 0) { _menuSince = now; Log("Hauptmenü"); }
            if (now - _menuSince < 4f || now - _lastClick < 6f) return true;   // Menü erst aufbauen lassen

            // Bestätigungsdialog ("Wir empfehlen erst das Training"): fortfahren
            var ok = Field<Button>(menu, "_confirmOkButton");
            if (ok != null && ok.isActiveAndEnabled && ok.gameObject.activeInHierarchy) { Click(ok, "Bestätigung OK"); return true; }

            // Spielmodus "Solo" wählen
            var infos = Field<Array>(menu, "_gameModeInfos");
            if (infos != null)
                foreach (var info in infos)
                {
                    if (info == null) continue;
                    foreach (var f in info.GetType().GetFields(All))
                        if (f.FieldType.IsEnum && f.GetValue(info).ToString() == "Solo")
                        {
                            var t = info.GetType().GetField("ModeSelectionToggle", All);
                            var toggle = t != null ? t.GetValue(info) as Toggle : null;
                            if (toggle != null && !toggle.isOn) { toggle.isOn = true; Log("Modus Solo gewählt"); }
                        }
                }
            menu.OnPlayGame();
            _lastClick = now;
            Log("Spiel starten (Solo)");
            return true;
        }

        // ---------------- Besprechungsraum ----------------

        void BriefingRoom(float now)
        {
            var room = UnityEngine.Object.FindObjectOfType<ConsoleBriefingRoom>();
            if (room == null || !room.isActiveAndEnabled) { _briefingSince = -1f; return; }
            if (_briefingSince < 0) { _briefingSince = now; Log("Besprechungsraum"); return; }
            if (now - _briefingSince < 5f || now - _lastClick < 8f) return;

            var recap = Field<Button>(room, "_closeMissionRecapButton");
            if (recap != null && recap.gameObject.activeInHierarchy) { Click(recap, "Missionsbericht schließen"); return; }

            bool campaign = (MissionType ?? "").StartsWith("kamp") || (MissionType ?? "").StartsWith("camp");
            SetToggle(room, campaign ? "_campaignMissionToggle" : "_proceduralMissionToggle", campaign ? "Kampagne" : "Zufallsmission");
            string ship = (Ship ?? "aegis").ToLowerInvariant();
            SetToggle(room, ship.StartsWith("tng") ? "_tngToggle" : ship.StartsWith("ent") ? "_enterpriseToggle" : "_aegisToggle", "Schiff " + ship);

            var launch = Field<Button>(room, "_launchGameButton");
            if (launch != null) Click(launch, "Mission starten");
            else Log("Startknopf nicht gefunden");
        }

        /// <summary>Bildschirmfoto aus dem Spiel selbst (geht auch, wenn das Fenster von außen nicht abfotografiert werden kann).</summary>
        void Screenshot(float now)
        {
            if (ShotDir == null || now < _nextShot) return;
            _nextShot = now + 15f;
            if (_shot == 0 && !_shotLogged) { _shotLogged = true; Log("Bildschirmfotos nach " + ShotDir); }
            System.IO.Directory.CreateDirectory(ShotDir);
            Application.CaptureScreenshot(System.IO.Path.Combine(ShotDir, "latest.png"));
            if (GameBridge.InMission && _shot++ % 4 == 0)   // jede Minute eins aufheben
                Application.CaptureScreenshot(System.IO.Path.Combine(ShotDir, "m" + _missions + "_" + (_shot / 4).ToString("000") + ".png"));
        }

        // ---------------- Hilfen ----------------

        void SetToggle(object owner, string field, string what)
        {
            var t = Field<Toggle>(owner, field);
            if (t != null && !t.isOn) { t.isOn = true; Log(what + " gewählt"); }
        }

        void Click(Button b, string what)
        {
            _lastClick = Time.realtimeSinceStartup;
            Log(what);
            b.onClick.Invoke();
        }

        static T Field<T>(object owner, string name) where T : class
        {
            for (var t = owner.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, All);
                if (f != null) return f.GetValue(owner) as T;
            }
            return null;
        }

        /// <summary>Spielereignis senden (statische Send()-Methode der verschleierten Ereignisklasse).</summary>
        static void Send(string eventType)
        {
            try
            {
                var t = typeof(ConsoleMainMenu).Assembly.GetType(eventType);
                var m = t != null ? t.GetMethod("Send", All, null, Type.EmptyTypes, null) : null;
                if (m != null) m.Invoke(null, null);
            }
            catch (Exception e) { Plugin.Log.LogDebug("Testpilot Ereignis " + eventType + ": " + e.Message); }
        }

        /// <summary>Name der laufenden Mission (Kampagne) bzw. Missionsart (Zufallsmission).</summary>
        static string MissionName()
        {
            try { var m = Loader.EJLNDBGLIFL; if (m == null) return "?"; string table = m.GetMissionTableDisplayName(); return !string.IsNullOrEmpty(table) ? "Zufallsmission " + table : (m.GetMissionDisplayName() ?? "?"); }
            catch { return "?"; }
        }

        static void Log(string s) => Plugin.Log.LogInfo("Testpilot: " + s);
    }
}
