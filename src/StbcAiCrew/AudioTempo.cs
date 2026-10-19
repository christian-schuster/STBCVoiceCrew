using System;

namespace StbcAiCrew
{
    /// <summary>
    /// Sprechtempo ändern, ohne die Tonhöhe zu verschieben (WSOLA: überlappende Blöcke, jeweils an der
    /// ähnlichsten Stelle angesetzt), und Stille am Anfang und Ende kappen.
    /// </summary>
    public static class AudioTempo
    {
        /// <summary>Grundtempo für alle Qwen-Stimmen (aus der Konfiguration), mal Tempo des Charakters.</summary>
        public static float GlobalTempo = 1.25f;

        public static short[] Process(short[] pcm, int rate, float tempo)
        {
            if (pcm == null || pcm.Length == 0 || rate <= 0) return pcm;
            pcm = TrimSilence(pcm, rate);
            if (tempo < 1.02f && tempo > 0.98f) return pcm;
            return Stretch(pcm, rate, Math.Max(0.6f, Math.Min(1.6f, tempo)));
        }

        /// <summary>Stille vorne und hinten bis auf 150 ms kürzen.</summary>
        static short[] TrimSilence(short[] pcm, int rate)
        {
            const int threshold = 250;
            int keep = rate * 150 / 1000;
            int start = 0, end = pcm.Length - 1;
            while (start < pcm.Length && Math.Abs((int)pcm[start]) < threshold) start++;
            while (end > start && Math.Abs((int)pcm[end]) < threshold) end--;
            if (end <= start) return pcm;
            start = Math.Max(0, start - keep);
            end = Math.Min(pcm.Length - 1, end + keep);
            if (start == 0 && end == pcm.Length - 1) return pcm;
            var r = new short[end - start + 1];
            Array.Copy(pcm, start, r, 0, r.Length);
            return r;
        }

        static short[] Stretch(short[] src, int rate, float tempo)
        {
            int frame = rate * 30 / 1000;        // 30-ms-Blöcke
            int overlap = frame / 2;
            int hopOut = frame - overlap;
            int search = rate * 8 / 1000;         // ±8 ms Suchbereich
            if (src.Length < frame * 3) return src;

            int outLen = (int)(src.Length / tempo) + frame * 2;
            var dst = new float[outLen];
            var window = new float[overlap];
            for (int i = 0; i < overlap; i++) window[i] = (float)i / overlap;

            // erster Block unverändert
            for (int i = 0; i < frame; i++) dst[i] = src[i];
            int outPos = hopOut;
            int prevIn = 0;
            int written = frame;
            int block = 0;

            while (true)
            {
                // Sollposition absolut aus der Blocknummer, nicht relativ zum letzten Block:
                // sonst zieht die Ähnlichkeitssuche das Tempo immer wieder auf 1 zurück
                block++;
                int nominal = (int)Math.Round(block * hopOut * (double)tempo);
                if (nominal + frame + search >= src.Length) break;

                // Stelle suchen, die am besten an das Ende des letzten Blocks anschließt
                int bestOff = 0;
                double best = double.MinValue;
                int refStart = prevIn + hopOut;   // natürliche Fortsetzung des vorigen Blocks
                for (int off = -search; off <= search; off += 2)
                {
                    int cand = nominal + off;
                    if (cand < 0) continue;
                    double corr = 0;
                    for (int i = 0; i < overlap; i += 2) corr += (double)src[cand + i] * src[refStart + i];
                    if (corr > best) { best = corr; bestOff = off; }
                }
                int inPos = nominal + bestOff;
                if (outPos + frame >= outLen) break;   // sollte nicht vorkommen

                for (int i = 0; i < overlap; i++)
                    dst[outPos + i] = dst[outPos + i] * (1f - window[i]) + src[inPos + i] * window[i];
                for (int i = overlap; i < frame; i++) dst[outPos + i] = src[inPos + i];

                written = outPos + frame;
                prevIn = inPos;
                outPos += hopOut;
            }

            // Rest hinter dem letzten Block mit Überblendung anhängen
            int tail = prevIn + frame;
            if (tail < src.Length)
            {
                int extra = src.Length - tail;
                if (written + extra > dst.Length) Array.Resize(ref dst, written + extra);
                for (int i = 0; i < extra; i++) dst[written + i] = src[tail + i];
                written += extra;
            }

            var r = new short[written];
            for (int i = 0; i < written; i++)
            {
                float v = dst[i];
                r[i] = (short)(v > 32767f ? 32767 : v < -32768f ? -32768 : v);
            }
            return r;
        }
    }
}
