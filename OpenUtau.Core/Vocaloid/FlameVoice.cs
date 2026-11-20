// OpenUtau.Core/Vocaloid/FlameVoice.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// "Flame": sintetizador de fuente y filtro (glotis + formantes + ruido) que canta a partir de
// la secuencia de fonemas, el pitch y las curvas de la frase. No depende de ningún archivo
// del banco, por eso siempre produce sonido. Cuando exista un decodificador de muestras
// .ddb/.ddi se conecta por IVocaloidSampleSource en VocaloidRenderer.
//
// Modelo: onda glotal de Rosenberg (derivada) -> 3 formantes variables + 2 fijos (resonadores
// de Klatt); ruido blanco -> filtro pasabanda para fricativas y explosiones. Los parámetros se
// actualizan cada 64 muestras y se suavizan para que las transiciones no hagan clics.
//
// Humanización: vibrato que entra tarde en notas largas, scoop de afinación y soplo al atacar la
// vocal, soplo al soltarla, deriva lenta de pitch, jitter y shimmer por periodo, tilt espectral
// según tensión y respiración, ligera oscilación de formantes y filtro de salida suave.
// Todo se controla con Humanness (0 = voz robótica de antes, 1 = natural, hasta 1.5).
//
// Voz base: DefaultLoid es HOMBRE. La tabla de fonemas trae formantes neutros/femeninos, así que
// se escalan con VoiceFormantScale (≈0.86, tracto vocal más largo) y el tono por defecto es grave.
//
// Expresiones: Natural, Cute, Joyful, Rapper, Hard, Cold, Love, Sad, Angry, Whisper. Cada una es
// un perfil (ExpressionProfile) que cambia vibrato, ataque, aire, brillo, formantes, consonantes,
// dureza y cuánto "robot" queda (snap de afinación + saturación). Se elige con
// FlameVoice.Expression = FlameExpression.Cute o pasando la expresión a Synthesize por frase.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.Vocaloid {

    internal enum FlameExpression { Natural, Cute, Joyful, Rapper, Hard, Cold, Love, Sad, Angry, Whisper }

    /// <summary>Todos los números que definen cómo canta una expresión.</summary>
    internal sealed class ExpressionProfile {
        public double VibratoCents = 28, VibratoOnsetMs = 200, VibratoRateHz = 5.6;
        public double ScoopCents = 22;          // sube desde abajo al atacar (negativo = baja)
        public double Drift = 1, Jitter = 1, Shimmer = 1;   // irregularidad humana
        public double Breath = 0, OnsetBreath = 0.22, EndBreath = 0.25;  // aire constante, al atacar, al soltar
        public double Tension = 0;              // -1 suave .. +1 apretada
        public double Formant = 0;              // + voz más chica/aguda, - más grande/grave
        public double Brightness = 0;           // Hz extra en el filtro de brillo
        public double AttackMs = 4, ReleaseMs = 12;   // cuánto tarda en prender y apagar la voz
        public double Swell = 0.12;             // empuje de volumen al atacar
        public double Volume = 1, Consonant = 1;
        public double Drive = 1;                // saturación (grit)
        public double Robot = 0.12;             // 0 humano puro .. 1 autotune duro

        public static ExpressionProfile Get(FlameExpression e) {
            switch (e) {
                case FlameExpression.Cute:
                    return new ExpressionProfile {
                        VibratoCents = 14, VibratoOnsetMs = 150, VibratoRateHz = 6.4, ScoopCents = 30,
                        Drift = 1.3, Breath = 0.12, OnsetBreath = 0.3, Tension = -0.2, Formant = 0.55,
                        Brightness = 600, AttackMs = 3, ReleaseMs = 10, Swell = 0.2, Consonant = 0.8, Robot = 0.15 };
                case FlameExpression.Joyful:
                    return new ExpressionProfile {
                        VibratoCents = 34, VibratoOnsetMs = 120, VibratoRateHz = 6.2, ScoopCents = 35,
                        Drift = 1.4, Jitter = 1.2, OnsetBreath = 0.3, Tension = 0.35, Formant = 0.2,
                        Brightness = 1200, AttackMs = 3, ReleaseMs = 10, Swell = 0.2, Volume = 1.1, Consonant = 1.1, Robot = 0.1 };
                case FlameExpression.Rapper:
                    return new ExpressionProfile {
                        VibratoCents = 0, ScoopCents = 4, Drift = 0.5, Shimmer = 1.2, OnsetBreath = 0.05, EndBreath = 0.05,
                        Tension = 0.5, Formant = -0.1, Brightness = 800, AttackMs = 1.5, ReleaseMs = 6, Swell = 0.0,
                        Volume = 1.1, Consonant = 1.5, Drive = 1.25, Robot = 0.3 };
                case FlameExpression.Hard:
                    return new ExpressionProfile {
                        VibratoCents = 8, VibratoOnsetMs = 450, ScoopCents = 0, Jitter = 1.8, Shimmer = 1.6,
                        OnsetBreath = 0.0, EndBreath = 0.05, Tension = 0.9, Formant = -0.35, Brightness = 1800,
                        AttackMs = 1.5, ReleaseMs = 8, Swell = 0.05, Volume = 1.1, Consonant = 1.6, Drive = 1.7, Robot = 0.2 };
                case FlameExpression.Cold:
                    return new ExpressionProfile {
                        VibratoCents = 0, ScoopCents = 0, Drift = 0.15, Jitter = 0.2, Shimmer = 0.2,
                        Breath = 0.10, OnsetBreath = 0.05, EndBreath = 0.1, Tension = 0.1, Formant = -0.15,
                        Brightness = -1200, AttackMs = 6, ReleaseMs = 14, Swell = 0.0, Consonant = 0.9, Robot = 0.55 };
                case FlameExpression.Love:
                    return new ExpressionProfile {
                        VibratoCents = 24, VibratoOnsetMs = 300, VibratoRateHz = 5.0, ScoopCents = 18,
                        Drift = 1.2, Jitter = 0.8, Breath = 0.3, OnsetBreath = 0.35, EndBreath = 0.35,
                        Tension = -0.5, Formant = 0.1, Brightness = -600, AttackMs = 8, ReleaseMs = 22, Swell = 0.18,
                        Volume = 0.95, Consonant = 0.7, Robot = 0.08 };
                case FlameExpression.Sad:
                    return new ExpressionProfile {
                        VibratoCents = 22, VibratoOnsetMs = 350, VibratoRateHz = 4.6, ScoopCents = -12,
                        Drift = 1.6, Breath = 0.25, OnsetBreath = 0.3, EndBreath = 0.4, Tension = -0.6,
                        Formant = -0.05, Brightness = -900, AttackMs = 9, ReleaseMs = 24, Swell = 0.1,
                        Volume = 0.9, Consonant = 0.75, Robot = 0.1 };
                case FlameExpression.Angry:
                    return new ExpressionProfile {
                        VibratoCents = 0, ScoopCents = 8, Jitter = 2.0, Shimmer = 1.8, OnsetBreath = 0.0, EndBreath = 0.0,
                        Tension = 1.0, Formant = -0.25, Brightness = 1500, AttackMs = 1.5, ReleaseMs = 7, Swell = 0.0,
                        Volume = 1.15, Consonant = 1.5, Drive = 1.8, Robot = 0.2 };
                case FlameExpression.Whisper:
                    return new ExpressionProfile {
                        VibratoCents = 0, ScoopCents = 0, Drift = 0.5, Breath = 0.85, OnsetBreath = 0.2, EndBreath = 0.2,
                        Tension = -0.7, Brightness = 800, AttackMs = 6, ReleaseMs = 14, Swell = 0.0,
                        Volume = 0.85, Consonant = 1.0, Robot = 0.1 };
                default:
                    return new ExpressionProfile();
            }
        }

        /// <summary>Acepta nombres en inglés o español (cute, lindo, joyful, alegre, frio, amor...).</summary>
        public static bool TryParse(string? name, out FlameExpression result) {
            result = FlameExpression.Natural;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim().ToLowerInvariant();
            switch (n) {
                case "lindo": case "tierno": case "kawaii": n = "cute"; break;
                case "alegre": case "feliz": case "happy": n = "joyful"; break;
                case "rap": n = "rapper"; break;
                case "duro": case "fuerte": n = "hard"; break;
                case "frio": case "frío": case "fria": case "fría": n = "cold"; break;
                case "amor": case "romantico": case "romántico": n = "love"; break;
                case "triste": n = "sad"; break;
                case "enojado": case "furioso": case "rabia": n = "angry"; break;
                case "susurro": case "suave": n = "whisper"; break;
            }
            return Enum.TryParse(n, true, out result);
        }
    }

    internal static class FlameVoice {

        public const int SampleRate = 44100;
        private const int Block = 64;
        private const double BlockMs = Block * 1000.0 / SampleRate;

        /// <summary>Cuánta humanidad se mezcla: 0 desactiva todo, 1 por defecto, máx 1.5.</summary>
        public static double Humanness = 1.0;
        /// <summary>Expresión por defecto para todas las frases (se puede sobreescribir por frase).</summary>
        public static FlameExpression Expression { get; set; } = FlameExpression.Natural;

        /// <summary>Escala de formantes de la voz base. 1.0 = tabla original, 0.86 = hombre adulto.</summary>
        public static double VoiceFormantScale = 0.86;
        /// <summary>Tono (Hz) cuando la frase no trae curva de pitch. 130.81 = C3, rango masculino.</summary>
        public static double DefaultF0Hz = 130.81;

        /// <summary>Lee flame-expression.txt junto al banco (una palabra: cute, rapper, frio...). null si no hay.</summary>
        public static FlameExpression? ResolveExpression(string? singerLocation) {
            try {
                if (string.IsNullOrEmpty(singerLocation)) return null;
                string dir = Directory.Exists(singerLocation) ? singerLocation! : (Path.GetDirectoryName(singerLocation) ?? "");
                string file = Path.Combine(dir, "flame-expression.txt");
                if (!File.Exists(file)) return null;
                if (ExpressionProfile.TryParse(File.ReadAllText(file), out FlameExpression e)) return e;
            } catch { }
            return null;
        }

        private sealed class Seg {
            public PhonemeSpec Spec = null!;
            public double StartMs;
            public double EndMs;
            public float Volume = 1f;
            public int NextSonorant = -1;
        }

        private static float Smooth(float x) {
            x = Math.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static double Curve(float[]? arr, double index) {
            if (arr == null || arr.Length == 0) return 0.0;
            if (index <= 0) return arr[0];
            if (index >= arr.Length - 1) return arr[arr.Length - 1];
            int i0 = (int)index;
            double frac = index - i0;
            return arr[i0] + (arr[i0 + 1] - arr[i0]) * frac;
        }

        /// <summary>Genera el audio mono de 44100 Hz de la frase. samples[0] cae en positionMs - leadingMs.</summary>
        public static float[] Synthesize(RenderPhrase phrase, double tailMs, CancellationToken token, FlameExpression? expression = null) {
            double t0 = phrase.positionMs - phrase.leadingMs;
            int total = Math.Max(1, (int)Math.Ceiling((phrase.durationMs + phrase.leadingMs + tailMs) * SampleRate / 1000.0));
            var output = new float[total];

            // ── segmentos ──────────────────────────────────────────────────
            var segs = new List<Seg>();
            foreach (RenderPhone phone in phrase.phones) {
                if (!VocaloidPhonemeSet.TryGet(phone.phoneme, out PhonemeSpec spec)) continue;
                double s = phone.positionMs - t0;
                double e = phone.endMs - t0;
                if (e <= s) e = s + 1.0;
                segs.Add(new Seg { Spec = spec, StartMs = s, EndMs = e, Volume = Math.Clamp(phone.volume, 0f, 2f) });
            }
            for (int i = 0; i < segs.Count; i++) {
                for (int j = i + 1; j < segs.Count; j++) {
                    if (segs[j].Spec.IsSonorant) { segs[i].NextSonorant = j; break; }
                }
            }

            int pitchStart = phrase.position - phrase.leading;
            float[] pitches = phrase.pitches;

            // ── estado ─────────────────────────────────────────────────────
            double f1 = 500, f2 = 1500, f3 = 2500;
            double voice = 0, noise = 0, noiseHz = 2000, noiseBw = 2000;
            var ra = new double[5]; var rb = new double[5]; var rc = new double[5];
            var ry1 = new double[5]; var ry2 = new double[5];
            var fr = new double[5];
            var bw = new double[] { 90.0, 110.0, 160.0, 300.0, 400.0 };
            double bpx1 = 0, bpx2 = 0, bpy1 = 0, bpy2 = 0;
            double phase = 0, gPrev = 0;
            uint rng = 2463534242u;
            int segIdx = 0;

            // ── humanización ───────────────────────────────────────────────
            double hum = Math.Clamp(Humanness, 0.0, 1.5);
            ExpressionProfile xp = ExpressionProfile.Get(expression ?? Expression);
            double rob = Math.Clamp(xp.Robot, 0.0, 1.0);
            double drive = Math.Max(xp.Drive, 0.5) + 0.5 * rob;
            double driveInv = 1.0 / Math.Sqrt(drive);
            double drift = 0, drift2 = 0;            // deriva lenta de pitch y de la tasa del vibrato
            double vibPhase = 0, vibRate = 5.6;
            double jitter = 1.0, shimmer = 1.0;      // variación por periodo glotal
            double tiltY = 0;                         // inclinación espectral de la fuente
            double lpY = 0, dcX = 0, dcY = 0;         // filtro de salida y bloqueo de continua
            double Rand() {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                return rng / 2147483648.0 - 1.0;
            }

            for (int b0 = 0; b0 < total; b0 += Block) {
                if ((b0 / Block) % 256 == 0 && token.IsCancellationRequested) break;

                double tms = (b0 + Block / 2) * 1000.0 / SampleRate;
                while (segIdx < segs.Count && segs[segIdx].EndMs <= tms) segIdx++;
                Seg? seg = null;
                // el solape de fonemas es raro; se busca desde segIdx hacia delante
                for (int k = segIdx; k < segs.Count && segs[k].StartMs <= tms; k++) {
                    if (segs[k].EndMs > tms) { seg = segs[k]; }
                }

                double vT = 0, nT = 0;
                double extraBreath = 0, vibCents = 0, scoopCents = 0;
                double tF1 = f1, tF2 = f2, tF3 = f3;
                double tHz = noiseHz, tBw = noiseBw;

                if (seg != null) {
                    PhonemeSpec sp = seg.Spec;
                    double d = Math.Max(seg.EndMs - seg.StartMs, 1e-3);
                    float p = (float)Math.Clamp((tms - seg.StartMs) / d, 0.0, 1.0);
                    double tIn = tms - seg.StartMs;

                    if (sp.IsSonorant) {
                        float sm = Smooth(p);
                        tF1 = sp.F1 + (sp.E1 - sp.F1) * sm;
                        tF2 = sp.F2 + (sp.E2 - sp.F2) * sm;
                        tF3 = sp.F3 + (sp.E3 - sp.F3) * sm;
                    } else if (seg.NextSonorant >= 0) {
                        PhonemeSpec nx = segs[seg.NextSonorant].Spec;
                        tF1 = nx.F1; tF2 = nx.F2; tF3 = nx.F3;
                    } else if (sp.F1 > 0) {
                        tF1 = sp.F1; tF2 = sp.F2; tF3 = sp.F3;
                    }

                    switch (sp.Class) {
                        case PhonemeClass.Vowel: {
                            // ataque con un pequeño empuje en vez de golpe plano
                            vT = (1.0 - xp.Swell) + xp.Swell * Smooth((float)(tIn / Math.Max(xp.AttackMs * 11.0, 15.0)));
                            if (hum > 0) {
                                double toEnd = seg.EndMs - tms;
                                if (tIn < 70.0) {
                                    // scoop desde abajo y un soplo de aire al empezar
                                    scoopCents = -xp.ScoopCents * (1.0 - Smooth((float)(tIn / 70.0)));
                                    extraBreath = xp.OnsetBreath * (1.0 - Smooth((float)(tIn / 60.0)));
                                }
                                if (xp.VibratoCents > 0 && d > xp.VibratoOnsetMs + 120.0) {
                                    // vibrato que entra tarde y se apaga al soltar la nota
                                    double vin = Smooth((float)((tIn - xp.VibratoOnsetMs) / 350.0));
                                    double vout = Smooth((float)(toEnd / 90.0));
                                    vibCents = xp.VibratoCents * vin * vout * (1.0 + 8.0 * drift2);
                                }
                                if (seg.NextSonorant < 0 && toEnd < 90.0) {
                                    // se escapa aire al terminar la frase
                                    extraBreath += xp.EndBreath * (1.0 - Smooth((float)(toEnd / 90.0)));
                                }
                            }
                            break;
                        }
                        case PhonemeClass.Nasal:
                            vT = 0.55 * sp.Gain / 0.7;
                            break;
                        case PhonemeClass.Liquid:
                            vT = 0.9 * sp.Gain;
                            if (sp.Tap) {
                                double sn = Math.Sin(Math.PI * p);
                                vT *= 1.0 - 0.8 * sn * sn;
                            }
                            if (sp.Trill) vT *= 0.55 + 0.45 * Math.Cos(2.0 * Math.PI * 25.0 * tIn / 1000.0);
                            break;
                        case PhonemeClass.Glide:
                            vT = 0.9;
                            break;
                        case PhonemeClass.Fricative: {
                            vT = sp.Voiced ? 0.3 : 0.0;
                            double env = Math.Min(1.0, Math.Min(p / 0.15, (1.0 - p) / 0.2));
                            nT = sp.NoiseGain * Math.Max(env, 0.0);
                            tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            break;
                        }
                        case PhonemeClass.Affricate: {
                            if (p < 0.3) {
                                vT = sp.Voiced ? 0.15 : 0.0;
                            } else {
                                double q = (p - 0.3) / 0.7;
                                nT = sp.NoiseGain * Math.Max(0.0, Math.Min(1.0, Math.Min(q / 0.1, (1.0 - q) / 0.2)));
                                vT = sp.Voiced ? 0.3 : 0.0;
                                tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            }
                            break;
                        }
                        case PhonemeClass.Plosive: {
                            const double burst = 12.0;
                            double asp = sp.Voiced ? 0.0 : sp.Aspiration * 35.0;
                            double clos = Math.Max(d - burst - asp, 0.3 * d);
                            if (tIn < clos) {
                                vT = sp.Voiced ? 0.2 : 0.0;
                            } else if (tIn < clos + burst) {
                                nT = sp.NoiseGain * 1.5;
                                vT = sp.Voiced ? 0.5 : 0.0;
                            } else {
                                nT = sp.NoiseGain * 0.4 * Math.Max(sp.Aspiration, 0.1);
                            }
                            tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            break;
                        }
                        case PhonemeClass.Breath:
                            nT = sp.NoiseGain * Math.Min(1.0, Math.Min(p / 0.2, (1.0 - p) / 0.3));
                            tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            break;
                        default:
                            break;
                    }
                    vT *= seg.Volume * xp.Volume;
                    nT *= seg.Volume * xp.Consonant;
                }

                // ── curvas de la frase (índices cada 5 ticks) ──────────────
                double absMs = t0 + b0 * 1000.0 / SampleRate;
                double idx = (phrase.timeAxis.MsPosToNonExactTickPos(absMs) - pitchStart) / 5.0;
                double genderCurve = Curve(phrase.gender, idx);
                double breath = Math.Clamp(Curve(phrase.breathiness, idx) / 100.0 + (extraBreath + xp.Breath) * hum, 0.0, 1.0);
                double tension = Math.Clamp(Curve(phrase.tension, idx) / 100.0 + xp.Tension, -1.0, 1.0);
                double fScale = VoiceFormantScale * Math.Pow(2.0, Math.Clamp(genderCurve / 100.0 + xp.Formant, -1.0, 1.0) * 0.3);
                tF1 *= fScale; tF2 *= fScale; tF3 *= fScale;

                // micro-variaciones humanas: nunca dos bloques idénticos
                drift += (Rand() * 0.06 * xp.Drift * (1.0 - 0.6 * rob) - drift) * (1.0 - Math.Exp(-BlockMs / 60.0));
                drift2 += (Rand() - drift2) * 0.004;
                vibRate = xp.VibratoRateHz + 6.0 * drift2;
                vibPhase += vibRate * BlockMs / 1000.0;
                if (vibPhase >= 1.0) vibPhase -= 1.0;
                double fw = 1.0 + 1.5 * drift * hum;
                tF1 *= fw; tF2 *= fw; tF3 *= fw;

                // ── suavizado ──────────────────────────────────────────────
                double a = 1.0 - Math.Exp(-BlockMs / (15.0 * (1.0 - 0.5 * rob)));
                f1 += (tF1 - f1) * a; f2 += (tF2 - f2) * a; f3 += (tF3 - f3) * a;
                voice += (vT - voice) * (1.0 - Math.Exp(-BlockMs / (vT < voice ? xp.ReleaseMs : xp.AttackMs)));
                noise += (nT - noise) * (1.0 - Math.Exp(-BlockMs / 2.5));
                double ah = 1.0 - Math.Exp(-BlockMs / 8.0);
                noiseHz += (tHz - noiseHz) * ah;
                noiseBw += (tBw - noiseBw) * ah;

                bool silent = voice < 1e-4 && noise < 1e-4;

                // ── coeficientes ───────────────────────────────────────────
                fr[0] = f1; fr[1] = f2; fr[2] = f3; fr[3] = 3600.0 * fScale; fr[4] = 4600.0 * fScale;
                for (int r = 0; r < 5; r++) {
                    double fc = Math.Min(Math.Max(fr[r], 50.0), SampleRate * 0.45);
                    double c = -Math.Exp(-2.0 * Math.PI * bw[r] / SampleRate);
                    double bq = 2.0 * Math.Exp(-Math.PI * bw[r] / SampleRate) * Math.Cos(2.0 * Math.PI * fc / SampleRate);
                    ra[r] = 1.0 - bq - c; rb[r] = bq; rc[r] = c;
                }
                double nfc = Math.Min(Math.Max(noiseHz * Math.Sqrt(VoiceFormantScale), 200.0), SampleRate * 0.45);
                double q2 = Math.Max(nfc / Math.Max(noiseBw, 100.0), 0.3);
                double w0 = 2.0 * Math.PI * nfc / SampleRate;
                double al = Math.Sin(w0) / (2.0 * q2);
                double bb0 = al / (1.0 + al);
                double ba1 = -2.0 * Math.Cos(w0) / (1.0 + al);
                double ba2 = (1.0 - al) / (1.0 + al);

                // vocales cerradas suenan más flojas con filtros de ganancia unidad en continua
                double comp = Math.Min(2.6, Math.Pow(800.0 / Math.Max(f1, 200.0), 1.1));
                double tp = Math.Clamp(0.43 - 0.08 * tension, 0.28, 0.5);
                const double tn = 0.16;
                // voz tensa = más brillo, voz soplada = más oscura
                double tiltFc = Math.Clamp(4200.0 + 2200.0 * tension - 1800.0 * breath + xp.Brightness, 1500.0, 9000.0);
                double tiltA = 1.0 - Math.Exp(-2.0 * Math.PI * tiltFc / SampleRate);

                double fa = F0At(phrase, pitches, t0, b0, pitchStart, rob * 0.6);
                double fb = F0At(phrase, pitches, t0, b0 + Block, pitchStart, rob * 0.6);
                double pm = Math.Pow(2.0, hum * (vibCents * Math.Sin(2.0 * Math.PI * vibPhase) + scoopCents) / 1200.0)
                            * (1.0 + drift * hum);
                fa *= pm; fb *= pm;

                if (silent) {
                    Array.Clear(ry1, 0, 5); Array.Clear(ry2, 0, 5);
                    bpx1 = bpx2 = bpy1 = bpy2 = 0;
                }
                int end = Math.Min(Block, total - b0);
                for (int k = 0; k < end; k++) {
                    rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                    double nz = rng / 2147483648.0 - 1.0;

                    double sample = 0.0;
                    if (!silent) {
                        double f0 = fa + (fb - fa) * k / Block;
                        phase += f0 * jitter / SampleRate;
                        if (phase >= 1.0) {
                            phase -= 1.0;
                            jitter = 1.0 + Rand() * 0.004 * hum * xp.Jitter * (1.0 - 0.6 * rob);
                            shimmer = 1.0 + Rand() * 0.04 * hum * xp.Shimmer * (1.0 - 0.6 * rob);
                        }
                        double g;
                        if (phase < tp) g = 0.5 * (1.0 - Math.Cos(Math.PI * phase / tp));
                        else if (phase < tp + tn) g = Math.Cos(0.5 * Math.PI * (phase - tp) / tn);
                        else g = 0.0;
                        double ex = (g - gPrev) * (SampleRate / f0) * 0.012;
                        gPrev = g;
                        tiltY += (ex - tiltY) * tiltA;
                        ex = tiltY * shimmer;
                        ex = ex * (1.0 - 0.6 * breath) + nz * 0.15 * breath + nz * 0.01;

                        double x = ex * voice * comp;
                        for (int r = 0; r < 5; r++) {
                            double y = ra[r] * x + rb[r] * ry1[r] + rc[r] * ry2[r];
                            ry2[r] = ry1[r]; ry1[r] = y; x = y;
                        }
                        double xn = nz * 0.7;
                        double yb = bb0 * xn - bb0 * bpx2 - ba1 * bpy1 - ba2 * bpy2;
                        bpx2 = bpx1; bpx1 = xn; bpy2 = bpy1; bpy1 = yb;
                        sample = x + yb * noise * 1.6;
                    }
                    double dc = sample - dcX + 0.995 * dcY; dcX = sample; dcY = dc;
                    lpY += (dc - lpY) * 0.7;
                    output[b0 + k] = (float)(Math.Tanh(lpY * drive) * driveInv);
                }
            }
            return output;
        }

        /// <summary>Frecuencia fundamental (Hz) en la muestra sampleIndex de la frase.</summary>
        private static double F0At(RenderPhrase phrase, float[] pitches, double t0, int sampleIndex, int pitchStart, double snap = 0.0) {
            if (pitches == null || pitches.Length == 0) return DefaultF0Hz;
            double ms = t0 + sampleIndex * 1000.0 / SampleRate;
            double idx = (phrase.timeAxis.MsPosToNonExactTickPos(ms) - pitchStart) / 5.0;
            double cents = Curve(pitches, idx);
            if (snap > 0) cents += (Math.Round(cents / 100.0) * 100.0 - cents) * snap;   // autotune suave
            double hz = 440.0 * Math.Pow(2.0, (cents / 100.0 - 69.0) / 12.0);
            return Math.Clamp(hz, 55.0, 1400.0);
        }
    }
}
