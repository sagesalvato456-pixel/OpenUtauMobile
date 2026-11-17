// OpenUtau.Core/Vocaloid/VocaloidSampleBank.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Lee las vocales y nasales sostenidas REALES de un banco VOCALOID V4 (.ddi + .ddb) y las
// canta con TD-PSOLA a la altura pedida. El resto (consonantes y transiciones) sigue
// sintetizándose con Flame.
//
// Formato verificado con el banco MIKU_V4X_EVEC (ver VOCALOID-SUPPORT.md):
//  .ddb  cadena de bloques [tag(4) tamaño(u32) ...]; FRM2 = frame de 256 muestras
//        (f0 como float en +0x1088); "SND " = audio PCM int16 mono 44100 Hz con cabecera de
//        18 bytes [tag, tamaño, 44100, canales(u16), muestras(u32)].
//  .ddi  registros "EpR" 0xFFFFFFFF N, N punteros u64 a frames, y luego
//        [44100(u32) canales(u16) muestras(u32) puntero_u64]. El puntero apunta a la muestra
//        1024 del SND, de modo que el SND empieza 2066 bytes antes (18 + 2*1024). El PCM
//        trae 4 frames de margen antes y 4 después de los N frames listados.
//        Tras 16 bytes y un u32=1 viene: [c(1 byte) nlen(u32) nombre]. Cada fonema tiene 4
//        registros (escalera de alturas ~C4, E4, G4, C5) y el nombre va en el cuarto.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    internal sealed class VocaloidSampleBank {

        private const int kSampleRate = 44100;
        private const int kPreRoll = 1024;
        private const int kPcmOffset = 2066;

        private sealed class Rec {
            public string Name = string.Empty;
            public long SndStart;
            public int Samples;
            public long[] Frames = Array.Empty<long>();
            public double F0;
            public float[]? Pcm;
            public int[]? Marks;
            public int RegionStart;
            public int RegionEnd;
            public bool Failed;
        }

        private static readonly Dictionary<string, VocaloidSampleBank?> cache
            = new Dictionary<string, VocaloidSampleBank?>(StringComparer.Ordinal);

        private readonly string _ddbPath;
        private readonly object _lock = new object();
        private readonly Dictionary<string, List<Rec>> _byName
            = new Dictionary<string, List<Rec>>(StringComparer.Ordinal);

        private VocaloidSampleBank(string ddbPath) {
            _ddbPath = ddbPath;
        }

        /// <summary>Devuelve el banco de muestras del cantante o null si no se puede leer.</summary>
        public static VocaloidSampleBank? ForSinger(VocaloidSinger singer) {
            try {
                singer.EnsureLoaded();
                string ddi = singer.DdiPath;
                string ddb = singer.DdbPath;
                if (string.IsNullOrEmpty(ddi) || string.IsNullOrEmpty(ddb) || !File.Exists(ddi) || !File.Exists(ddb)) {
                    return null;
                }
                lock (cache) {
                    if (cache.TryGetValue(ddi, out VocaloidSampleBank? hit)) return hit;
                }
                VocaloidSampleBank? bank = Load(ddi, ddb);
                lock (cache) {
                    cache[ddi] = bank;
                }
                return bank;
            } catch (Exception ex) {
                Log.Warning(ex, "[VocaloidSampleBank] cannot open the bank samples");
                return null;
            }
        }

        private static VocaloidSampleBank? Load(string ddiPath, string ddbPath) {
            byte[] ddi = File.ReadAllBytes(ddiPath);
            var recs = new List<Rec>();
            for (int p = 0; p + 19 < ddi.Length; p++) {
                if (ddi[p] != 0x45 || ddi[p + 1] != 0x70 || ddi[p + 2] != 0x52) continue;
                if (ddi[p + 3] != 0xFF || ddi[p + 4] != 0xFF || ddi[p + 5] != 0xFF || ddi[p + 6] != 0xFF) continue;
                uint n = BitConverter.ToUInt32(ddi, p + 7);
                if (n == 0 || n > 5000) continue;
                long q = p + 11 + 8L * n;
                if (q + 18 + 16 + 9 + 16 > ddi.Length) continue;
                int qi = (int)q;
                uint rate = BitConverter.ToUInt32(ddi, qi);
                ushort channels = BitConverter.ToUInt16(ddi, qi + 4);
                uint samples = BitConverter.ToUInt32(ddi, qi + 6);
                ulong pcm = BitConverter.ToUInt64(ddi, qi + 10);
                if (rate != kSampleRate || channels != 1 || samples < 2048 || samples > 1000000 || pcm < kPcmOffset) continue;

                var rec = new Rec { Samples = (int)samples, SndStart = (long)pcm - kPcmOffset, Frames = new long[n] };
                for (int k = 0; k < (int)n; k++) rec.Frames[k] = (long)BitConverter.ToUInt64(ddi, p + 11 + 8 * k);
                int t = qi + 18 + 16;
                uint nameLen = BitConverter.ToUInt32(ddi, t + 5);
                if (nameLen > 0 && nameLen < 16 && t + 9 + nameLen <= ddi.Length) {
                    rec.Name = Encoding.Latin1.GetString(ddi, t + 9, (int)nameLen);
                }
                recs.Add(rec);
                p = qi + 17;
            }

            // el nombre aparece solo en el último registro de cada grupo de 4: se reparte hacia atrás
            var pending = new List<Rec>();
            foreach (Rec r in recs) {
                pending.Add(r);
                if (r.Name.Length > 0) {
                    foreach (Rec x in pending) x.Name = r.Name;
                    pending.Clear();
                }
            }

            var bank = new VocaloidSampleBank(ddbPath);
            using (var fs = new FileStream(ddbPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                var buf = new byte[4];
                foreach (Rec r in recs) {
                    if (r.Name.Length == 0) continue;
                    r.F0 = MedianF0(fs, r.Frames, buf);
                    if (r.F0 <= 0) continue;
                    if (!bank._byName.TryGetValue(r.Name, out List<Rec>? list)) {
                        list = new List<Rec>();
                        bank._byName[r.Name] = list;
                    }
                    list.Add(r);
                }
            }
            Log.Information($"[VocaloidSampleBank] {recs.Count} stationary samples, {bank._byName.Count} phonemes");
            return bank._byName.Count > 0 ? bank : null;
        }

        private static double ReadFrameF0(Stream fs, long ptr, byte[] buf) {
            if (ptr < 0 || ptr + 0x1090 > fs.Length) return 0.0;
            fs.Seek(ptr + 4, SeekOrigin.Begin);
            if (fs.Read(buf, 0, 4) != 4) return 0.0;
            if (BitConverter.ToUInt32(buf, 0) < 0x1090) return 0.0;   // frame vacío (sin voz)
            fs.Seek(ptr + 0x1088, SeekOrigin.Begin);
            if (fs.Read(buf, 0, 4) != 4) return 0.0;
            float f = BitConverter.ToSingle(buf, 0);
            return (f >= 50f && f <= 1500f) ? f : 0.0;
        }

        private static double MedianF0(Stream fs, long[] frames, byte[] buf) {
            var values = new List<double>();
            int step = Math.Max(1, frames.Length / 9);
            for (int i = 0; i < frames.Length; i += step) {
                double f = ReadFrameF0(fs, frames[i], buf);
                if (f > 0) values.Add(f);
            }
            if (values.Count == 0) return 0.0;
            values.Sort();
            return values[values.Count / 2];
        }

        /// <summary>Fonema del catálogo a nombre del banco japonés, o null si no hay muestra.</summary>
        private static string? MapSymbol(string s) {
            switch (s) {
                case "a": case "A": case "aa": case "{": case "ae": case "V": case "ah": case "@": return "a";
                case "i": case "I": case "ih": case "iy": case "i:": return "i";
                case "e": case "E": case "eh": case "e:": return "e";
                case "o": case "O": case "ao": case "o:": return "o";
                case "M": case "u": case "U": case "uh": case "uw": case "u:": return "M";
                case "n": return "n";
                case "m": return "m";
                case "m'": return "m'";
                case "N": case "ng": return "N";
                case "N'": return "N'";
                case "J": case "nj": return "J";
                default: return null;
            }
        }

        private bool Prepare(Rec rec) {
            if (rec.Pcm != null) return true;
            if (rec.Failed) return false;
            try {
                using var fs = new FileStream(_ddbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var head = new byte[18];
                fs.Seek(rec.SndStart, SeekOrigin.Begin);
                if (fs.Read(head, 0, 18) != 18 || head[0] != (byte)'S' || head[1] != (byte)'N' || head[2] != (byte)'D' || head[3] != (byte)' ') {
                    rec.Failed = true;
                    return false;
                }
                int ns = (int)BitConverter.ToUInt32(head, 14);
                if (ns != rec.Samples) { rec.Failed = true; return false; }

                var raw = new byte[ns * 2];
                int got = 0;
                while (got < raw.Length) {
                    int n = fs.Read(raw, got, raw.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got != raw.Length) { rec.Failed = true; return false; }
                var pcm = new float[ns];
                for (int i = 0; i < ns; i++) pcm[i] = BitConverter.ToInt16(raw, i * 2) / 32768f;

                int nf = rec.Frames.Length;
                var f0 = new double[nf];
                var buf = new byte[4];
                for (int k = 0; k < nf; k++) f0[k] = ReadFrameF0(fs, rec.Frames[k], buf);
                double last = rec.F0;
                for (int k = 0; k < nf; k++) {
                    if (f0[k] <= 0) f0[k] = last; else last = f0[k];
                }

                int start = kPreRoll;
                int end = Math.Min(ns - 1, kPreRoll + nf * 256);
                var marks = new List<int>();
                double pos = start;
                while (pos < end) {
                    int fi = Math.Min(nf - 1, (int)((pos - kPreRoll) / 256.0));
                    double period = kSampleRate / f0[fi];
                    int c = (int)Math.Round(pos);
                    int lo = Math.Max(0, c - (int)(period / 4));
                    int hi = Math.Min(ns - 1, c + (int)(period / 4));
                    int peak = lo;
                    float best = float.MinValue;
                    for (int j = lo; j <= hi; j++) {
                        if (pcm[j] > best) { best = pcm[j]; peak = j; }
                    }
                    if (marks.Count == 0 || peak > marks[marks.Count - 1]) marks.Add(peak);
                    pos += period;
                }
                if (marks.Count < 4 || end - start < 1024) { rec.Failed = true; return false; }

                rec.Marks = marks.ToArray();
                rec.RegionStart = start;
                rec.RegionEnd = end;
                rec.Pcm = pcm;
                return true;
            } catch (Exception ex) {
                rec.Failed = true;
                Log.Warning(ex, $"[VocaloidSampleBank] cannot read sample {rec.Name}");
                return false;
            }
        }

        private static int Nearest(int[] marks, int target) {
            int idx = Array.BinarySearch(marks, target);
            if (idx >= 0) return idx;
            idx = ~idx;
            if (idx <= 0) return 0;
            if (idx >= marks.Length) return marks.Length - 1;
            return (target - marks[idx - 1] <= marks[idx] - target) ? idx - 1 : idx;
        }

        /// <summary>
        /// Canta el fonema durante length muestras siguiendo la curva de pitch f0At(i) en Hz.
        /// Devuelve null si el banco no tiene muestra para ese símbolo.
        /// </summary>
        public float[]? Render(string symbol, int length, Func<int, double> f0At) {
            string? name = MapSymbol(symbol);
            if (name == null || length < 200) return null;
            if (!_byName.TryGetValue(name, out List<Rec>? list)) return null;

            double target = Math.Clamp(f0At(length / 2), 55.0, 1400.0);
            Rec? chosen = null;
            double bestDist = double.MaxValue;
            foreach (Rec r in list) {
                double d = Math.Abs(Math.Log(target / r.F0));
                if (d < bestDist) { bestDist = d; chosen = r; }
            }
            if (chosen == null) return null;
            lock (_lock) {
                if (!Prepare(chosen)) return null;
            }

            float[] pcm = chosen.Pcm!;
            int[] marks = chosen.Marks!;
            int regionStart = chosen.RegionStart;
            int regionLen = chosen.RegionEnd - chosen.RegionStart;
            var buf = new float[length + 4096];

            double outPos = 0.0;
            double srcPos = 0.0;
            while (outPos < length) {
                double ft = Math.Clamp(f0At((int)outPos), 55.0, 1400.0);
                double tt = Math.Clamp(kSampleRate / ft, 25.0, 800.0);
                double s = srcPos % (2.0 * regionLen);
                if (s > regionLen) s = 2.0 * regionLen - s;
                int src = regionStart + (int)s;
                int j = Nearest(marks, src);
                int m = marks[j];
                int period = j + 1 < marks.Length ? marks[j + 1] - m : (j > 0 ? m - marks[j - 1] : (int)tt);
                period = Math.Clamp(period, 20, 1000);
                double gain = Math.Clamp(tt / period, 0.5, 2.0);
                int oi = (int)Math.Round(outPos);
                for (int k = -period; k <= period; k++) {
                    int si = m + k;
                    int di = oi + k;
                    if (si < 0 || si >= pcm.Length || di < 0 || di >= buf.Length) continue;
                    double w = 0.5 + 0.5 * Math.Cos(Math.PI * k / period);
                    buf[di] += (float)(pcm[si] * w * gain);
                }
                outPos += tt;
                srcPos += tt;
            }

            var result = new float[length];
            int fadeIn = Math.Min(530, length / 3);
            int fadeOut = Math.Min(1100, length / 3);
            for (int i = 0; i < length; i++) {
                double e = 1.0;
                if (i < fadeIn) e = 0.5 - 0.5 * Math.Cos(Math.PI * i / fadeIn);
                int fromEnd = length - 1 - i;
                if (fromEnd < fadeOut) e *= 0.5 - 0.5 * Math.Cos(Math.PI * fromEnd / fadeOut);
                result[i] = (float)(buf[i] * e);
            }
            return result;
        }
    }
}
