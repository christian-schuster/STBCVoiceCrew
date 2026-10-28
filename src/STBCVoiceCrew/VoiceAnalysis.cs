using System;
using System.Collections.Generic;

namespace StbcAiCrew
{
    /// <summary>Einfache Stimmanalyse, damit entworfene Stimmen nicht piepsig ausfallen.</summary>
    public static class VoiceAnalysis
    {
        /// <summary>
        /// Median der Grundfrequenz stimmhafter Abschnitte in Hz (Autokorrelation, 60–450 Hz).
        /// Typisch: tiefe Männerstimmen 85–130 Hz, Frauenstimmen 165–255 Hz; darüber klingt es schnell piepsig.
        /// </summary>
        public static float MedianPitch(short[] s, int rate)
        {
            if (s == null || rate <= 0) return 0f;
            int win = rate / 25, hop = rate / 50;
            int minLag = rate / 450, maxLag = rate / 60;
            var f0 = new List<float>();
            for (int start = 0; start + win + maxLag < s.Length; start += hop)
            {
                double e = 0;
                for (int i = 0; i < win; i++) e += (double)s[start + i] * s[start + i];
                if (Math.Sqrt(e / win) < 800) continue;   // leise/stimmlos

                double best = 0;
                int bestLag = 0;
                for (int lag = minLag; lag <= maxLag; lag++)
                {
                    double c = 0;
                    for (int i = 0; i < win; i++) c += (double)s[start + i] * s[start + i + lag];
                    if (c > best) { best = c; bestLag = lag; }
                }
                if (bestLag > 0 && best / e > 0.5) f0.Add(rate / (float)bestLag);
            }
            if (f0.Count == 0) return 0f;
            f0.Sort();
            return f0[f0.Count / 2];
        }
    }
}
