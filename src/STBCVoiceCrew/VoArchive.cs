using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace STBCVoiceCrew
{
    /// <summary>
    /// Findet die Aufnahme zu einer Sprachausgabe des Spiels und liefert sie als 16-kHz-Mono-PCM (für Whisper).
    /// Wwise: SoundbanksInfo.xml ordnet Dateinamen (= Ereignisname ohne "Play_") je Sprache eine Datei-ID zu;
    /// die Daten liegen entweder in einer Soundbank (DIDX/DATA) oder im Streaming-Archiv (AKPK).
    /// Die Sprachaufnahmen sind unkomprimiertes PCM (WAVE_FORMAT_EXTENSIBLE, 48 kHz).
    /// </summary>
    public sealed class VoArchive
    {
        struct Loc { public string File; public long Offset; public int Size; }

        readonly Dictionary<string, uint> _idByName = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<uint, Loc> _loc = new Dictionary<uint, Loc>();
        public string Language { get; private set; }
        public int Count => _idByName.Count;

        /// <summary>banksDir = ...\StreamingAssets\Audio\GeneratedSoundBanks\Windows, language = "German" usw.</summary>
        public static VoArchive Load(string banksDir, string language)
        {
            var a = new VoArchive { Language = language };
            a.ReadInfo(Path.Combine(banksDir, "SoundbanksInfo.xml"), language);
            string langDir = Path.Combine(banksDir, language);
            if (Directory.Exists(langDir))
                foreach (var bnk in Directory.GetFiles(langDir, "*.bnk")) a.ReadBank(bnk);
            foreach (var pck in Directory.GetFiles(banksDir, "*.pck")) a.ReadPck(pck, language);
            return a;
        }

        static readonly Regex FileRx = new Regex("<File Id=\"(\\d+)\" Language=\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex NameRx = new Regex("<ShortName>([^<]+)</ShortName>", RegexOptions.Compiled);

        void ReadInfo(string xml, string language)
        {
            uint id = 0;
            bool want = false;
            using (var r = new StreamReader(xml, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    var m = FileRx.Match(line);
                    if (m.Success) { want = m.Groups[2].Value == language; id = uint.Parse(m.Groups[1].Value); continue; }
                    if (!want) continue;
                    var n = NameRx.Match(line);
                    if (!n.Success) continue;
                    string name = n.Groups[1].Value;
                    if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    _idByName[name] = id;
                    want = false;
                }
            }
        }

        void ReadBank(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                var didx = new List<uint[]>();
                long dataStart = -1;
                while (fs.Position + 8 <= fs.Length)
                {
                    string tag = Encoding.ASCII.GetString(br.ReadBytes(4));
                    uint size = br.ReadUInt32();
                    long start = fs.Position;
                    if (tag == "DIDX")
                        for (uint i = 0; i < size / 12; i++) didx.Add(new[] { br.ReadUInt32(), br.ReadUInt32(), br.ReadUInt32() });
                    else if (tag == "DATA") dataStart = start;
                    fs.Position = start + size;
                    if (dataStart >= 0 && didx.Count > 0) break;
                }
                if (dataStart < 0) return;
                foreach (var e in didx) _loc[e[0]] = new Loc { File = path, Offset = dataStart + e[1], Size = (int)e[2] };
            }
        }

        void ReadPck(string path, string language)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "AKPK") return;
                br.ReadUInt32();                  // Kopfgröße
                br.ReadUInt32();                  // Version
                uint langSize = br.ReadUInt32();
                uint banksSize = br.ReadUInt32();
                uint soundsSize = br.ReadUInt32();
                uint externalsSize = br.ReadUInt32();
                long langStart = fs.Position;

                // Sprachtabelle: Anzahl, (Offset, ID)-Paare, Namen (UTF-16 oder ASCII) relativ zum Tabellenanfang
                uint langId = uint.MaxValue;
                uint nLang = br.ReadUInt32();
                var entries = new List<uint[]>();
                for (uint i = 0; i < nLang; i++) entries.Add(new[] { br.ReadUInt32(), br.ReadUInt32() });
                foreach (var e in entries)
                {
                    fs.Position = langStart + e[0];
                    string name = ReadLangName(br);
                    if (string.Equals(name, language, StringComparison.OrdinalIgnoreCase)) langId = e[1];
                }
                long soundsStart = langStart + langSize + banksSize;
                fs.Position = soundsStart;
                uint n = br.ReadUInt32();
                for (uint i = 0; i < n; i++)
                {
                    uint id = br.ReadUInt32(), block = br.ReadUInt32(), size = br.ReadUInt32(), startBlock = br.ReadUInt32(), lang = br.ReadUInt32();
                    if (lang != langId && lang != 0) continue;   // 0 = sprachneutral (SFX)
                    if (lang == 0 && _loc.ContainsKey(id)) continue;
                    _loc[id] = new Loc { File = path, Offset = (long)startBlock * Math.Max(1u, block), Size = (int)size };
                }
            }
        }

        static string ReadLangName(BinaryReader br)
        {
            var b1 = br.ReadByte(); var b2 = br.ReadByte();
            var sb = new StringBuilder();
            if (b2 == 0)   // UTF-16LE
            {
                while (b1 != 0 || b2 != 0) { sb.Append((char)(b1 | (b2 << 8))); b1 = br.ReadByte(); b2 = br.ReadByte(); }
            }
            else
            {
                sb.Append((char)b1);
                while (b2 != 0) { sb.Append((char)b2); b2 = br.ReadByte(); }
            }
            return sb.ToString();
        }

        /// <summary>Ereignisname ("Play_VO_...") → Dateiname der Aufnahme.</summary>
        public static string MediaName(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return null;
            return eventName.StartsWith("Play_", StringComparison.OrdinalIgnoreCase) ? eventName.Substring(5) : eventName;
        }

        public bool Has(string eventName)
        {
            uint id;
            return _idByName.TryGetValue(MediaName(eventName) ?? "", out id) && _loc.ContainsKey(id);
        }

        /// <summary>Aufnahme als 16-kHz-Mono, oder null.</summary>
        public short[] Pcm16k(string eventName)
        {
            uint id;
            Loc loc;
            if (!_idByName.TryGetValue(MediaName(eventName) ?? "", out id) || !_loc.TryGetValue(id, out loc)) return null;
            byte[] data;
            using (var fs = File.OpenRead(loc.File))
            {
                fs.Position = loc.Offset;
                data = new byte[loc.Size];
                int read = 0;
                while (read < data.Length) { int r = fs.Read(data, read, data.Length - read); if (r <= 0) break; read += r; }
            }
            return Decode(data);
        }

        static short[] Decode(byte[] d)
        {
            if (d.Length < 12 || Encoding.ASCII.GetString(d, 0, 4) != "RIFF") return null;
            int channels = 1, rate = 48000, bits = 16, fmtTag = 1;
            int pos = 12, dataOff = -1, dataLen = 0;
            while (pos + 8 <= d.Length)
            {
                string tag = Encoding.ASCII.GetString(d, pos, 4);
                int len = BitConverter.ToInt32(d, pos + 4);
                if (tag == "fmt ")
                {
                    fmtTag = BitConverter.ToUInt16(d, pos + 8);
                    channels = BitConverter.ToUInt16(d, pos + 10);
                    rate = BitConverter.ToInt32(d, pos + 12);
                    bits = BitConverter.ToUInt16(d, pos + 22);
                }
                else if (tag == "data") { dataOff = pos + 8; dataLen = Math.Min(len, d.Length - dataOff); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataOff < 0 || bits != 16 || (fmtTag != 1 && fmtTag != 0xFFFE) || channels < 1) return null;   // nur PCM

            int frames = dataLen / (2 * channels);
            double step = rate / 16000.0;
            int outLen = (int)(frames / step);
            var o = new short[outLen];
            for (int i = 0; i < outLen; i++)
            {
                // Mittelwert über das Quellfenster (einfacher Tiefpass) und über die Kanäle
                int a = (int)(i * step), b = Math.Min(frames, (int)((i + 1) * step));
                long sum = 0; int cnt = 0;
                for (int f = a; f < b; f++)
                    for (int c = 0; c < channels; c++) { sum += BitConverter.ToInt16(d, dataOff + (f * channels + c) * 2); cnt++; }
                o[i] = cnt > 0 ? (short)(sum / cnt) : (short)0;
            }
            return o;
        }
    }
}
