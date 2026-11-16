// OpenUtau.Core/Vocaloid/VocaloidPhonemeSet.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Catálogo de símbolos de fonema (X-SAMPA / ARPAbet en minúsculas) que entienden los
// fonemizadores de VOCALOID y el sintetizador Flame. Cada símbolo lleva sus parámetros
// acústicos (formantes, ruido, tipo). Un símbolo que no está aquí se marca en rojo en el
// editor (VocaloidSinger.TryGetOto devuelve false).

using System;
using System.Collections.Generic;
using System.Globalization;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    internal enum PhonemeClass { Silence, Breath, Vowel, Nasal, Liquid, Glide, Fricative, Affricate, Plosive }

    /// <summary>Parámetros acústicos de un fonema.</summary>
    internal sealed class PhonemeSpec {
        public string Symbol = string.Empty;
        public PhonemeClass Class;
        public bool Voiced;
        // Formantes de inicio (F) y de fin (E) en Hz; E distinto de F forma un diptongo.
        public float F1, F2, F3, E1, E2, E3;
        public float B1 = 90f, B2 = 110f, B3 = 160f;
        // Ruido: frecuencia central, ancho de banda y ganancia.
        public float NoiseHz, NoiseBw, NoiseGain;
        public float Gain = 1f;
        public float Aspiration;
        public bool Trill;
        public bool Tap;

        public bool IsSonorant {
            get {
                return Class == PhonemeClass.Vowel || Class == PhonemeClass.Nasal
                    || Class == PhonemeClass.Liquid || Class == PhonemeClass.Glide;
            }
        }

        public bool IsConsonant {
            get { return Class != PhonemeClass.Vowel && Class != PhonemeClass.Silence && Class != PhonemeClass.Breath; }
        }

        /// <summary>Duración natural en ms de una consonante antes de la vocal.</summary>
        public double NaturalMs {
            get {
                switch (Class) {
                    case PhonemeClass.Plosive: return 55;
                    case PhonemeClass.Affricate: return 70;
                    case PhonemeClass.Fricative: return 75;
                    case PhonemeClass.Nasal: return 60;
                    case PhonemeClass.Liquid: return Tap ? 30 : 55;
                    case PhonemeClass.Glide: return 45;
                    case PhonemeClass.Breath: return 140;
                    case PhonemeClass.Silence: return 60;
                    default: return 0;
                }
            }
        }
    }

    internal static class VocaloidPhonemeSet {

        // Formato de línea: TIPO nombres,separados,por,comas parámetros...
        //   V  f1 f2 f3 [e1 e2 e3]   vocal (con e* = diptongo)
        //   N  f1 f2 f3              nasal
        //   L  f1 f2 f3 [gain]       líquida
        //   G  f1 f2 f3              deslizante
        //   F  voz hz bw gain        fricativa
        //   A  voz hz bw gain        africada
        //   P  voz hz bw gain asp    oclusiva
        //   S                        silencio
        //   B                        respiración
        private const string Table = @"
V a 800 1350 2800
V A,aa 840 1220 2810
V {,ae 860 2050 2850
V V,ah 760 1400 2780
V @ 550 1450 2750
V e 470 2250 2900
V E,eh 610 2330 2990
V i,iy,i: 320 2700 3300
V I,ih 430 2480 3070
V o 450 880 2700
V O,ao 590 920 2710
V u,uw,u: 370 950 2670
V M 380 1350 2300
V U,uh 470 1160 2680
V y 300 1900 2400
V Y 330 1700 2300
V 2 450 1500 2400
V } 400 1500 2300
V i0 400 1500 2400
V @r,er,3` 490 1350 1690
V 5 560 1700 2600
V a: 800 1350 2800
V e: 470 2250 2900
V o: 450 880 2700
V aI,ay 800 1300 2800 400 2300 3000
V aU,aw 800 1300 2800 400 1000 2500
V eI,ey 470 2250 2900 320 2600 3200
V oU,ow,@U 450 880 2700 370 950 2600
V OI,oy 590 920 2710 350 2200 3000
N m 250 1100 2200
N n 250 1700 2500
N ng,N 250 1400 2200
N J,nj 250 2100 2900
L l 360 1300 2800
L L,lh 300 2100 2900
L r 350 1300 1750
L 4 400 1500 2500 0.85
L rr 350 1400 2500 0.9
G j 280 2200 3000
G w,W 300 700 2200
F 0 s 7200 2800 1.0
F 1 z 7200 2800 0.9
F 0 S,sh 3400 2000 1.0
F 1 Z,zh 3400 2000 0.9
F 0 C 5000 3000 0.8
F 0 x 2400 1800 0.8
F 1 G 2000 1500 0.7
F 0 f 5500 4500 0.45
F 1 v 5500 4500 0.4
F 0 T,th 5800 4500 0.35
F 1 D,dh 5800 4500 0.35
F 0 h,H,hh 2000 3500 0.55
F 0 F,p\ 1600 3500 0.45
F 1 R 1500 1200 0.7
F 0 ss 7200 2800 1.0
A 0 ts 7000 2800 1.0
A 1 dz 7000 2800 0.9
A 0 tS,ch 3400 2000 1.0
A 1 dZ,jh 3400 2000 0.9
A 0 tC 4800 2800 0.9
A 1 dZ' 4800 2800 0.8
A 0 tCh 4800 2800 1.0
A 0 jj 4800 2800 0.8
P 0 p 900 1600 0.55 0.25
P 1 b 900 1600 0.45 0
P 0 t 4200 2800 0.6 0.3
P 1 d 4200 2800 0.5 0
P 0 k 2100 1800 0.6 0.35
P 1 g 2100 1800 0.5 0
P 0 kh,k' 2100 1800 0.65 0.7
P 0 th,t' 4200 2800 0.65 0.7
P 0 ph,p' 900 1600 0.6 0.7
P 0 kk 2100 1800 0.7 0.05
P 0 tt 4200 2800 0.7 0.05
P 0 pp 900 1600 0.65 0.05
S -,sp,SP,Sil,pau,cl,Q,?,q
B br,ap,AP
";

        private static readonly Dictionary<string, PhonemeSpec> specs
            = new Dictionary<string, PhonemeSpec>(StringComparer.Ordinal);

        private static float Num(string s) {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        static VocaloidPhonemeSet() {
            foreach (string raw in Table.Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string kind = p[0];
                string[] names;
                int a; // índice del primer parámetro numérico
                if (kind == "F" || kind == "A" || kind == "P") {
                    names = p[2].Split(',');
                    a = 3;
                } else {
                    names = p[1].Split(',');
                    a = 2;
                }
                foreach (string name in names) {
                    var s = new PhonemeSpec { Symbol = name };
                    switch (kind) {
                        case "V":
                            s.Class = PhonemeClass.Vowel; s.Voiced = true;
                            s.F1 = Num(p[a]); s.F2 = Num(p[a + 1]); s.F3 = Num(p[a + 2]);
                            if (p.Length >= a + 6) {
                                s.E1 = Num(p[a + 3]); s.E2 = Num(p[a + 4]); s.E3 = Num(p[a + 5]);
                            } else {
                                s.E1 = s.F1; s.E2 = s.F2; s.E3 = s.F3;
                            }
                            break;
                        case "N":
                        case "L":
                        case "G":
                            s.Class = kind == "N" ? PhonemeClass.Nasal : kind == "L" ? PhonemeClass.Liquid : PhonemeClass.Glide;
                            s.Voiced = true;
                            s.F1 = s.E1 = Num(p[a]); s.F2 = s.E2 = Num(p[a + 1]); s.F3 = s.E3 = Num(p[a + 2]);
                            if (kind == "N") s.Gain = 0.7f;
                            if (p.Length > a + 3) s.Gain = Num(p[a + 3]);
                            s.Tap = name == "4";
                            s.Trill = name == "rr";
                            break;
                        case "F":
                        case "A":
                        case "P":
                            s.Class = kind == "F" ? PhonemeClass.Fricative : kind == "A" ? PhonemeClass.Affricate : PhonemeClass.Plosive;
                            s.Voiced = p[1] == "1";
                            s.NoiseHz = Num(p[a]); s.NoiseBw = Num(p[a + 1]); s.NoiseGain = Num(p[a + 2]);
                            if (kind == "P" && p.Length > a + 3) s.Aspiration = Num(p[a + 3]);
                            s.F1 = s.E1 = 500f; s.F2 = s.E2 = 1500f; s.F3 = s.E3 = 2500f;
                            break;
                        case "S":
                            s.Class = PhonemeClass.Silence;
                            break;
                        case "B":
                            s.Class = PhonemeClass.Breath;
                            s.NoiseHz = 2500f; s.NoiseBw = 5000f; s.NoiseGain = 0.25f;
                            break;
                        default:
                            continue;
                    }
                    specs[name] = s;
                }
            }
        }

        /// <summary>Busca el símbolo tal cual, o sin sufijo de longitud / acento.</summary>
        internal static bool TryGet(string? symbol, out PhonemeSpec spec) {
            spec = null!;
            if (string.IsNullOrEmpty(symbol)) return false;
            if (specs.TryGetValue(symbol, out var found)) { spec = found; return true; }
            if (symbol.Length > 1) {
                char last = symbol[symbol.Length - 1];
                if (last == ':' || last == '0' || last == '1' || last == '2') {
                    if (specs.TryGetValue(symbol.Substring(0, symbol.Length - 1), out found)) { spec = found; return true; }
                }
            }
            return false;
        }

        private static readonly Dictionary<string, UOto> otoCache = new Dictionary<string, UOto>(StringComparer.Ordinal);

        /// <summary>Oto sintético para un símbolo conocido (los bancos VOCALOID no tienen oto.ini).</summary>
        internal static bool TryGetOto(string? symbol, out UOto oto) {
            oto = default!;
            if (symbol == null || !Contains(symbol)) return false;
            lock (otoCache) {
                if (!otoCache.TryGetValue(symbol, out UOto? cached)) {
                    cached = UOto.OfDummy(symbol);
                    otoCache[symbol] = cached;
                }
                oto = cached;
            }
            return true;
        }

        internal static bool Contains(string? symbol) {
            return TryGet(symbol, out _);
        }

        internal static IEnumerable<string> AllSymbols {
            get { return specs.Keys; }
        }
    }
}
