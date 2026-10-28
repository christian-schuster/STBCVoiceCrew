using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StbcVoiceCrewSetup
{
    /// <summary>
    /// One-click installer for STBC Voice Crew: finds Star Trek: Bridge Crew (Steam libraries, Ubisoft Connect or a file
    /// dialog), downloads the latest release ZIP from GitHub and unpacks it into the game folder. Running it again updates;
    /// "Remove" uninstalls the mod.
    /// </summary>
    static class Program
    {
        public const string Repo = "__REPO__";   // GitHub owner/repository
        const string SteamAppId = "527100";

        [STAThread]
        static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        // ---------------- Finding the game ----------------

        public static List<string> FindGame()
        {
            var found = new List<string>();
            foreach (var root in SteamRoots())
            {
                var libs = new List<string> { root };
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string acf = Path.Combine(lib, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                    if (!File.Exists(acf)) continue;
                    var dir = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    Add(found, Path.Combine(lib, "steamapps", "common", dir));
                }
            }
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs"))
                if (key != null)
                    foreach (var id in key.GetSubKeyNames())
                        using (var k = key.OpenSubKey(id))
                            Add(found, k?.GetValue("InstallDir") as string);
            return found;
        }

        static IEnumerable<string> SteamRoots()
        {
            foreach (var (hive, path, name) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
                using (var k = hive.OpenSubKey(path))
                {
                    var v = k?.GetValue(name) as string;
                    if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) yield return Path.GetFullPath(v.Replace('/', '\\'));
                }
        }

        static void Add(List<string> list, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "stbc.exe"))) return;
            dir = Path.GetFullPath(dir).TrimEnd('\\');
            if (!list.Contains(dir, StringComparer.OrdinalIgnoreCase)) list.Add(dir);
        }

        // ---------------- Install / update / remove ----------------

        public static string InstalledVersion(string gameDir)
        {
            string dll = Path.Combine(gameDir, "BepInEx", "plugins", "STBC Voice Crew", "de.phuntasyproductions.stbcvoicecrew.dll");
            return File.Exists(dll) ? FileVersionInfo.GetVersionInfo(dll).FileVersion : null;
        }

        public static async Task<string> Install(string gameDir, Action<string> status)
        {
            if (Process.GetProcessesByName("stbc").Length > 0) throw new Exception("Please close Star Trek: Bridge Crew first.");
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("STBC-Voice-Crew-Setup");
                status("Looking for the latest release …");
                string json = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest");
                string tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                var asset = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.zip)\"").Cast<Match>()
                                 .Select(m => m.Groups[1].Value).FirstOrDefault(u => Path.GetFileName(u).StartsWith("STBC-Voice-Crew-"));
                if (asset == null) throw new Exception("No release ZIP found on GitHub (" + Repo + ").");

                string zip = Path.Combine(Path.GetTempPath(), Path.GetFileName(asset));
                status("Downloading " + Path.GetFileName(asset) + " …");
                using (var resp = await http.GetAsync(asset, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = File.Create(zip)) await src.CopyToAsync(dst);
                }

                // Old plugin folder/DLL name: the mod takes over its data on first start; only the old DLL must go
                foreach (var folder in new[] { "StbcAiCrew", "STBC Voice Crew" })
                {
                    string oldDll = Path.Combine(gameDir, "BepInEx", "plugins", folder, "StbcAiCrew.dll");
                    if (File.Exists(oldDll)) File.Delete(oldDll);
                }

                status("Installing …");
                using (var archive = ZipFile.OpenRead(zip))
                    foreach (var entry in archive.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(gameDir, entry.FullName));
                        if (!target.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase)) continue;   // no paths outside the game
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                File.Delete(zip);
                return tag;
            }
        }

        public static void Remove(string gameDir)
        {
            if (Process.GetProcessesByName("stbc").Length > 0) throw new Exception("Please close Star Trek: Bridge Crew first.");
            foreach (var d in new[] { "STBC Voice Crew", "StbcAiCrew" })
            {
                string p = Path.Combine(gameDir, "BepInEx", "plugins", d);
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }
        }
    }

    sealed class SetupForm : Form
    {
        readonly ComboBox _games = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
        readonly Button _browse = new Button { Text = "Browse …", Width = 90 };
        readonly Button _install = new Button { Text = "Install / Update", Width = 140, Height = 32 };
        readonly Button _remove = new Button { Text = "Remove", Width = 90, Height = 32 };
        readonly Label _status = new Label { AutoSize = false, Width = 560, Height = 60 };

        public SetupForm()
        {
            Text = "STBC Voice Crew Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 230);
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label { Text = "STBC Voice Crew – voice commands and an AI First Officer for Star Trek: Bridge Crew", AutoSize = true, Location = new Point(20, 18) };
            var where = new Label { Text = "Game folder:", AutoSize = true, Location = new Point(20, 58) };
            _games.Location = new Point(20, 80);
            _browse.Location = new Point(490, 79);
            _install.Location = new Point(20, 120);
            _remove.Location = new Point(170, 120);
            _status.Location = new Point(20, 165);
            Controls.AddRange(new Control[] { title, where, _games, _browse, _install, _remove, _status });

            foreach (var g in Program.FindGame()) _games.Items.Add(g);
            if (_games.Items.Count > 0) _games.SelectedIndex = 0;
            ShowState();

            _games.SelectedIndexChanged += (s, e) => ShowState();
            _browse.Click += (s, e) => Browse();
            _install.Click += async (s, e) => await Run(true);
            _remove.Click += async (s, e) => await Run(false);
        }

        string GameDir => _games.SelectedItem as string;

        void ShowState()
        {
            if (GameDir == null) { _status.Text = "Star Trek: Bridge Crew was not found automatically. Please choose stbc.exe with “Browse …”."; return; }
            string v = Program.InstalledVersion(GameDir);
            _status.Text = v == null ? "Not installed yet." : "Installed version: " + v;
        }

        void Browse()
        {
            using (var dlg = new OpenFileDialog { Title = "Please select stbc.exe (Star Trek: Bridge Crew)", Filter = "stbc.exe|stbc.exe" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string dir = Path.GetDirectoryName(dlg.FileName);
                    if (!_games.Items.Contains(dir)) _games.Items.Add(dir);
                    _games.SelectedItem = dir;
                }
        }

        async Task Run(bool install)
        {
            if (GameDir == null) { Browse(); if (GameDir == null) return; }
            _install.Enabled = _remove.Enabled = false;
            try
            {
                if (install)
                {
                    string tag = await Program.Install(GameDir, s => _status.Text = s);
                    _status.Text = "STBC Voice Crew " + tag + " installed. Start the game as usual – speech models and voices are downloaded on first use.";
                }
                else
                {
                    Program.Remove(GameDir);
                    _status.Text = "STBC Voice Crew removed. BepInEx stays in the game folder (other mods may use it).";
                }
            }
            catch (Exception ex) { _status.Text = "Error: " + ex.Message; }
            finally { _install.Enabled = _remove.Enabled = true; }
        }
    }
}
