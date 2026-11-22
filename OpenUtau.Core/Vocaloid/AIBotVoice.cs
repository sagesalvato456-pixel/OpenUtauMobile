// OpenUtau.Core/Vocaloid/AIBotVoice.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Voz "AIBot": hombre de 17 años, grave, Hard, directa y a veces dulce.
// Mismo motor que FlameVoice (glotis de Rosenberg + formantes de Klatt + ruido), pero con un
// perfil propio que se mezcla entre dos extremos según la frase:
//
//   HARD  (por defecto): formantes bajos, resonancias estrechas, glotis tensa y brillante,
//                        ataque rápido, explosiones fuertes, un poco de saturación.
//   DULCE:               formantes algo más altos, resonancias anchas, glotis relajada,
//                        ataque suave, algo de aire y sin saturación.
//
// Qué manda el modo (suavizado cada bloque, sin clics):
//   - curva TENC  (tension)      positiva -> más hard, negativa -> más dulce
//   - curva BREC  (breathiness)  más aire -> más dulce
//   - volumen del fonema         notas flojas (VOL/VEL bajos) -> más dulce
//   - curva GENC  (gender)       sigue funcionando encima del perfil
//
// ── Cómo conectarlo (VocaloidRenderer.cs, dentro de Render) ───────────────────────────────
//   samples = AIBotVoice.Matches(phrase.singer.Name)
//       ? AIBotVoice.Synthesize(phrase, kTailMs, cancellation.Token)
//       : FlameVoice.Synthesize(phrase, kTailMs, cancellation.Token);
// Si el cantante se llama distinto, cambia SingerName aquí abajo.

using System;
using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.Vocaloid {

    internal static class AIBotVoice {

        public const string SingerName = "AIBot";
        public const int SampleRate = 44100;
        private const int Block = 64;
        private const double BlockMs = Block * 1000.0 / SampleRate;

        /// <summary>true si el cantante es AIBot (sin distinguir mayúsculas).</summary>
        public static bool Matches(string? singerName) {
            return !string.IsNullOrEmpty(singerName)
                && singerName!.IndexOf(SingerName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── perfil de voz ──────────────────────────────────────────────────────

        private readonly struct Profile {
            public readonly double Formant;   // escala del tracto: <1 = más grave
            public readonly double BwMul;     // ancho de banda de formantes
            public readonly double OpenQ;     // fase abierta de la glotis (menor = más brillante)
            public readonly double Drive;     // saturación
            public readonly double AttackMs;  // subida de la voz
            public readonly double ReleaseMs; // bajada de la voz
            public readonly double GlideMs;   // velocidad de los formantes entre fonemas
            public readonly double BurstMul;  // fuerza de las explosiones (p t k b d g)
            public readonly double BurstMs;   // duración de la explosión
            public readonly double Chest;     // cuerpo grave del pecho
            public readonly double Air;       // aire extra bajo las vocales
            public readonly double Jitter;    // inestabilidad mínima del tono

            public Profile(double formant, double bwMul, double openQ, double drive, double attackMs,
                           double releaseMs, double glideMs, double burstMul, double burstMs,
                           double chest, double air, double jitter) {
                Formant = formant; BwMul = bwMul; OpenQ = openQ; Drive = drive; AttackMs = attackMs;
                ReleaseMs = releaseMs; GlideMs = glideMs; BurstMul = burstMul; BurstMs = burstMs;
                Chest = chest; Air = air; Jitter = jitter;
            }
        }

        //                                         form  bw    openQ drive atk  rel  glide burst bMs  chest air   jitter
        private static readonly Profile Hard  = new Profile(0.91, 0.85, 0.33, 2.2, 2.5,  9.0,  9.0, 1.35,  9.0, 1.00, 0.00, 0.0035);
        private static readonly Profile Sweet = new Profile(0.96, 1.20, 0.46, 1.1, 6.0, 16.0, 17.0, 0.80, 14.0, 0.65, 0.18, 0.0010);

        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        private static Profile Mix(double sweet) {
            double s = Math.Clamp(sweet, 0.0, 1.0);
            return new Profile(
                Lerp(Hard.Formant, Sweet.Formant, s), Lerp(Hard.BwMul, Sweet.BwMul, s),
                Lerp(Hard.OpenQ, Sweet.OpenQ, s), Lerp(Hard.Drive, Sweet.Drive, s),
                Lerp(Hard.AttackMs, Sweet.AttackMs, s), Lerp(Hard.ReleaseMs, Sweet.ReleaseMs, s),
                Lerp(Hard.GlideMs, Sweet.GlideMs, s), Lerp(Hard.BurstMul, Sweet.BurstMul, s),
                Lerp(Hard.BurstMs, Sweet.BurstMs, s), Lerp(Hard.Chest, Sweet.Chest, s),
                Lerp(Hard.Air, Sweet.Air, s), Lerp(Hard.Jitter, Sweet.Jitter, s));
        }

        // ── utilidades ─────────────────────────────────────────────────────────

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
        public static float[] Synthesize(RenderPhrase phrase, double tailMs, CancellationToken token) {
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
            const int Res = 5;
            double f1 = 450, f2 = 1400, f3 = 2400;
            double voice = 0, noise = 0, noiseHz = 2000, noiseBw = 2000;
            double sweetSm = 0.28;                       // empieza en el modo Hard
            var ra = new double[Res]; var rb = new double[Res]; var rc = new double[Res];
            var ry1 = new double[Res]; var ry2 = new double[Res];
            var fr = new double[Res];
            var bwBase = new double[] { 90.0, 110.0, 160.0, 300.0, 400.0 };
            var bw = new double[Res];
            // resonador paralelo de pecho (cuerpo grave)
            double ca = 0, cb = 0, cc = 0, cy1 = 0, cy2 = 0;
            double bpx1 = 0, bpx2 = 0, bpy1 = 0, bpy2 = 0;
            double phase = 0, gPrev = 0;
            uint rng = 2463534242u;
            int segIdx = 0;

            for (int b0 = 0; b0 < total; b0 += Block) {
                if ((b0 / Block) % 256 == 0 && token.IsCancellationRequested) break;

                double tms = (b0 + Block / 2) * 1000.0 / SampleRate;
                while (segIdx < segs.Count && segs[segIdx].EndMs <= tms) segIdx++;
                Seg? seg = null;
                for (int k = segIdx; k < segs.Count && segs[k].StartMs <= tms; k++) {
                    if (segs[k].EndMs > tms) { seg = segs[k]; }
                }

                // ── curvas de la frase (índices cada 5 ticks) ──────────────
                double absMs = t0 + b0 * 1000.0 / SampleRate;
                double idx = (phrase.timeAxis.MsPosToNonExactTickPos(absMs) - pitchStart) / 5.0;
                double genderCurve = Curve(phrase.gender, idx);
                double breath = Math.Clamp(Curve(phrase.breathiness, idx) / 100.0, 0.0, 1.0);
                double tension = Math.Clamp(Curve(phrase.tension, idx) / 100.0, -1.0, 1.0);

                // ── modo Hard <-> Dulce ────────────────────────────────────
                double hard = 0.72 + 0.6 * tension - 0.8 * breath;
                if (seg != null) hard -= Math.Clamp(0.8 - seg.Volume, 0.0, 0.6);
                double sweetT = 1.0 - Math.Clamp(hard, 0.0, 1.0);
                sweetSm += (sweetT - sweetSm) * (1.0 - Math.Exp(-BlockMs / 20.0));
                Profile pf = Mix(sweetSm);

                double vT = 0, nT = 0;
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
                        case PhonemeClass.Vowel:
                            vT = 1.0;
                            break;
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
                            double env = Math.Min(1.0, Math.Min(p / 0.12, (1.0 - p) / 0.2));
                            nT = sp.NoiseGain * Math.Max(env, 0.0);
                            tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            break;
                        }
                        case PhonemeClass.Affricate: {
                            if (p < 0.3) {
                                vT = sp.Voiced ? 0.15 : 0.0;
                            } else {
                                double q = (p - 0.3) / 0.7;
                                nT = sp.NoiseGain * Math.Max(0.0, Math.Min(1.0, Math.Min(q / 0.08, (1.0 - q) / 0.2)));
                                vT = sp.Voiced ? 0.3 : 0.0;
                                tHz = sp.NoiseHz; tBw = sp.NoiseBw;
                            }
                            break;
                        }
                        case PhonemeClass.Plosive: {
                            double burst = pf.BurstMs;
                            double asp = sp.Voiced ? 0.0 : sp.Aspiration * 35.0;
                            double clos = Math.Max(d - burst - asp, 0.3 * d);
                            if (tIn < clos) {
                                vT = sp.Voiced ? 0.2 : 0.0;
                            } else if (tIn < clos + burst) {
                                nT = sp.NoiseGain * 1.5 * pf.BurstMul;
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
                    vT *= seg.Volume;
                    nT *= seg.Volume;
                }

                // ── tracto masculino y gender ──────────────────────────────
                double fScale = pf.Formant * Math.Pow(2.0, Math.Clamp(genderCurve / 100.0, -1.0, 1.0) * 0.3);
                tF1 *= fScale; tF2 *= fScale; tF3 *= fScale;

                // ── suavizado ──────────────────────────────────────────────
                double a = 1.0 - Math.Exp(-BlockMs / pf.GlideMs);
                f1 += (tF1 - f1) * a; f2 += (tF2 - f2) * a; f3 += (tF3 - f3) * a;
                voice += (vT - voice) * (1.0 - Math.Exp(-BlockMs / (vT < voice ? pf.ReleaseMs : pf.AttackMs)));
                noise += (nT - noise) * (1.0 - Math.Exp(-BlockMs / 2.0));
                double ah = 1.0 - Math.Exp(-BlockMs / 8.0);
                noiseHz += (tHz - noiseHz) * ah;
                noiseBw += (tBw - noiseBw) * ah;

                bool silent = voice < 1e-4 && noise < 1e-4;

                // ── coeficientes ───────────────────────────────────────────
                fr[0] = f1; fr[1] = f2; fr[2] = f3; fr[3] = 3600.0 * pf.Formant; fr[4] = 4600.0 * pf.Formant;
                for (int r = 0; r < Res; r++) {
                    bw[r] = bwBase[r] * pf.BwMul;
                    double fc = Math.Min(Math.Max(fr[r], 50.0), SampleRate * 0.45);
                    double c = -Math.Exp(-2.0 * Math.PI * bw[r] / SampleRate);
                    double bq = 2.0 * Math.Exp(-Math.PI * bw[r] / SampleRate) * Math.Cos(2.0 * Math.PI * fc / SampleRate);
                    ra[r] = 1.0 - bq - c; rb[r] = bq; rc[r] = c;
                }
                {
                    const double chestHz = 210.0, chestBw = 130.0;
                    cc = -Math.Exp(-2.0 * Math.PI * chestBw / SampleRate);
                    cb = 2.0 * Math.Exp(-Math.PI * chestBw / SampleRate) * Math.Cos(2.0 * Math.PI * chestHz / SampleRate);
                    ca = 1.0 - cb - cc;
                }
                double nfc = Math.Min(Math.Max(noiseHz, 200.0), SampleRate * 0.45);
                double q2 = Math.Max(nfc / Math.Max(noiseBw, 100.0), 0.3);
                double w0 = 2.0 * Math.PI * nfc / SampleRate;
                double al = Math.Sin(w0) / (2.0 * q2);
                double bb0 = al / (1.0 + al);
                double ba1 = -2.0 * Math.Cos(w0) / (1.0 + al);
                double ba2 = (1.0 - al) / (1.0 + al);

                // las vocales cerradas suenan más flojas con filtros de ganancia unidad en continua
                double comp = Math.Min(2.6, Math.Pow(800.0 / Math.Max(f1 / pf.Formant, 200.0), 1.1));
                double tp = Math.Clamp(pf.OpenQ - 0.07 * tension, 0.26, 0.5);
                const double tn = 0.16;
                double airAmt = Math.Clamp(breath + pf.Air, 0.0, 1.0);
                double gain = 1.0 / Math.Sqrt(pf.Drive);

                // inestabilidad mínima del tono: un poco robótico pero humano
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                double jit = 1.0 + pf.Jitter * (rng / 2147483648.0 - 1.0);

                double fa = F0At(phrase, pitches, t0, b0, pitchStart) * jit;
                double fb = F0At(phrase, pitches, t0, b0 + Block, pitchStart) * jit;

                if (silent) {
                    Array.Clear(ry1, 0, Res); Array.Clear(ry2, 0, Res);
                    cy1 = cy2 = 0;
                    bpx1 = bpx2 = bpy1 = bpy2 = 0;
                }
                int end = Math.Min(Block, total - b0);
                for (int k = 0; k < end; k++) {
                    rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                    double nz = rng / 2147483648.0 - 1.0;

                    double sample = 0.0;
                    if (!silent) {
                        double f0 = fa + (fb - fa) * k / Block;
                        phase += f0 / SampleRate;
                        if (phase >= 1.0) phase -= 1.0;
                        double g;
                        if (phase < tp) g = 0.5 * (1.0 - Math.Cos(Math.PI * phase / tp));
                        else if (phase < tp + tn) g = Math.Cos(0.5 * Math.PI * (phase - tp) / tn);
                        else g = 0.0;
                        double ex = (g - gPrev) * (SampleRate / f0) * 0.012;
                        gPrev = g;
                        ex = ex * (1.0 - 0.6 * airAmt) + nz * 0.15 * airAmt + nz * 0.008;

                        double xin = ex * voice * comp;

                        // cuerpo grave en paralelo
                        double yc = ca * xin + cb * cy1 + cc * cy2;
                        cy2 = cy1; cy1 = yc;

                        double x = xin;
                        for (int r = 0; r < Res; r++) {
                            double y = ra[r] * x + rb[r] * ry1[r] + rc[r] * ry2[r];
                            ry2[r] = ry1[r]; ry1[r] = y; x = y;
                        }
                        x += yc * pf.Chest * 0.9;

                        double xn = nz * 0.7;
                        double yb = bb0 * xn - bb0 * bpx2 - ba1 * bpy1 - ba2 * bpy2;
                        bpx2 = bpx1; bpx1 = xn; bpy2 = bpy1; bpy1 = yb;
                        sample = x + yb * noise * 1.6;
                    }
                    output[b0 + k] = (float)(Math.Tanh(sample * pf.Drive) * gain);
                }
            }
            return output;
        }

        /// <summary>Frecuencia fundamental (Hz) en la muestra sampleIndex de la frase.</summary>
        private static double F0At(RenderPhrase phrase, float[] pitches, double t0, int sampleIndex, int pitchStart) {
            if (pitches == null || pitches.Length == 0) return 130.81; // C3, registro grave
            double ms = t0 + sampleIndex * 1000.0 / SampleRate;
            double idx = (phrase.timeAxis.MsPosToNonExactTickPos(ms) - pitchStart) / 5.0;
            double cents = Curve(pitches, idx);
            double hz = 440.0 * Math.Pow(2.0, (cents / 100.0 - 69.0) / 12.0);
            return Math.Clamp(hz, 55.0, 1400.0);
        }
    }
}
