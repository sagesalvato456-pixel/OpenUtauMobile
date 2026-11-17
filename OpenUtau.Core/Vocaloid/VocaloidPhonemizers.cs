// OpenUtau.Core/Vocaloid/VocaloidPhonemizers.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Base + fonemizadores por idioma para los bancos VOCALOID.
//   JA japonés (kana y romaji) · EN inglés (diccionario + reglas) · ES español
//   ZH mandarín (pinyin) · KO coreano (hangul) · VI vietnamita · FR francés
//   DE alemán · IT italiano · RU ruso · PT portugués · TH tailandés (RTGS) · RAW directo
//
// Correcciones respecto a la versión anterior:
//  - Phoneme.position es relativo al inicio de la nota; antes se devolvía note.position
//    (posición absoluta del proyecto), lo que sacaba el fonema fuera de la nota.
//  - La consonante inicial se coloca ANTES del pulso (la vocal cae en el tiempo de la nota).
//  - Todos los símbolos que se emiten existen en VocaloidPhonemeSet.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    // ═══════════════════════════════════════════════════════════════════════
    //  BASE
    // ═══════════════════════════════════════════════════════════════════════

    public abstract class VocaloidPhonemizerBase : Phonemizer {

        protected USinger? singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
                Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

            Note head = notes[0];
            Note last = notes[notes.Length - 1];
            bool hinted = !string.IsNullOrWhiteSpace(head.phoneticHint);
            string lyric = ((hinted ? head.phoneticHint : head.lyric) ?? string.Empty).Trim();

            List<string> symbols = hinted
                ? new List<string>(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                : MapLyric(lyric);
            if (symbols.Count == 0) symbols.Add("-");

            int totalTicks = Math.Max(1, last.position + last.duration - head.position);
            double totalMs = TickSpanToMs(head.position, totalTicks);
            double maxOnsetMs = prevNeighbour.HasValue
                ? 0.5 * TickSpanToMs(prevNeighbour.Value.position, prevNeighbour.Value.duration)
                : 250.0;

            double[] startMs = LayoutMs(symbols, totalMs, maxOnsetMs);
            var phonemes = new Phoneme[symbols.Count];
            int lastTick = int.MinValue;
            for (int i = 0; i < phonemes.Length; i++) {
                int tick = MsToTicks(head.position, startMs[i]);
                if (i > 0 && tick <= lastTick) tick = lastTick + 1;
                lastTick = tick;
                phonemes[i] = new Phoneme { phoneme = symbols[i], position = tick };
            }
            return new Result { phonemes = phonemes };
        }

        /// <summary>Convierte una letra (o una palabra) en símbolos de fonema.</summary>
        protected abstract List<string> MapLyric(string lyric);

        // ── tiempos ─────────────────────────────────────────────────────────

        /// <summary>Milisegundos que dura un tramo de ticks que empieza en fromTick.</summary>
        protected double TickSpanToMs(int fromTick, int ticks) {
            if (timeAxis == null) return ticks * 60000.0 / (120.0 * 480.0);
            return timeAxis.TickPosToMsPos(fromTick + ticks) - timeAxis.TickPosToMsPos(fromTick);
        }

        /// <summary>Ticks relativos a fromTick que corresponden a ms (puede ser negativo).</summary>
        protected int MsToTicks(int fromTick, double ms) {
            if (ms < 0) return -MsToTicks(fromTick, -ms);
            if (timeAxis == null) return (int)Math.Round(ms * 120.0 * 480.0 / 60000.0);
            double startMs = timeAxis.TickPosToMsPos(fromTick);
            return timeAxis.MsPosToTickPos(startMs + ms) - fromTick;
        }

        /// <summary>
        /// Reparte los fonemas: la consonante inicial va antes de la nota, el núcleo (primera
        /// vocal) cae en el inicio, las consonantes finales se quedan con su duración natural
        /// y las demás vocales se reparten el resto.
        /// </summary>
        private double[] LayoutMs(List<string> sym, double totalMs, double maxOnsetMs) {
            int n = sym.Count;
            var cls = new PhonemeClass[n];
            var fixedMs = new double[n];
            double stretch = Math.Clamp(GetParentConsonantStretchRatio(), 0.4, 2.5);
            for (int i = 0; i < n; i++) {
                if (VocaloidPhonemeSet.TryGet(sym[i], out PhonemeSpec spec)) {
                    cls[i] = spec.Class;
                    fixedMs[i] = spec.NaturalMs * stretch;
                } else {
                    cls[i] = PhonemeClass.Plosive;
                    fixedMs[i] = 50.0 * stretch;
                }
            }

            int nucleus = -1;
            for (int i = 0; i < n; i++) {
                if (cls[i] == PhonemeClass.Vowel) { nucleus = i; break; }
            }
            if (nucleus < 0) nucleus = n - 1;

            double onset = 0;
            for (int i = 0; i < nucleus; i++) onset += fixedMs[i];
            if (onset > maxOnsetMs && onset > 0) {
                double k = maxOnsetMs / onset;
                for (int i = 0; i < nucleus; i++) fixedMs[i] *= k;
            }

            var pos = new double[n];
            pos[nucleus] = 0;
            for (int i = nucleus - 1; i >= 0; i--) pos[i] = pos[i + 1] - fixedMs[i];

            double tailFixed = 0;
            int slots = 1;
            for (int j = nucleus + 1; j < n; j++) {
                if (cls[j] == PhonemeClass.Vowel) slots++;
                else tailFixed += fixedMs[j];
            }
            if (tailFixed > 0.6 * totalMs && tailFixed > 0) {
                double k = 0.6 * totalMs / tailFixed;
                for (int j = nucleus + 1; j < n; j++) {
                    if (cls[j] != PhonemeClass.Vowel) fixedMs[j] *= k;
                }
                tailFixed *= k;
            }
            double share = Math.Max(0.0, totalMs - tailFixed) / slots;
            double cursor = share;
            for (int j = nucleus + 1; j < n; j++) {
                pos[j] = cursor;
                cursor += cls[j] == PhonemeClass.Vowel ? share : fixedMs[j];
            }
            return pos;
        }

        // ── utilidades para las tablas de idiomas ───────────────────────────

        /// <summary>Lee "clave=f1 f2;clave2=f3" en un diccionario grafema a fonemas.</summary>
        protected static Dictionary<string, string[]> BuildMap(string def) {
            var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string entry in def.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                string e = entry.Trim();
                if (e.Length == 0) continue;
                int eq = e.IndexOf('=');
                if (eq <= 0) continue;
                string key = e.Substring(0, eq).Trim();
                string val = e.Substring(eq + 1).Trim();
                map[key] = val.Length == 0
                    ? Array.Empty<string>()
                    : val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
            return map;
        }

        /// <summary>Recorre la palabra tomando siempre el grafema más largo que exista en el mapa.</summary>
        protected static List<string> GreedyMap(string word, Dictionary<string, string[]> map, int maxLen) {
            var result = new List<string>();
            string w = word.ToLowerInvariant();
            int i = 0;
            while (i < w.Length) {
                bool hit = false;
                for (int len = Math.Min(maxLen, w.Length - i); len >= 1 && !hit; len--) {
                    if (map.TryGetValue(w.Substring(i, len), out string[]? ph)) {
                        result.AddRange(ph);
                        i += len;
                        hit = true;
                    }
                }
                if (hit) continue;
                char c = w[i];
                if (char.IsLetter(c)) result.Add(c.ToString());
                i++;
            }
            return result;
        }

        /// <summary>Aplica fn a cada palabra de la letra y concatena.</summary>
        protected static List<string> EachWord(string lyric, Func<string, List<string>> fn) {
            var result = new List<string>();
            foreach (string word in lyric.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) {
                result.AddRange(fn(word));
            }
            return result;
        }

        protected static bool IsRestLyric(string t) {
            return t == "-" || t == "R" || t == "sp" || t == "SP";
        }

        protected static bool IsBreathLyric(string t) {
            return t == "br" || t == "ap" || t == "息";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  JAPONÉS
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Japanese", "JA V4", "OpenLoid-Flame", language: "JA")]
    public class VocaloidJapanesePhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<char, string> kanaRomaji = new Dictionary<char, string>();
        private static readonly Dictionary<char, string> smallKana = new Dictionary<char, string>();
        private static readonly Dictionary<string, string[]> moras = new Dictionary<string, string[]>(StringComparer.Ordinal);

        static VocaloidJapanesePhonemizer() {
            string[] kana = {
                "あいうえお", "かきくけこ", "さしすせそ", "たちつてと", "なにぬねの", "はひふへほ",
                "まみむめも", "らりるれろ", "がぎぐげご", "ざじずぜぞ", "だぢづでど", "ばびぶべぼ", "ぱぴぷぺぽ",
            };
            string[] roma = {
                "a i u e o", "ka ki ku ke ko", "sa shi su se so", "ta chi tsu te to", "na ni nu ne no", "ha hi fu he ho",
                "ma mi mu me mo", "ra ri ru re ro", "ga gi gu ge go", "za ji zu ze zo", "da ji zu de do", "ba bi bu be bo", "pa pi pu pe po",
            };
            for (int r = 0; r < kana.Length; r++) {
                string[] cols = roma[r].Split(' ');
                for (int c = 0; c < cols.Length; c++) kanaRomaji[kana[r][c]] = cols[c];
            }
            kanaRomaji['や'] = "ya"; kanaRomaji['ゆ'] = "yu"; kanaRomaji['よ'] = "yo";
            kanaRomaji['わ'] = "wa"; kanaRomaji['を'] = "o"; kanaRomaji['ゐ'] = "i"; kanaRomaji['ゑ'] = "e";
            kanaRomaji['ゔ'] = "vu";
            smallKana['ゃ'] = "ya"; smallKana['ゅ'] = "yu"; smallKana['ょ'] = "yo";
            smallKana['ぁ'] = "a"; smallKana['ぃ'] = "i"; smallKana['ぅ'] = "u"; smallKana['ぇ'] = "e"; smallKana['ぉ'] = "o";

            string[] prefixes = {
                "", "k", "g", "s", "z", "t", "d", "n", "h", "b", "p", "m", "r", "w", "y", "v", "f", "j",
                "sh", "ch", "ts", "ky", "gy", "ny", "hy", "by", "py", "my", "ry",
            };
            foreach (string p in prefixes) {
                foreach (char v in "aiueo") moras[p + v] = BuildMora(p, v);
            }
        }

        private static string[] BuildMora(string prefix, char v) {
            var list = new List<string>();
            string vow = v == 'u' ? "M" : v.ToString();
            switch (prefix) {
                case "": break;
                case "s": list.Add(v == 'i' ? "S" : "s"); break;
                case "z": list.Add(v == 'i' ? "dZ" : "dz"); break;
                case "j": list.Add("dZ"); break;
                case "k": list.Add(v == 'i' ? "k'" : "k"); break;
                case "g": list.Add(v == 'i' ? "g'" : "g"); break;
                case "b": list.Add(v == 'i' ? "b'" : "b"); break;
                case "p": list.Add(v == 'i' ? "p'" : "p"); break;
                case "m": list.Add(v == 'i' ? "m'" : "m"); break;
                case "t": list.Add(v == 'i' ? "tS" : v == 'u' ? "ts" : "t"); break;
                case "n": list.Add(v == 'i' ? "J" : "n"); break;
                case "h": list.Add(v == 'i' ? "C" : v == 'u' ? "F" : "h"); break;
                case "f": list.Add("F"); break;
                case "r": list.Add(v == 'i' ? "4'" : "4"); break;
                case "y": list.Add("j"); break;
                case "sh": list.Add("S"); break;
                case "ch": list.Add("tS"); break;
                // consonantes palatalizadas nativas del banco JA (k' g' p' b' m' 4')
                case "ky": list.Add("k'"); break;
                case "gy": list.Add("g'"); break;
                case "ny": list.Add("J"); break;
                case "hy": list.Add("C"); break;
                case "by": list.Add("b'"); break;
                case "py": list.Add("p'"); break;
                case "my": list.Add("m'"); break;
                case "ry": list.Add("4'"); break;
                default: list.Add(prefix); break; // k g b p m d w v ts
            }
            list.Add(vow);
            return list.ToArray();
        }

        private static string ToHiragana(string s) {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s) {
                if (ch >= '\u30A1' && ch <= '\u30F6') sb.Append((char)(ch - 0x60));
                else sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>Une un kana (romaji) con una kana pequeña: き+ゃ = kya, ふ+ぁ = fa, う+ぃ = wi.</summary>
        private static string Combine(string r, string small) {
            string cons = r.Substring(0, r.Length - 1);
            if (small[0] == 'y') {
                if (cons == "sh" || cons == "ch" || cons == "j") return cons + small.Substring(1);
                return cons.Length == 0 ? small : cons + small;
            }
            if (cons.Length == 0 && r == "u") return "w" + small;
            return cons + small;
        }

        private static void AddProlong(List<string> result) {
            for (int i = result.Count - 1; i >= 0; i--) {
                if (VocaloidPhonemeSet.TryGet(result[i], out PhonemeSpec spec) && spec.Class == PhonemeClass.Vowel) {
                    result.Add(result[i]);
                    return;
                }
            }
        }

        private static void ParseRomaji(string run, List<string> result) {
            int i = 0;
            while (i < run.Length) {
                bool found = false;
                for (int len = 3; len >= 1 && !found; len--) {
                    if (i + len > run.Length) continue;
                    if (moras.TryGetValue(run.Substring(i, len), out string[]? ph)) {
                        result.AddRange(ph);
                        i += len;
                        found = true;
                    }
                }
                if (found) continue;
                char c = run[i];
                if (c == 'n') { result.Add("N"); i++; continue; }
                if (i + 1 < run.Length && run[i] == run[i + 1]) { result.Add("cl"); i++; continue; }
                result.Add(c.ToString());
                i++;
            }
        }

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            string t = lyric.Trim();
            if (IsRestLyric(t)) { result.Add("-"); return result; }
            if (IsBreathLyric(t)) { result.Add("br"); return result; }

            string s = ToHiragana(t);
            int i = 0;
            while (i < s.Length) {
                char c = s[i];
                if (c == ' ' || c == '\t') { i++; continue; }
                if (c == 'ん') { result.Add("N"); i++; continue; }
                if (c == 'っ') { result.Add("cl"); i++; continue; }
                if (c == 'ー') { AddProlong(result); i++; continue; }
                if (kanaRomaji.TryGetValue(c, out string? r)) {
                    i++;
                    if (i < s.Length && smallKana.TryGetValue(s[i], out string? sm)) {
                        r = Combine(r, sm);
                        i++;
                    }
                    ParseRomaji(r, result);
                    continue;
                }
                if (c >= 'a' && c <= 'z') {
                    int j = i;
                    while (j < s.Length && s[j] >= 'a' && s[j] <= 'z') j++;
                    ParseRomaji(s.Substring(i, j - i), result);
                    i = j;
                    continue;
                }
                result.Add(c.ToString());
                i++;
            }
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  INGLÉS
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 English", "EN V4", "OpenLoid-Flame", language: "EN")]
    public class VocaloidEnglishPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> dict = BuildMap(@"
a=@;the=dh @;i=ay;you=j uw;love=l ah v;me=m iy;and=@ n d;to=t uw;it=ih t;is=ih z;in=ih n;my=m ay;
your=j ao r;we=w iy;be=b iy;see=s iy;sky=s k ay;fly=f l ay;day=d ey;way=w ey;night=n ay t;light=l ay t;
fire=f ay er;sun=s ah n;moon=m uw n;star=s t aa r;dream=d r iy m;sing=s ih ng;song=s ao ng;voice=v oy s;
world=w er l d;time=t ay m;never=n eh v er;forever=f er eh v er;hello=hh @ l ow;baby=b ey b iy;oh=ow;
yeah=j eh;la=l aa;na=n aa;heart=hh aa r t;of=ah v;for=f ao r;with=w ih th;one=w ah n;two=t uw;
are=aa r;what=w ah t;when=w eh n;do=d uw;go=g ow;so=s ow;no=n ow;yes=j eh s;he=hh iy;she=sh iy;
they=dh ey;there=dh eh r;here=hh ih r;eyes=ay z;hand=hh ae n d;blue=b l uw;green=g r iy n;
red=r eh d;miku=m iy k uw;vocaloid=v ow k @ l oy d");

        private static readonly (string g, string p)[] rules = {
            ("eigh", "ey"), ("tch", "ch"), ("igh", "ay"), ("dge", "jh"), ("sch", "s k"), ("ough", "ow"),
            ("ph", "f"), ("sh", "sh"), ("ch", "ch"), ("th", "th"), ("ng", "ng"), ("nk", "ng k"), ("ck", "k"),
            ("qu", "k w"), ("wh", "w"), ("gh", ""), ("ee", "iy"), ("ea", "iy"), ("oo", "uw"), ("ou", "aw"),
            ("oi", "oy"), ("oy", "oy"), ("ai", "ey"), ("ay", "ey"), ("au", "ao"), ("aw", "ao"), ("ie", "iy"),
            ("ei", "ey"), ("oa", "ow"), ("ue", "uw"), ("ui", "uw"), ("ew", "uw"), ("er", "er"), ("ir", "er"),
            ("ur", "er"), ("ar", "aa r"), ("or", "ao r"), ("ow", "ow"),
        };

        private static bool IsVowel(char c) { return "aeiou".IndexOf(c) >= 0; }

        private static List<string> G2P(string w) {
            var r = new List<string>();
            int n = w.Length;
            int i = 0;
            while (i < n) {
                bool hit = false;
                if (i == 0 && w.StartsWith("kn", StringComparison.Ordinal)) { r.Add("n"); i += 2; continue; }
                if (i == 0 && w.StartsWith("wr", StringComparison.Ordinal)) { r.Add("r"); i += 2; continue; }
                foreach (var rule in rules) {
                    if (i + rule.g.Length <= n && string.CompareOrdinal(w, i, rule.g, 0, rule.g.Length) == 0) {
                        if (rule.p.Length > 0) r.AddRange(rule.p.Split(' '));
                        i += rule.g.Length;
                        hit = true;
                        break;
                    }
                }
                if (hit) continue;

                char c = w[i];
                char next = i + 1 < n ? w[i + 1] : '\0';
                bool magic = i + 2 == n - 1 && w[n - 1] == 'e' && !IsVowel(next) && next != '\0';
                switch (c) {
                    case 'a': r.Add(magic ? "ey" : "ae"); break;
                    case 'e':
                        // la e final detrás de consonante es muda (love, time, fire)
                        if (!(i == n - 1 && n > 2 && !IsVowel(w[i - 1]))) r.Add(magic ? "iy" : "eh");
                        break;
                    case 'i': r.Add(magic ? "ay" : "ih"); break;
                    case 'o': r.Add(magic || i == n - 1 ? "ow" : "aa"); break;
                    case 'u': r.Add(magic ? "uw" : "ah"); break;
                    case 'y':
                        if (i == 0) r.Add("j");
                        else if (i == n - 1) r.Add("iy");
                        else r.Add("ih");
                        break;
                    case 'c': r.Add(next == 'e' || next == 'i' || next == 'y' ? "s" : "k"); break;
                    case 'g': r.Add(next == 'e' || next == 'i' || next == 'y' ? "jh" : "g"); break;
                    case 'h': r.Add("hh"); break;
                    case 'j': r.Add("jh"); break;
                    case 'q': r.Add("k"); break;
                    case 'x': r.Add("k"); r.Add("s"); break;
                    case 'l':
                        // ...ble, ...tle: vocal neutra antes de la l
                        if (i == n - 2 && next == 'e' && n > 3 && !IsVowel(w[i - 1])) r.Add("@");
                        r.Add("l");
                        break;
                    default:
                        if (c >= 'a' && c <= 'z') {
                            if (i > 0 && w[i - 1] == c) break; // consonante doble
                            r.Add(c.ToString());
                        }
                        break;
                }
                i++;
            }
            return r;
        }

        private static List<string> Word(string word) {
            string w = word.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':', '"');
            if (dict.TryGetValue(w, out string[]? ph)) return new List<string>(ph);
            return G2P(w);
        }

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, Word);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ESPAÑOL
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Spanish", "ES V4", "OpenLoid-Flame", language: "ES")]
    public class VocaloidSpanishPhonemizer : VocaloidPhonemizerBase {

        private static bool IsVowelChar(char c) { return "aeiouáéíóúü".IndexOf(c) >= 0; }

        private static string Plain(char c) {
            switch (c) {
                case 'á': return "a";
                case 'é': return "e";
                case 'í': return "i";
                case 'ó': return "o";
                case 'ú': return "u";
                case 'ü': return "u";
                default: return c.ToString();
            }
        }

        private static List<string> Word(string word) {
            var r = new List<string>();
            string w = word.ToLowerInvariant();
            int n = w.Length;
            for (int i = 0; i < n; i++) {
                char c = w[i];
                char next = i + 1 < n ? w[i + 1] : '\0';
                char prev = i > 0 ? w[i - 1] : '\0';
                bool front = next == 'e' || next == 'i' || next == 'é' || next == 'í';
                switch (c) {
                    case 'a': case 'e': case 'o': case 'á': case 'é': case 'ó':
                        r.Add(Plain(c));
                        break;
                    case 'i': case 'í': case 'u': case 'ú': case 'ü': {
                        // la u es muda en que, qui, gue, gui
                        if (c == 'u' && (prev == 'q' || (prev == 'g' && (next == 'e' || next == 'i' || next == 'é' || next == 'í')))) {
                            break;
                        }
                        bool strong = c == 'í' || c == 'ú';
                        bool prevVowel = IsVowelChar(prev);
                        bool nextVowel = IsVowelChar(next);
                        if (!strong && (c == 'i' || c == 'u') && (prevVowel != nextVowel)) {
                            r.Add(c == 'i' ? "j" : "w"); // hielo, agua, hay, auto
                        } else {
                            r.Add(Plain(c));
                        }
                        break;
                    }
                    case 'b': case 'v': r.Add("b"); break;
                    case 'c':
                        if (next == 'h') { r.Add("tS"); i++; }
                        else r.Add(front ? "s" : "k");
                        break;
                    case 'd': r.Add("d"); break;
                    case 'f': r.Add("f"); break;
                    case 'g': r.Add(front ? "x" : "g"); break;
                    case 'h': break;
                    case 'j': r.Add("x"); break;
                    case 'k': r.Add("k"); break;
                    case 'l':
                        if (next == 'l') { r.Add("j"); i++; } else r.Add("l");
                        break;
                    case 'm': r.Add("m"); break;
                    case 'n': r.Add("n"); break;
                    case 'ñ': r.Add("nj"); break;
                    case 'p': r.Add("p"); break;
                    case 'q': r.Add("k"); break;
                    case 'r':
                        if (next == 'r') { r.Add("rr"); i++; }
                        else if (i == 0 || prev == 'n' || prev == 'l' || prev == 's') r.Add("rr");
                        else r.Add("4");
                        break;
                    case 's': r.Add("s"); break;
                    case 't': r.Add("t"); break;
                    case 'w': r.Add("w"); break;
                    case 'x': r.Add("k"); r.Add("s"); break;
                    case 'y': r.Add(IsVowelChar(next) ? "j" : "i"); break;
                    case 'z': r.Add("s"); break;
                    default:
                        if (char.IsLetter(c)) r.Add(c.ToString());
                        break;
                }
            }
            return r;
        }

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, Word);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MANDARÍN
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Chinese Mandarin", "ZH V4", "OpenLoid-Flame", language: "ZH")]
    public class VocaloidMandarinPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> initials = BuildMap(
            "zh=tS;ch=tS;sh=S;b=p;p=ph;m=m;f=f;d=t;t=th;n=n;l=l;g=k;k=kh;h=x;j=tC;q=tCh;x=C;r=Z;z=ts;c=ts;s=s");

        private static readonly Dictionary<string, string[]> finals = BuildMap(@"
a=a;o=o;e=@;i=i;u=u;v=y;ai=aI;ei=eI;ui=w eI;ao=aU;ou=oU;iu=j oU;ie=j e;ve=y e;ue=y e;er=@r;
an=a n;en=@ n;in=i n;un=w @ n;vn=y n;ang=a ng;eng=@ ng;ing=i ng;ong=o ng;
ia=j a;ua=w a;uo=w o;iao=j aU;uai=w aI;ian=j E n;uan=w a n;van=y E n;iang=j a ng;uang=w a ng;iong=j o ng");

        private static readonly string[] initialOrder = {
            "zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l", "g", "k", "h", "j", "q", "x", "r", "z", "c", "s",
        };

        private static List<string> Syllable(string raw) {
            var r = new List<string>();
            string py = raw.ToLowerInvariant().TrimEnd('1', '2', '3', '4', '5').Replace("ü", "v");
            if (py.Length == 0) return r;

            string ini = string.Empty;
            string fin = py;
            if (py[0] == 'y') {
                string rest = py.Substring(1);
                if (rest.StartsWith("u", StringComparison.Ordinal)) fin = "v" + rest.Substring(1);
                else if (rest.StartsWith("i", StringComparison.Ordinal)) fin = rest;
                else { ini = "y"; fin = rest; }
                if (rest.Length == 0) fin = "i";
            } else if (py[0] == 'w') {
                string rest = py.Substring(1);
                if (rest.StartsWith("u", StringComparison.Ordinal) || rest.Length == 0) fin = rest.Length == 0 ? "u" : rest;
                else { ini = "w"; fin = rest; }
            } else {
                foreach (string cand in initialOrder) {
                    if (py.StartsWith(cand, StringComparison.Ordinal)) { ini = cand; fin = py.Substring(cand.Length); break; }
                }
            }

            if ((ini == "j" || ini == "q" || ini == "x") && fin.StartsWith("u", StringComparison.Ordinal)) {
                fin = "v" + fin.Substring(1);
            }
            if (ini == "y") r.Add("j");
            else if (ini == "w") r.Add("w");
            else if (ini.Length > 0 && initials.TryGetValue(ini, out string[]? ip)) r.AddRange(ip);

            bool apical = ini == "zh" || ini == "ch" || ini == "sh" || ini == "r" || ini == "z" || ini == "c" || ini == "s";
            if (fin == "i" && apical) r.Add("i0");
            else if (finals.TryGetValue(fin, out string[]? fp)) r.AddRange(fp);
            else if (fin.Length > 0) r.Add(fin);
            return r;
        }

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, Syllable);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  COREANO
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Korean", "KO V4", "OpenLoid-Flame", language: "KO")]
    public class VocaloidKoreanPhonemizer : VocaloidPhonemizerBase {

        private static readonly string[] initCons = {
            "g", "kk", "n", "t", "tt", "4", "m", "b", "pp", "s", "ss", "", "tC", "jj", "tCh", "kh", "th", "ph", "h",
        };
        // d inicial suena t/d: usamos d para ㄷ
        private static readonly string[] vowels = {
            "a", "E", "j a", "j E", "V", "e", "j V", "j e", "o", "w a", "w E", "w e", "j o", "u", "w V", "w e", "w i", "j u", "M", "M i", "i",
        };
        private static readonly string[] finCons = {
            "", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t",
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            string t = lyric.Trim();
            if (IsRestLyric(t)) { result.Add("-"); return result; }
            if (IsBreathLyric(t)) { result.Add("br"); return result; }
            foreach (char ch in t) {
                if (ch >= 0xAC00 && ch <= 0xD7A3) {
                    int code = ch - 0xAC00;
                    int iIdx = code / (21 * 28);
                    int vIdx = (code % (21 * 28)) / 28;
                    int fIdx = code % 28;
                    string ini = initCons[iIdx];
                    if (ini == "t") ini = "d";
                    if (ini.Length > 0) result.Add(ini);
                    result.AddRange(vowels[vIdx].Split(' '));
                    if (finCons[fIdx].Length > 0) result.Add(finCons[fIdx]);
                } else if (!char.IsWhiteSpace(ch)) {
                    result.Add(ch.ToString());
                }
            }
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VIETNAMITA
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Vietnamese", "VI V4", "OpenLoid-Flame", language: "VI")]
    public class VocaloidVietnamesePhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;ă=a;â=@;e=e;ê=e;i=i;y=i;o=o;ô=o;ơ=@;u=u;ư=M;
b=b;c=k;k=k;q=k;d=z;đ=d;g=g;gh=g;h=h;kh=x;l=l;m=m;n=n;ng=ng;ngh=ng;nh=nj;ph=f;r=z;s=s;t=t;th=th;tr=tS;v=v;x=s;ch=tC;gi=z;qu=k w");

        private static string StripTones(string s) {
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d) {
                if (c == '\u0300' || c == '\u0301' || c == '\u0303' || c == '\u0309' || c == '\u0323') continue;
                sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(StripTones(t), w => GreedyMap(w, map, 3));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FRANCÉS
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 French", "FR V4", "OpenLoid-Flame", language: "FR")]
    public class VocaloidFrenchPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;â=a;à=a;e=@;é=e;è=E;ê=E;ë=E;i=i;î=i;ï=i;o=o;ô=o;u=y;û=y;ù=y;ou=u;eu=2;œu=2;œ=2;au=o;eau=o;ai=E;ei=E;oi=w a;
an=a n;en=a n;am=a n;em=a n;in=5;im=5;ain=5;ein=5;un=5;um=5;on=o n;om=o n;
b=b;c=k;ç=s;d=d;f=f;g=g;j=Z;k=k;l=l;m=m;n=n;p=p;q=k;qu=k;r=R;s=s;t=t;v=v;w=w;x=k s;y=j;z=z;
ch=S;gn=nj;ph=f;th=t;h=;ce=s @;ci=s i;ge=Z @;gi=Z i");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 3));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ALEMÁN
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 German", "DE V4", "OpenLoid-Flame", language: "DE")]
    public class VocaloidGermanPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;aa=a;ä=E;e=@;ee=e;i=I;ie=i;o=O;oo=o;ö=2;u=U;uu=u;ü=Y;au=aU;eu=oy;äu=oy;ei=aI;ai=aI;
b=b;ch=x;ck=k;d=d;f=f;g=g;h=h;j=j;k=k;l=l;m=m;n=n;ng=ng;p=p;pf=p f;qu=k v;r=R;s=z;sch=S;ss=s;ß=s;t=t;tsch=tS;tz=ts;v=f;w=v;x=k s;z=ts");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 4));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ITALIANO
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Italian", "IT V4", "OpenLoid-Flame", language: "IT")]
    public class VocaloidItalianPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;e=e;è=E;é=e;i=i;o=o;ò=O;ó=o;u=u;
b=b;c=k;d=d;f=f;g=g;h=;l=l;m=m;n=n;p=p;q=k;r=4;s=s;t=t;v=v;z=ts;
ch=k;gh=g;gn=nj;gli=L i;ce=tS e;ci=tS i;cia=tS a;cio=tS o;ciu=tS u;ge=dZ e;gi=dZ i;gia=dZ a;gio=dZ o;giu=dZ u;
sce=S e;sci=S i;qu=k w;rr=rr;zz=ts");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 3));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  RUSO
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Russian", "RU V4", "OpenLoid-Flame", language: "RU")]
    public class VocaloidRussianPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
а=a;е=j e;ё=j o;и=i;й=j;о=o;у=u;ы=I;э=E;ю=j u;я=j a;
б=b;в=v;г=g;д=d;ж=Z;з=z;к=k;л=l;м=m;н=n;п=p;р=4;с=s;т=t;ф=f;х=x;ц=ts;ч=tS;ш=S;щ=S;ъ=;ь=");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 1));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PORTUGUÉS
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Portuguese", "PT V4", "OpenLoid-Flame", language: "PT")]
    public class VocaloidPortuguesePhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;á=a;â=@;ã=a n;à=a;ão=a w;e=e;ê=e;é=E;i=i;í=i;o=o;ó=O;ô=o;õ=o n;u=u;ú=u;
b=b;c=k;ç=s;d=d;f=f;g=g;h=;j=Z;k=k;l=l;lh=L;m=m;n=n;nh=nj;p=p;q=k;qu=k;r=4;rr=R;s=s;ss=s;t=t;v=v;w=v;x=S;z=z;ch=S;ce=s e;ci=s i;ge=Z e;gi=Z i");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 3));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TAILANDÉS (RTGS)
    // ═══════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Thai", "TH V4", "OpenLoid-Flame", language: "TH")]
    public class VocaloidThaiPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> map = BuildMap(@"
a=a;ae={;i=i;ue=M;u=u;e=e;o=o;oe=@;ia=i a;uea=M a;ua=u a;ai=aI;ao=aU;
p=p;ph=ph;b=b;t=t;th=th;d=d;k=k;kh=kh;g=g;m=m;n=n;ng=ng;f=f;s=s;h=h;ch=tCh;j=tC;r=r;l=l;w=w;y=j");

        protected override List<string> MapLyric(string lyric) {
            string t = lyric.Trim();
            if (IsRestLyric(t)) return new List<string> { "-" };
            if (IsBreathLyric(t)) return new List<string> { "br" };
            return EachWord(t, w => GreedyMap(w, map, 3));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DIRECTO
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Cada palabra de la letra se toma como símbolo de fonema ("k a" o "ka").</summary>
    [Phonemizer("Vocaloid4 Phoneme (direct)", "V4 RAW", "OpenLoid-Flame", language: "")]
    public class VocaloidRawPhonemizer : VocaloidPhonemizerBase {
        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(lyric)) { result.Add("-"); return result; }
            result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return result;
        }
    }
}
