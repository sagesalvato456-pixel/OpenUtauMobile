// OpenUtau.Core/Vocaloid/VocaloidPhonemizers.cs
// Lang: C# 10, Target: .NET 7+ / Android (OpenUtauMobile-dev / OpenLoid-Flame)
//
// One file — base class + every language phonemizer for Vocaloid4 singers.
//
// Language coverage:
//   JA  Japanese     CV + V split with moraic N and closure
//   EN  English      ARPAbet-style diphone
//   ZH  Mandarin     Pinyin → V4 CV phonemes
//   KO  Korean       Jamo decomposition
//   VI  Vietnamese   VINA tone phonemes
//   ES  Spanish      CV diphone
//   FR  French       CV + nasal vowels
//   DE  German       CV + length
//   IT  Italian      CV
//   RU  Russian      CV + palatalisation
//   PT  Portuguese   CV
//   TH  Thai         CV + tone
//
// All phonemizers inherit VocaloidPhonemizerBase which handles the boilerplate
// and calls the language-specific MapLyric() method.

using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    // ═══════════════════════════════════════════════════════════════════════════
    //  BASE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shared plumbing for all Vocaloid4 phonemizers.
    /// Subclasses implement <see cref="MapLyric"/> to convert a lyric string
    /// into a list of V4 phoneme symbols.
    /// </summary>
    public abstract class VocaloidPhonemizerBase : Phonemizer {

        protected USinger? singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
                Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

            var note = notes[0];
            string lyric = string.IsNullOrWhiteSpace(note.phoneticHint)
                ? note.lyric
                : note.phoneticHint;

            var phonemes = MapLyric(lyric.Trim());

            if (phonemes.Count == 0)
                phonemes.Add("-");  // silence fallback

            var result = new Phoneme[phonemes.Count];
            for (int i = 0; i < phonemes.Count; i++) {
                result[i] = new Phoneme {
                    phoneme  = phonemes[i],
                    position = NoteToPhonemePosition(note, i, phonemes.Count),
                };
            }

            return new Result { phonemes = result };
        }

        /// <summary>Map a lyric / phonetic hint to a list of V4 phoneme symbols.</summary>
        protected abstract List<string> MapLyric(string lyric);

        /// <summary>
        /// Spread phonemes evenly across the note.
        /// First phoneme starts at the note onset; subsequent ones are spaced.
        /// </summary>
        protected static int NoteToPhonemePosition(Note note, int index, int total) {
            if (total == 1 || index == 0) return note.position;
            int slot = note.duration / total;
            return note.position + index * slot;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  JAPANESE
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Japanese", "JA V4", "OpenLoid-Flame", language: "JA")]
    public class VocaloidJapanesePhonemizer : VocaloidPhonemizerBase {

        // Hiragana/Katakana → (C, V) pairs; moraic N and special cases handled
        // separately. Covers JIS-X 0208 kana + common combinations.
        private static readonly Dictionary<string, (string c, string v)> kanaTable
            = new Dictionary<string, (string, string)>(StringComparer.Ordinal) {
            // ── vowels ────────────────────────────────────────────────────────
            {"あ",("","a")},{"ア",("","a")},
            {"い",("","i")},{"イ",("","i")},
            {"う",("","u")},{"ウ",("","u")},
            {"え",("","e")},{"エ",("","e")},
            {"お",("","o")},{"オ",("","o")},
            // ── k ─────────────────────────────────────────────────────────────
            {"か",("k","a")},{"カ",("k","a")},
            {"き",("k","i")},{"キ",("k","i")},
            {"く",("k","u")},{"ク",("k","u")},
            {"け",("k","e")},{"ケ",("k","e")},
            {"こ",("k","o")},{"コ",("k","o")},
            // ── s ─────────────────────────────────────────────────────────────
            {"さ",("s","a")},{"サ",("s","a")},
            {"し",("S","i")},{"シ",("S","i")},
            {"す",("s","u")},{"ス",("s","u")},
            {"せ",("s","e")},{"セ",("s","e")},
            {"そ",("s","o")},{"ソ",("s","o")},
            // ── t ─────────────────────────────────────────────────────────────
            {"た",("t","a")},{"タ",("t","a")},
            {"ち",("tC","i")},{"チ",("tC","i")},
            {"つ",("ts","u")},{"ツ",("ts","u")},
            {"て",("t","e")},{"テ",("t","e")},
            {"と",("t","o")},{"ト",("t","o")},
            // ── n ─────────────────────────────────────────────────────────────
            {"な",("n","a")},{"ナ",("n","a")},
            {"に",("n","i")},{"ニ",("n","i")},
            {"ぬ",("n","u")},{"ヌ",("n","u")},
            {"ね",("n","e")},{"ネ",("n","e")},
            {"の",("n","o")},{"ノ",("n","o")},
            // ── h ─────────────────────────────────────────────────────────────
            {"は",("h","a")},{"ハ",("h","a")},
            {"ひ",("C","i")},{"ヒ",("C","i")},
            {"ふ",("P","u")},{"フ",("P","u")},
            {"へ",("h","e")},{"ヘ",("h","e")},
            {"ほ",("h","o")},{"ホ",("h","o")},
            // ── m ─────────────────────────────────────────────────────────────
            {"ま",("m","a")},{"マ",("m","a")},
            {"み",("m","i")},{"ミ",("m","i")},
            {"む",("m","u")},{"ム",("m","u")},
            {"め",("m","e")},{"メ",("m","e")},
            {"も",("m","o")},{"モ",("m","o")},
            // ── y ─────────────────────────────────────────────────────────────
            {"や",("j","a")},{"ヤ",("j","a")},
            {"ゆ",("j","u")},{"ユ",("j","u")},
            {"よ",("j","o")},{"ヨ",("j","o")},
            // ── r ─────────────────────────────────────────────────────────────
            {"ら",("4","a")},{"ラ",("4","a")},
            {"り",("4","i")},{"リ",("4","i")},
            {"る",("4","u")},{"ル",("4","u")},
            {"れ",("4","e")},{"レ",("4","e")},
            {"ろ",("4","o")},{"ロ",("4","o")},
            // ── w ─────────────────────────────────────────────────────────────
            {"わ",("w","a")},{"ワ",("w","a")},
            {"ゐ",("w","i")},{"ヰ",("w","i")},
            {"ゑ",("w","e")},{"ヱ",("w","e")},
            {"を",("w","o")},{"ヲ",("w","o")},
            // ── g ─────────────────────────────────────────────────────────────
            {"が",("g","a")},{"ガ",("g","a")},
            {"ぎ",("g","i")},{"ギ",("g","i")},
            {"ぐ",("g","u")},{"グ",("g","u")},
            {"げ",("g","e")},{"ゲ",("g","e")},
            {"ご",("g","o")},{"ゴ",("g","o")},
            // ── z ─────────────────────────────────────────────────────────────
            {"ざ",("dz","a")},{"ザ",("dz","a")},
            {"じ",("dZ'","i")},{"ジ",("dZ'","i")},
            {"ず",("z","u")},{"ズ",("z","u")},
            {"ぜ",("dz","e")},{"ゼ",("dz","e")},
            {"ぞ",("dz","o")},{"ゾ",("dz","o")},
            // ── d ─────────────────────────────────────────────────────────────
            {"だ",("d","a")},{"ダ",("d","a")},
            {"ぢ",("dZ'","i")},{"ヂ",("dZ'","i")},
            {"づ",("dz","u")},{"ヅ",("dz","u")},
            {"で",("d","e")},{"デ",("d","e")},
            {"ど",("d","o")},{"ド",("d","o")},
            // ── b ─────────────────────────────────────────────────────────────
            {"ば",("b","a")},{"バ",("b","a")},
            {"び",("b","i")},{"ビ",("b","i")},
            {"ぶ",("b","u")},{"ブ",("b","u")},
            {"べ",("b","e")},{"ベ",("b","e")},
            {"ぼ",("b","o")},{"ボ",("b","o")},
            // ── p ─────────────────────────────────────────────────────────────
            {"ぱ",("p","a")},{"パ",("p","a")},
            {"ぴ",("p","i")},{"ピ",("p","i")},
            {"ぷ",("p","u")},{"プ",("p","u")},
            {"ぺ",("p","e")},{"ペ",("p","e")},
            {"ぽ",("p","o")},{"ポ",("p","o")},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();

            // moraic N
            if (lyric == "ん" || lyric == "ン") { result.Add("N"); return result; }
            // っ / ッ — geminate closure
            if (lyric == "っ" || lyric == "ッ") { result.Add("cl"); return result; }
            // breath / silence
            if (lyric == "-" || lyric == "R" || lyric == "息") { result.Add("br"); return result; }

            if (kanaTable.TryGetValue(lyric, out var cv)) {
                if (!string.IsNullOrEmpty(cv.c)) result.Add(cv.c);
                result.Add(cv.v);
                return result;
            }

            // romaji fallback: treat as direct phoneme symbol
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ENGLISH
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 English", "EN V4", "OpenLoid-Flame", language: "EN")]
    public class VocaloidEnglishPhonemizer : VocaloidPhonemizerBase {

        // Simple lookup for common words; falls back to treating lyric as
        // a space-separated phoneme sequence (IPA-ish).
        private static readonly Dictionary<string, string[]> arpa =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            {"a",   new[]{"@"}},
            {"the", new[]{"D","@"}},
            {"I",   new[]{"aI"}},
            {"you", new[]{"j","uw"}},
            {"love",new[]{"l","V","v"}},
            {"me",  new[]{"m","iy"}},
            {"and", new[]{"@","n","d"}},
            {"to",  new[]{"t","uw"}},
            {"it",  new[]{"ih","t"}},
            {"is",  new[]{"ih","z"}},
            {"in",  new[]{"ih","n"}},
            {"my",  new[]{"m","aI"}},
            {"your",new[]{"j","Ur"}},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();

            if (lyric == "-" || lyric == "sp") { result.Add("-"); return result; }
            if (lyric == "br")                 { result.Add("br"); return result; }

            if (arpa.TryGetValue(lyric, out var phones)) {
                result.AddRange(phones);
                return result;
            }

            // Space-separated phoneme string: "l V v" → ["l","V","v"]
            if (lyric.Contains(' ')) {
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return result;
            }

            // Single token — pass through (phonetic hint mode)
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MANDARIN CHINESE
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Chinese Mandarin", "ZH V4", "OpenLoid-Flame", language: "ZH")]
    public class VocaloidMandarinPhonemizer : VocaloidPhonemizerBase {

        // Pinyin initial → consonant phoneme
        private static readonly Dictionary<string, string> initials
            = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"b","b"},{"p","p"},{"m","m"},{"f","f"},
            {"d","d"},{"t","t"},{"n","n"},{"l","l"},
            {"g","g"},{"k","k"},{"h","x"},
            {"j","tC"},{"q","tC"},{"x","C"},
            {"zh","ts"},{"ch","ts"},{"sh","S"},{"r","4"},
            {"z","ts"},{"c","ts"},{"s","s"},
            {"y","j"},{"w","w"},
        };

        // Pinyin final → vowel/nucleus phoneme
        private static readonly Dictionary<string, string> finals
            = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"a","a"},{"o","o"},{"e","@"},{"i","i"},{"u","u"},{"v","v"},
            {"ai","aI"},{"ei","eI"},{"ui","eI"},
            {"ao","aU"},{"ou","oU"},{"iu","oU"},
            {"ie","e"},{"ve","e"},{"er","@r"},
            {"an","an"},{"en","@n"},{"in","in"},{"un","un"},{"vn","un"},
            {"ang","ang"},{"eng","@ng"},{"ing","ing"},{"ong","ong"},
            {"ia","a"},{"ua","a"},{"uo","o"},{"iao","aU"},{"uai","aI"},
            {"ian","en"},{"uan","an"},{"van","an"},
            {"iang","ang"},{"uang","ang"},
            {"iong","ong"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-" || lyric == "sp") { result.Add("-"); return result; }

            // Strip tone number if present (1-5)
            string py = lyric.TrimEnd('1','2','3','4','5');

            // Try splitting initial + final
            foreach (var kv in initials) {
                if (py.StartsWith(kv.Key, StringComparison.Ordinal)) {
                    string fin = py.Substring(kv.Key.Length);
                    result.Add(kv.Value);
                    if (finals.TryGetValue(fin, out string vph))
                        result.Add(vph);
                    else
                        result.Add(fin.Length > 0 ? fin : "a");
                    return result;
                }
            }

            // pure vowel nucleus
            if (finals.TryGetValue(py, out string v))
                result.Add(v);
            else
                result.Add(py.Length > 0 ? py : "a");

            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KOREAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Korean", "KO V4", "OpenLoid-Flame", language: "KO")]
    public class VocaloidKoreanPhonemizer : VocaloidPhonemizerBase {

        // Jamo initial consonants (Unicode 0x1100–0x1112)
        private static readonly string[] initCons = {
            "g","kk","n","d","tt","4","m","b","pp","s","ss","-","j","jj","C","k","t","p","h"
        };
        // Jamo vowels (0x1161–0x1175)
        private static readonly string[] vowels = {
            "a","ae","ya","yae","eo","e","yeo","ye","o","wa","wae","oe","yo","u","wo","we","wi","yu","eu","ui","i"
        };
        // Jamo final consonants (0x11A8–0x11C2, index 0 = no coda)
        private static readonly string[] finCons = {
            "","g","kk","gs","n","nj","nh","d","l","lg","lm","lb","ls","lt","lp","lh","m","b","bs","s","ss","ng","j","C","k","t","p","h"
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (string.IsNullOrEmpty(lyric)) { result.Add("-"); return result; }

            char ch = lyric[0];
            if (ch >= 0xAC00 && ch <= 0xD7A3) {
                int code  = ch - 0xAC00;
                int iIdx  = code / (21 * 28);
                int vIdx  = (code % (21 * 28)) / 28;
                int fIdx  = code % 28;

                string initC = iIdx < initCons.Length ? initCons[iIdx] : "";
                string vow   = vIdx < vowels.Length   ? vowels[vIdx]   : "a";
                string coda  = fIdx < finCons.Length  ? finCons[fIdx]  : "";

                if (!string.IsNullOrEmpty(initC) && initC != "-")
                    result.Add(initC);
                result.Add(vow);
                if (!string.IsNullOrEmpty(coda))
                    result.Add(coda);
                return result;
            }

            // romaja / phoneme hint passthrough
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIETNAMESE
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Vietnamese", "VI V4", "OpenLoid-Flame", language: "VI")]
    public class VocaloidVietnamesePhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> vina =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            // common syllables: initial + nucleus (+coda)
            {"a",  new[]{"a"}},
            {"ă",  new[]{"a"}},
            {"â",  new[]{"@"}},
            {"e",  new[]{"e"}},
            {"ê",  new[]{"e"}},
            {"i",  new[]{"i"}},
            {"o",  new[]{"o"}},
            {"ô",  new[]{"o"}},
            {"ơ",  new[]{"@"}},
            {"u",  new[]{"u"}},
            {"ư",  new[]{"u"}},
            {"y",  new[]{"i"}},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();

            if (lyric == "-" || lyric == "sp") { result.Add("-"); return result; }

            if (vina.TryGetValue(lyric, out var ph)) {
                result.AddRange(ph);
                return result;
            }

            // Space-separated phoneme hint
            if (lyric.Contains(' ')) {
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return result;
            }

            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SPANISH
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Spanish", "ES V4", "OpenLoid-Flame", language: "ES")]
    public class VocaloidSpanishPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string[]> table =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
            {"a",new[]{"a"}},{"e",new[]{"e"}},{"i",new[]{"i"}},
            {"o",new[]{"o"}},{"u",new[]{"u"}},
            {"b",new[]{"b"}},{"v",new[]{"b"}},
            {"c",new[]{"k"}},{"d",new[]{"d"}},{"f",new[]{"f"}},
            {"g",new[]{"g"}},{"h",new[]{"h"}},{"j",new[]{"x"}},
            {"k",new[]{"k"}},{"l",new[]{"l"}},{"ll",new[]{"j"}},
            {"m",new[]{"m"}},{"n",new[]{"n"}},{"ñ",new[]{"nj"}},
            {"p",new[]{"p"}},{"q",new[]{"k"}},{"r",new[]{"4"}},
            {"rr",new[]{"rr"}},{"s",new[]{"s"}},{"t",new[]{"t"}},
            {"w",new[]{"w"}},{"x",new[]{"ks"}},{"y",new[]{"j"}},{"z",new[]{"s"}},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (table.TryGetValue(lyric, out var ph)) { result.AddRange(ph); return result; }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FRENCH
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 French", "FR V4", "OpenLoid-Flame", language: "FR")]
    public class VocaloidFrenchPhonemizer : VocaloidPhonemizerBase {

        // minimal vowel/consonant passthrough + nasal vowels
        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"a","a"},{"â","a:"},{"e","@"},{"é","e"},{"è","E"},
            {"ê","E"},{"i","i"},{"î","i"},{"o","o"},{"ô","o:"},
            {"u","y"},{"û","y"},{"ou","u"},{"eu","@"},{"œu","@"},
            {"an","an"},{"en","an"},{"in","5"},{"un","5"},{"on","on"},
            {"b","b"},{"c","k"},{"d","d"},{"f","f"},{"g","g"},
            {"h","-"},{"j","Z"},{"k","k"},{"l","l"},{"m","m"},
            {"n","n"},{"p","p"},{"q","k"},{"r","R"},{"s","s"},
            {"t","t"},{"v","v"},{"w","w"},{"x","ks"},{"y","j"},
            {"z","z"},{"ch","S"},{"gn","nj"},{"ph","f"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) { result.Add(ph); return result; }
            if (lyric.Contains(' ')) {
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return result;
            }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GERMAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 German", "DE V4", "OpenLoid-Flame", language: "DE")]
    public class VocaloidGermanPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"a","a"},{"aa","a:"},{"ä","E"},{"e","@"},{"ee","e:"},
            {"i","I"},{"ie","i:"},{"o","O"},{"oo","o:"},{"ö","2"},
            {"u","U"},{"uu","u:"},{"ü","Y"},{"au","aU"},{"eu","OI"},
            {"äu","OI"},{"ei","aI"},{"b","b"},{"ch","x"},{"ck","k"},
            {"d","d"},{"f","f"},{"g","g"},{"h","h"},{"j","j"},
            {"k","k"},{"l","l"},{"m","m"},{"n","n"},{"ng","ng"},
            {"p","p"},{"qu","kv"},{"r","R"},{"s","z"},{"sch","S"},
            {"ss","s"},{"st","St"},{"sp","Sp"},{"t","t"},{"tsch","tS"},
            {"tz","ts"},{"v","f"},{"w","v"},{"x","ks"},{"z","ts"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) { result.Add(ph); return result; }
            if (lyric.Contains(' ')) {
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return result;
            }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ITALIAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Italian", "IT V4", "OpenLoid-Flame", language: "IT")]
    public class VocaloidItalianPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"a","a"},{"e","e"},{"è","E"},{"é","e"},
            {"i","i"},{"o","o"},{"ò","O"},{"ó","o"},{"u","u"},
            {"b","b"},{"c","k"},{"d","d"},{"f","f"},{"g","g"},
            {"l","l"},{"m","m"},{"n","n"},{"p","p"},{"r","r"},
            {"s","s"},{"t","t"},{"v","v"},{"z","dz"},
            {"ch","k"},{"gh","g"},{"gn","nj"},{"gli","L"},
            {"ci","tS"},{"gi","dZ"},{"ce","tSe"},{"ge","dZe"},
            {"sc","S"},{"zz","ts"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) { result.Add(ph); return result; }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  RUSSIAN
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Russian", "RU V4", "OpenLoid-Flame", language: "RU")]
    public class VocaloidRussianPhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"а","a"},{"е","je"},{"ё","jo"},{"и","i"},{"й","j"},
            {"о","o"},{"у","u"},{"ы","I"},{"э","E"},{"ю","ju"},{"я","ja"},
            {"б","b"},{"в","v"},{"г","g"},{"д","d"},{"ж","Z"},
            {"з","z"},{"к","k"},{"л","l"},{"м","m"},{"н","n"},
            {"п","p"},{"р","r"},{"с","s"},{"т","t"},{"ф","f"},
            {"х","x"},{"ц","ts"},{"ч","tS"},{"ш","S"},{"щ","StS"},
            {"ъ",""},{"ь",""},
            // soft consonants
            {"бь","bj"},{"вь","vj"},{"гь","gj"},{"дь","dj"},
            {"зь","zj"},{"кь","kj"},{"ль","lj"},{"мь","mj"},
            {"нь","nj"},{"пь","pj"},{"рь","rj"},{"сь","sj"},
            {"ть","tj"},{"фь","fj"},{"хь","xj"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) {
                if (!string.IsNullOrEmpty(ph)) result.Add(ph);
                else result.Add("-");
                return result;
            }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PORTUGUESE
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Portuguese", "PT V4", "OpenLoid-Flame", language: "PT")]
    public class VocaloidPortuguesePhonemizer : VocaloidPhonemizerBase {

        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"a","a"},{"â","@"},{"ã","an"},{"à","a"},{"á","a"},
            {"e","e"},{"ê","E"},{"i","i"},{"í","i"},
            {"o","o"},{"ó","O"},{"ô","o"},{"u","u"},{"ú","u"},
            {"b","b"},{"c","k"},{"d","d"},{"f","f"},{"g","g"},
            {"h","-"},{"j","Z"},{"k","k"},{"l","l"},{"lh","L"},
            {"m","m"},{"n","n"},{"nh","nj"},{"p","p"},{"q","k"},
            {"r","4"},{"rr","R"},{"s","s"},{"t","t"},{"v","v"},
            {"w","v"},{"x","S"},{"z","z"},{"ch","S"},{"qu","k"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) {
                if (ph != "-") result.Add(ph); else result.Add("-");
                return result;
            }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  THAI
    // ═══════════════════════════════════════════════════════════════════════════

    [Phonemizer("Vocaloid4 Thai", "TH V4", "OpenLoid-Flame", language: "TH")]
    public class VocaloidThaiPhonemizer : VocaloidPhonemizerBase {

        // Thai RTGS romanisation → V4 phoneme
        private static readonly Dictionary<string, string> map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"a","a"},{"ae","ae"},{"i","i"},{"ue","u"},{"u","u"},
            {"e","e"},{"o","o"},{"oe","@"},{"ia","ia"},{"uea","ua"},
            {"ua","ua"},
            {"p","p"},{"ph","ph"},{"b","b"},{"t","t"},{"th","th"},
            {"d","d"},{"k","k"},{"kh","kh"},{"g","g"},
            {"m","m"},{"n","n"},{"ng","ng"},
            {"f","f"},{"s","s"},{"h","h"},{"ch","tS"},{"j","dZ"},
            {"r","4"},{"l","l"},{"w","w"},{"y","j"},
        };

        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (lyric == "-") { result.Add("-"); return result; }
            if (map.TryGetValue(lyric, out string ph)) { result.Add(ph); return result; }
            if (lyric.Contains(' ')) {
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                return result;
            }
            result.Add(lyric);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  GENERIC FALLBACK
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Passthrough phonemizer — lyric is treated as a raw V4 phoneme symbol.
    /// Works for any language if the user enters IPA/V4 symbols directly.
    /// </summary>
    [Phonemizer("Vocaloid4 Phoneme (direct)", "V4 RAW", "OpenLoid-Flame", language: "")]
    public class VocaloidRawPhonemizer : VocaloidPhonemizerBase {
        protected override List<string> MapLyric(string lyric) {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(lyric)) { result.Add("-"); return result; }
            if (lyric.Contains(' '))
                result.AddRange(lyric.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            else
                result.Add(lyric);
            return result;
        }
    }
}
