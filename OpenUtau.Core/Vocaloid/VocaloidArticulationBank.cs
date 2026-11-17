// OpenUtau.Core/Vocaloid/VocaloidArticulationBank.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Lee las CONSONANTES reales de un banco VOCALOID V4 (.ddi + .ddb): las articulaciones "ART".
// Formato descifrado con MIKU_V4X_EVEC (ver VOCALOID-SUPPORT.md, sección Articulations):
//
//  .ddi  raíz  "ART " + 7 u32 [0, 1, 0, id, cuenta, 0, 0]
//        hijos "ARTu" (sin audio) y "ARTp" (con audio). Un ARTp contiene "EpR", N punteros u64
//        a frames del .ddb, luego [44100 u32][canales u16][muestras u32][puntero PCM u64], y mas
//        adelante la palabra "default" seguida de nombres [u32 largo][ascii].
//  .ddb  el bloque "SND " de una articulación empieza en puntero-18 y el audio arranca EN el
//        puntero (en las vocales sostenidas el puntero cae 1024 muestras dentro; aquí no).
//        El frame EpR k coincide con el PCM desde la muestra k*256 (desfase medido 0 a 2 frames).
//        El PCM trae N+8 frames: los 8 extra son COLA, no relleno al inicio. En las oclusivas
//        esa cola contiene la liberación.
//  Cada fonema tiene 4 capas de tono (~C4, E4, G4, C5) y solo la última lleva el nombre.
//  Las raíces 0 a 4 son V->C: arrancan con la vocal sostenida a, i, M, e, o y terminan en la
//  consonante. De cada clip se recorta solo la parte consonántica: desde donde la energía cae
//  respecto a la vocal hasta que vuelve la voz (o el final).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    internal sealed class VocaloidArticulationBank {

        private const int kSampleRate = 44100;
        private const int kFrame = 256;
        private const int kSndHeader = 18;

        /// <summary>Muestras que la consonante se mete dentro de la vocal siguiente (~5 ms).</summary>
        public const int OverlapSamples = 220;

        private static readonly byte[] EpRMark = { 0x45, 0x70, 0x52 };
        private static readonly byte[] DefaultMark = Encoding.ASCII.GetBytes("default");

        private sealed class Rec {
            public long PcmPos;
            public int Samples;
            public long[] Frames = Array.Empty<long>();
            public double F0;
            public bool F0Done;
            public float[]? Part;
            public bool Failed;
        }

        private static readonly Dictionary<string, VocaloidArticulationBank?> cache
            = new Dictionary<string, VocaloidArticulationBank?>(StringComparer.Ordinal);

        private readonly string _ddbPath;
        private readonly object _lock = new object();

        // contexto de vocal ("a","i","M","e","o") -> consonante -> 4 capas de tono
        private readonly Dictionary<string, Dictionary<string, Rec[]>> _byContext
            = new Dictionary<string, Dictionary<string, Rec[]>>(StringComparer.Ordinal);

        private VocaloidArticulationBank(string ddbPath) {
            _ddbPath = ddbPath;
        }

        /// <summary>Devuelve el banco de consonantes del cantante o null si no se puede leer.</summary>
        public static VocaloidArticulationBank? ForSinger(VocaloidSinger singer) {
            try {
                singer.EnsureLoaded();
                string ddi = singer.DdiPath;
                string ddb = singer.DdbPath;
                if (string.IsNullOrEmpty(ddi) || string.IsNullOrEmpty(ddb) || !File.Exists(ddi) || !File.Exists(ddb)) {
                    return null;
                }
                lock (cache) {
                    if (cache.TryGetValue(ddi, out VocaloidArticulationBank? hit)) return hit;
                }
                VocaloidArticulationBank? bank = Load(ddi, ddb);
                lock (cache) {
                    cache[ddi] = bank;
                }
                return bank;
            } catch (Exception ex) {
                Log.Warning(ex, "[VocaloidArticulationBank] cannot open the bank articulations");
                return null;
            }
        }

        // ── lectura de bytes ──────────────────────────────────────────────

        private static uint U32(byte[] d, int o) { return BitConverter.ToUInt32(d, o); }
        private static ushort U16(byte[] d, int o) { return BitConverter.ToUInt16(d, o); }
        private static ulong U64(byte[] d, int o) { return BitConverter.ToUInt64(d, o); }

        private static int IndexOf(byte[] d, byte[] pat, int from, int to) {
            int last = Math.Min(to, d.Length) - pat.Length;
            for (int i = from; i <= last; i++) {
                int k = 0;
                while (k < pat.Length && d[i + k] == pat[k]) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }

        private static bool IsRoot(byte[] d, int p) {
            if (p + 32 > d.Length) return false;
            return U32(d, p + 4) == 0 && U32(d, p + 8) == 1 && U32(d, p + 12) == 0
                && U32(d, p + 24) == 0 && U32(d, p + 28) == 0;
        }

        private static bool ParseChild(byte[] ddi, int a, int b, out Rec? rec, out string[] names) {
            rec = null;
            names = Array.Empty<string>();
            int e = IndexOf(ddi, EpRMark, a, b);
            if (e < 0 || e + 11 > ddi.Length) return false;
            long n;
            int ptr0;
            if (ddi[e + 3] == 0xFF && ddi[e + 4] == 0xFF && ddi[e + 5] == 0xFF && ddi[e + 6] == 0xFF) {
                n = U32(ddi, e + 7); ptr0 = e + 11;
            } else {
                n = U32(ddi, e + 3); ptr0 = e + 7;
            }
            if (n == 0 || n > 5000) return false;
            long qL = ptr0 + 8L * n;
            if (qL + 18 > ddi.Length) return false;
            int q = (int)qL;
            uint rate = U32(ddi, q);
            ushort channels = U16(ddi, q + 4);
            uint samples = U32(ddi, q + 6);
            ulong pcm = U64(ddi, q + 10);
            if (rate != kSampleRate || channels != 1 || samples < 512 || samples > 2000000 || pcm < kSndHeader) return false;

            var frames = new long[n];
            for (int k = 0; k < (int)n; k++) frames[k] = (long)U64(ddi, ptr0 + 8 * k);

            var list = new List<string>();
            int dpos = IndexOf(ddi, DefaultMark, q, b);
            if (dpos >= 0) {
                int i = dpos + DefaultMark.Length;
                while (i + 4 <= ddi.Length) {
                    uint ln = U32(ddi, i);
                    if (ln < 1 || ln > 15 || i + 4 + ln > ddi.Length) break;
                    bool ok = true;
                    for (int k = 0; k < (int)ln; k++) {
                        byte c = ddi[i + 4 + k];
                        if (c < 32 || c >= 127) { ok = false; break; }
                    }
                    if (!ok) break;
                    list.Add(Encoding.ASCII.GetString(ddi, i + 4, (int)ln));
                    i += 4 + (int)ln;
                }
            }
            rec = new Rec { PcmPos = (long)pcm, Samples = (int)samples, Frames = frames };
            names = list.ToArray();
            return true;
        }

        private static VocaloidArticulationBank? Load(string ddiPath, string ddbPath) {
            byte[] ddi = File.ReadAllBytes(ddiPath);

            // marcas ART: la primera raíz válida fija el inicio de la zona de articulaciones
            var tagPos = new List<int>();
            var tagIsRoot = new List<bool>();
            bool started = false;
            for (int p = 0; p + 40 < ddi.Length; p++) {
                if (ddi[p] != (byte)'A' || ddi[p + 1] != (byte)'R' || ddi[p + 2] != (byte)'T') continue;
                byte k = ddi[p + 3];
                if (k != (byte)' ' && k != (byte)'u' && k != (byte)'p') continue;
                if (!started) {
                    if (k != (byte)' ' || !IsRoot(ddi, p)) continue;
                    started = true;
                }
                tagPos.Add(p);
                tagIsRoot.Add(k == (byte)' ');
            }
            if (!started) return null;

            // grupos por raíz: las capas sin nombre heredan el del siguiente hijo con nombre
            var groupsByRoot = new List<List<(string name, List<Rec> recs)>>();
            var pending = new List<Rec>();
            int children = 0;
            for (int t = 0; t < tagPos.Count; t++) {
                if (tagIsRoot[t]) {
                    groupsByRoot.Add(new List<(string name, List<Rec> recs)>());
                    pending = new List<Rec>();
                    continue;
                }
                int a = tagPos[t];
                int b = t + 1 < tagPos.Count ? tagPos[t + 1] : ddi.Length;
                Rec? rec;
                string[] names;
                bool ok;
                try { ok = ParseChild(ddi, a, b, out rec, out names); } catch (Exception) { continue; }
                if (!ok || rec == null) continue;
                children++;
                pending.Add(rec);
                if (names.Length > 0) {
                    groupsByRoot[groupsByRoot.Count - 1].Add((names[0], pending));
                    pending = new List<Rec>();
                }
            }

            // contexto de cada raíz V->C: se deduce del nombre de su último grupo (a#2 -> a)
            var bank = new VocaloidArticulationBank(ddbPath);
            foreach (List<(string name, List<Rec> recs)> groups in groupsByRoot) {
                if (groups.Count < 30) continue;
                string last = groups[groups.Count - 1].name;
                int hash = last.IndexOf('#');
                string ctx = hash >= 0 ? last.Substring(0, hash) : last;
                if (ctx != "a" && ctx != "i" && ctx != "M" && ctx != "e" && ctx != "o") continue;
                if (bank._byContext.ContainsKey(ctx)) continue;
                var table = new Dictionary<string, Rec[]>(StringComparer.Ordinal);
                foreach ((string name, List<Rec> recs) g in groups) {
                    if (g.recs.Count != 4) continue;
                    if (g.name.IndexOf('#') >= 0 || g.name.StartsWith("*", StringComparison.Ordinal) || g.name == "Sil") continue;
                    if (table.ContainsKey(g.name)) continue;
                    table[g.name] = g.recs.ToArray();
                }
                if (table.Count > 0) bank._byContext[ctx] = table;
            }
            Log.Information($"[VocaloidArticulationBank] {children} articulation samples, {bank._byContext.Count} vowel contexts");
            return bank._byContext.Count > 0 ? bank : null;
        }

        // ── tono de cada capa ─────────────────────────────────────────────

        private static double ReadFrameF0(Stream fs, long ptr, byte[] buf) {
            if (ptr < 0 || ptr + 0x1090 > fs.Length) return 0.0;
            fs.Seek(ptr + 4, SeekOrigin.Begin);
            if (fs.Read(buf, 0, 4) != 4) return 0.0;
            if (BitConverter.ToUInt32(buf, 0) < 0x1090) return 0.0;
            fs.Seek(ptr + 0x1088, SeekOrigin.Begin);
            if (fs.Read(buf, 0, 4) != 4) return 0.0;
            float f = BitConverter.ToSingle(buf, 0);
            return (f >= 50f && f <= 1500f) ? f : 0.0;
        }

        private void EnsureF0(Rec[] group) {
            lock (_lock) {
                bool need = false;
                foreach (Rec r in group) { if (!r.F0Done) need = true; }
                if (!need) return;
                try {
                    using var fs = new FileStream(_ddbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var buf = new byte[4];
                    foreach (Rec r in group) {
                        if (r.F0Done) continue;
                        var vals = new List<double>();
                        int upto = Math.Max(1, (int)(r.Frames.Length * 0.6));   // la parte con vocal
                        int step = Math.Max(1, upto / 6);
                        for (int k = 0; k < upto; k += step) {
                            double f = ReadFrameF0(fs, r.Frames[k], buf);
                            if (f > 0) vals.Add(f);
                        }
                        vals.Sort();
                        r.F0 = vals.Count > 0 ? vals[vals.Count / 2] : 0.0;
                        r.F0Done = true;
                    }
                } catch (Exception ex) {
                    Log.Warning(ex, "[VocaloidArticulationBank] cannot read the pitch of an articulation");
                    foreach (Rec r in group) r.F0Done = true;
                }
            }
        }

        // ── recorte de la parte consonántica ──────────────────────────────

        /// <summary>
        /// Busca la parte consonántica de un clip V->C. Empieza 2 frames antes de que la energía caiga
        /// 4 dB por debajo de la vocal inicial y termina donde la voz vuelve (6 dB de la vocal) o al final.
        /// </summary>
        internal static bool FindCut(float[] pcm, out int start, out int end) {
            start = 0;
            end = pcm.Length;
            int nf = pcm.Length / kFrame;
            if (nf < 16) return false;
            var db = new double[nf];
            for (int i = 0; i < nf; i++) {
                double e = 0.0;
                int o = i * kFrame;
                for (int k = 0; k < kFrame; k++) {
                    double x = pcm[o + k];
                    e += x * x;
                }
                db[i] = 10.0 * Math.Log10(e / kFrame + 1e-12);
            }
            var first = new double[10];
            Array.Copy(db, first, 10);
            Array.Sort(first);
            double baseDb = first[5];

            int ri = -1;
            for (int i = 6; i < nf; i++) {
                if (db[i] < baseDb - 4.0) { ri = i; break; }
            }
            if (ri < 0) return false;

            int back = -1;
            for (int j = ri + 3; j < nf; j++) {
                if (db[j] >= baseDb - 6.0) { back = j; break; }
            }
            start = Math.Max(0, ri - 2) * kFrame;
            end = back >= 0 ? Math.Min(pcm.Length, back * kFrame + 64) : pcm.Length;
            return end - start >= 600;
        }

        private bool Prepare(Rec rec) {
            lock (_lock) {
                if (rec.Part != null) return true;
                if (rec.Failed) return false;
                try {
                    using var fs = new FileStream(_ddbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var head = new byte[kSndHeader];
                    fs.Seek(rec.PcmPos - kSndHeader, SeekOrigin.Begin);
                    if (fs.Read(head, 0, kSndHeader) != kSndHeader
                        || head[0] != (byte)'S' || head[1] != (byte)'N' || head[2] != (byte)'D' || head[3] != (byte)' ') {
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

                    if (!FindCut(pcm, out int start, out int end)) { rec.Failed = true; return false; }
                    var part = new float[end - start];
                    Array.Copy(pcm, start, part, 0, part.Length);
                    rec.Part = part;
                    return true;
                } catch (Exception ex) {
                    rec.Failed = true;
                    Log.Warning(ex, "[VocaloidArticulationBank] cannot read an articulation");
                    return false;
                }
            }
        }

        // ── nombres ───────────────────────────────────────────────────────

        /// <summary>Base de vocal del banco (a, i, M, e, o) para un símbolo del catálogo.</summary>
        internal static string VowelBase(string s) {
            switch (s) {
                case "i": case "I": case "ih": case "iy": case "i:": case "y": case "Y": return "i";
                case "e": case "E": case "eh": case "e:": return "e";
                case "o": case "O": case "ao": case "o:": return "o";
                case "M": case "u": case "U": case "uh": case "uw": case "u:": return "M";
                default: return "a";
            }
        }

        private static string? Alias(string s) {
            switch (s) {
                case "F": return "p\\";
                case "sh": return "S";
                case "zh": return "Z";
                case "ch": case "tC": case "tCh": return "tS";
                case "jh": case "jj": case "dZ'": return "dZ";
                case "kh": case "kk": return "k";
                case "th": case "tt": return "t";
                case "ph": case "pp": return "p";
                case "ss": return "s";
                case "H": case "hh": case "x": return "h";
                default: return null;
            }
        }

        private static string? ResolveName(string symbol, Dictionary<string, Rec[]> table) {
            if (table.ContainsKey(symbol)) return symbol;
            string? alias = Alias(symbol);
            if (alias != null && table.ContainsKey(alias)) return alias;
            return null;
        }

        private static float[] Resample(float[] src, double step) {
            int outLen = Math.Max(1, (int)(src.Length / step));
            var res = new float[outLen];
            for (int i = 0; i < outLen; i++) {
                double pos = i * step;
                int i0 = (int)pos;
                if (i0 >= src.Length - 1) { res[i] = src[src.Length - 1]; continue; }
                double frac = pos - i0;
                res[i] = (float)(src[i0] + (src[i0 + 1] - src[i0]) * frac);
            }
            return res;
        }

        // ── salida ────────────────────────────────────────────────────────

        /// <summary>
        /// Devuelve la parte consonántica del símbolo, o null si el banco no la tiene.
        /// vowelCtx es la vocal vecina (a, i, M, e, o). maxLen recorta el clip: keepEnd=true conserva el
        /// final (consonante que desemboca en una vocal), false conserva el principio (consonante final).
        /// </summary>
        public float[]? Render(string symbol, string vowelCtx, double targetF0, bool voiced, int maxLen, bool keepEnd) {
            if (maxLen < 128) return null;
            Dictionary<string, Rec[]>? table;
            if (!_byContext.TryGetValue(vowelCtx, out table)) {
                if (!_byContext.TryGetValue("a", out table)) return null;
            }
            if (table == null) return null;
            string? name = ResolveName(symbol, table);
            if (name == null) return null;
            Rec[] group = table[name];

            EnsureF0(group);
            double tgt = Math.Clamp(targetF0, 55.0, 1400.0);
            Rec? chosen = null;
            double best = double.MaxValue;
            foreach (Rec r in group) {
                if (r.F0 <= 0) continue;
                double d = Math.Abs(Math.Log(tgt / r.F0));
                if (d < best) { best = d; chosen = r; }
            }
            if (chosen == null) return null;
            if (!Prepare(chosen)) return null;

            float[] part = chosen.Part!;
            if (voiced) {
                double step = Math.Clamp(tgt / chosen.F0, 0.75, 1.33);
                if (Math.Abs(step - 1.0) > 0.03) part = Resample(part, step);
            }

            int len = Math.Min(part.Length, maxLen);
            bool truncated = len < part.Length;
            var res = new float[len];
            Array.Copy(part, keepEnd ? part.Length - len : 0, res, 0, len);

            int fadeIn = Math.Min((keepEnd && truncated) ? 350 : 256, len / 2);
            int fadeOut = Math.Min((!keepEnd && truncated) ? 350 : 128, len / 2);
            for (int i = 0; i < fadeIn; i++) res[i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fadeIn));
            for (int i = 0; i < fadeOut; i++) res[len - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fadeOut));
            return res;
        }
    }
}
