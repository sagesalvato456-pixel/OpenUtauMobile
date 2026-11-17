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

using System;
using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.Vocaloid {

    /// <summary>Carácter de una voz Flame: tamaño de formantes, brillo, soplo y micro-variaciones.</summary>
    internal sealed class FlameVoiceProfile {
        /// <summary>Escala de formantes: mayor que 1 suena más joven / aguda.</summary>
        public float FormantScale = 1.0f;
        /// <summary>Desplaza la fase abierta de la glotis (negativo = más brillante).</summary>
        public float OpenQuotient = 0f;
        public float BaseBreath = 0f;
        /// <summary>Variación lenta de f0 (fracción); da naturalidad.</summary>
        public float Jitter = 0.002f;
        /// <summary>Variación lenta de amplitud (fracción).</summary>
        public float Shimmer = 0.03f;
        public float LowpassHz = 9500f;

        public static readonly FlameVoiceProfile Generic = new FlameVoiceProfile();

        public static readonly FlameVoiceProfile DefaultLoid = new FlameVoiceProfile {
            FormantScale = 1.07f,
            OpenQuotient = -0.02f,
            BaseBreath = 0.06f,
            Jitter = 0.0025f,
            Shimmer = 0.035f,
            LowpassHz = 9000f,
        };
    }

    internal static class FlameVoice {

        public const int SampleRate = 44100;
        private const int Block = 64;
        private const double BlockMs = Block * 1000.0 / SampleRate;

        private sealed class Seg {
            public PhonemeSpec Spec = null!;
            public double StartMs;
            public double EndMs;
            public float Volume = 1f;
            public int NextSonorant = -1;
            public string Symbol = string.Empty;
            public bool UseSampler;
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
        public static float[] Synthesize(RenderPhrase phrase, double tailMs, CancellationToken token, FlameVoiceProfile? profile = null, VocaloidSampleBank? bank = null, VocaloidArticulationBank? art = null) {
            profile ??= FlameVoiceProfile.Generic;
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
                segs.Add(new Seg { Spec = spec, Symbol = phone.phoneme, StartMs = s, EndMs = e, Volume = Math.Clamp(phone.volume, 0f, 2f) });
            }
            for (int i = 0; i < segs.Count; i++) {
                for (int j = i + 1; j < segs.Count; j++) {
                    if (segs[j].Spec.IsSonorant) { segs[i].NextSonorant = j; break; }
                }
            }

            int pitchStart = phrase.position - phrase.leading;
            float[] pitches = phrase.pitches;

            // Vocales y nasales con muestras reales del banco (PSOLA); lo demás lo hace Flame.
            float[]? samplerMix = null;
            if (bank != null) {
                foreach (Seg sg in segs) {
                    if (sg.Spec.Class != PhonemeClass.Vowel && sg.Spec.Class != PhonemeClass.Nasal) continue;
                    int segStart = (int)Math.Round(sg.StartMs * SampleRate / 1000.0);
                    int segLen = (int)Math.Round((sg.EndMs - sg.StartMs) * SampleRate / 1000.0);
                    if (segLen < 200 || segStart >= total) continue;
                    float[]? piece = bank.Render(sg.Symbol, segLen,
                        x => F0At(phrase, pitches, t0, segStart + x, pitchStart));
                    if (piece == null) continue;
                    samplerMix ??= new float[total];
                    for (int i = 0; i < piece.Length; i++) {
                        int di = segStart + i;
                        if (di >= 0 && di < total) samplerMix[di] += piece[i] * sg.Volume;
                    }
                    sg.UseSampler = true;
                }
            }

            // Consonantes (oclusivas, africadas y fricativas) con las articulaciones reales del banco.
            // Si la consonante desemboca en una vocal (silaba CV) se alinea su final con la vocal;
            // si no, es una consonante final y se alinea su principio con el segmento.
            if (art != null) {
                for (int si = 0; si < segs.Count; si++) {
                    Seg sg = segs[si];
                    PhonemeClass cls = sg.Spec.Class;
                    if (cls != PhonemeClass.Plosive && cls != PhonemeClass.Affricate && cls != PhonemeClass.Fricative) continue;
                    int segStart = (int)Math.Round(sg.StartMs * SampleRate / 1000.0);
                    int segEnd = (int)Math.Round(sg.EndMs * SampleRate / 1000.0);
                    int slot = segEnd - segStart;
                    if (slot < 200 || segStart >= total) continue;

                    int nx = sg.NextSonorant;
                    bool onset = nx >= 0 && segs[nx].Spec.Class == PhonemeClass.Vowel
                        && segs[nx].StartMs - sg.EndMs < 30.0;

                    float[]? piece;
                    int dest;
                    if (onset) {
                        string ctx = VocaloidArticulationBank.VowelBase(segs[nx].Symbol);
                        double tf0 = F0At(phrase, pitches, t0, segEnd, pitchStart);
                        piece = art.Render(sg.Symbol, ctx, tf0, sg.Spec.Voiced, slot + VocaloidArticulationBank.OverlapSamples, true);
                        if (piece == null) continue;
                        dest = segEnd + VocaloidArticulationBank.OverlapSamples - piece.Length;
                    } else {
                        string ctx = "a";
                        for (int pj = si - 1; pj >= 0; pj--) {
                            if (segs[pj].Spec.Class == PhonemeClass.Vowel) { ctx = VocaloidArticulationBank.VowelBase(segs[pj].Symbol); break; }
                        }
                        double tf0 = F0At(phrase, pitches, t0, segStart, pitchStart);
                        piece = art.Render(sg.Symbol, ctx, tf0, sg.Spec.Voiced, Math.Max(slot, 3100), false);
                        if (piece == null) continue;
                        dest = segStart;
                    }
                    samplerMix ??= new float[total];
                    for (int i = 0; i < piece.Length; i++) {
                        int di = dest + i;
                        if (di >= 0 && di < total) samplerMix[di] += piece[i] * sg.Volume;
                    }
                    sg.UseSampler = true;
                }
            }

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
            double jit = 0, shim = 0, nasal = 0, lp = 0;
            double nx1 = 0, nx2 = 0, ny1 = 0, ny2 = 0;
            double lpA = 1.0 - Math.Exp(-2.0 * Math.PI * profile.LowpassHz / SampleRate);

            for (int b0 = 0; b0 < total; b0 += Block) {
                if ((b0 / Block) % 256 == 0 && token.IsCancellationRequested) break;

                double tms = (b0 + Block / 2) * 1000.0 / SampleRate;
                while (segIdx < segs.Count && segs[segIdx].EndMs <= tms) segIdx++;
                Seg? seg = null;
                // el solape de fonemas es raro; se busca desde segIdx hacia delante
                for (int k = segIdx; k < segs.Count && segs[k].StartMs <= tms; k++) {
                    if (segs[k].EndMs > tms) { seg = segs[k]; }
                }

                double vT = 0, nT = 0, nasT = 0;
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
                            nasT = 1.0;
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
                    vT *= seg.Volume;
                    nT *= seg.Volume;
                    if (seg.UseSampler) { vT = 0; nT = 0; }   // el audio lo pone el banco
                }

                // ── curvas de la frase (índices cada 5 ticks) ──────────────
                double absMs = t0 + b0 * 1000.0 / SampleRate;
                double idx = (phrase.timeAxis.MsPosToNonExactTickPos(absMs) - pitchStart) / 5.0;
                double genderCurve = Curve(phrase.gender, idx);
                double breath = Math.Clamp(Curve(phrase.breathiness, idx) / 100.0 + profile.BaseBreath, 0.0, 1.0);
                double tension = Math.Clamp(Curve(phrase.tension, idx) / 100.0, -1.0, 1.0);
                double fScale = Math.Pow(2.0, Math.Clamp(genderCurve / 100.0, -1.0, 1.0) * 0.3);
                fScale *= profile.FormantScale;
                tF1 *= fScale; tF2 *= fScale; tF3 *= fScale;

                // ── suavizado ──────────────────────────────────────────────
                double a = 1.0 - Math.Exp(-BlockMs / 15.0);
                f1 += (tF1 - f1) * a; f2 += (tF2 - f2) * a; f3 += (tF3 - f3) * a;
                voice += (vT - voice) * (1.0 - Math.Exp(-BlockMs / (vT < voice ? 12.0 : 4.0)));
                noise += (nT - noise) * (1.0 - Math.Exp(-BlockMs / 2.5));
                nasal += (nasT - nasal) * (1.0 - Math.Exp(-BlockMs / 10.0));
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                jit += ((rng / 2147483648.0 - 1.0) - jit) * 0.12;
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                shim += ((rng / 2147483648.0 - 1.0) - shim) * 0.05;
                double ah = 1.0 - Math.Exp(-BlockMs / 8.0);
                noiseHz += (tHz - noiseHz) * ah;
                noiseBw += (tBw - noiseBw) * ah;

                bool silent = voice < 1e-4 && noise < 1e-4;

                // ── coeficientes ───────────────────────────────────────────
                fr[0] = f1; fr[1] = f2; fr[2] = f3; fr[3] = 3600.0; fr[4] = 4600.0;
                for (int r = 0; r < 5; r++) {
                    double fc = Math.Min(Math.Max(fr[r], 50.0), SampleRate * 0.45);
                    double c = -Math.Exp(-2.0 * Math.PI * bw[r] / SampleRate);
                    double bq = 2.0 * Math.Exp(-Math.PI * bw[r] / SampleRate) * Math.Cos(2.0 * Math.PI * fc / SampleRate);
                    ra[r] = 1.0 - bq - c; rb[r] = bq; rc[r] = c;
                }
                double nfc = Math.Min(Math.Max(noiseHz, 200.0), SampleRate * 0.45);
                double q2 = Math.Max(nfc / Math.Max(noiseBw, 100.0), 0.3);
                double w0 = 2.0 * Math.PI * nfc / SampleRate;
                double al = Math.Sin(w0) / (2.0 * q2);
                double bb0 = al / (1.0 + al);
                double ba1 = -2.0 * Math.Cos(w0) / (1.0 + al);
                double ba2 = (1.0 - al) / (1.0 + al);

                // vocales cerradas suenan más flojas con filtros de ganancia unidad en continua
                double comp = Math.Min(2.6, Math.Pow(800.0 / Math.Max(f1, 200.0), 1.1));
                double tp = Math.Clamp(0.40 + profile.OpenQuotient - 0.08 * tension, 0.28, 0.5);

                // antirresonancia de las nasales (muesca alrededor de 1 kHz)
                double nw = 2.0 * Math.PI * 1000.0 / SampleRate;
                double nal = Math.Sin(nw) / 3.0;
                double nb0 = 1.0 / (1.0 + nal);
                double nb1 = -2.0 * Math.Cos(nw) / (1.0 + nal);
                double na2 = (1.0 - nal) / (1.0 + nal);
                const double tn = 0.16;

                double fa = F0At(phrase, pitches, t0, b0, pitchStart) * (1.0 + profile.Jitter * jit * 3.0);
                double fb = F0At(phrase, pitches, t0, b0 + Block, pitchStart) * (1.0 + profile.Jitter * jit * 3.0);
                double shimmer = 1.0 + profile.Shimmer * shim;

                if (silent) {
                    Array.Clear(ry1, 0, 5); Array.Clear(ry2, 0, 5);
                    bpx1 = bpx2 = bpy1 = bpy2 = 0;
                    nx1 = nx2 = ny1 = ny2 = 0;
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
                        ex = ex * (1.0 - 0.6 * breath) + nz * 0.15 * breath + nz * 0.01;

                        double x = ex * voice * comp * shimmer;
                        for (int r = 0; r < 5; r++) {
                            double y = ra[r] * x + rb[r] * ry1[r] + rc[r] * ry2[r];
                            ry2[r] = ry1[r]; ry1[r] = y; x = y;
                        }
                        if (nasal > 1e-3) {
                            double yn = nb0 * x + nb1 * nx1 + nb0 * nx2 - nb1 * ny1 - na2 * ny2;
                            nx2 = nx1; nx1 = x; ny2 = ny1; ny1 = yn;
                            x = x * (1.0 - nasal) + yn * nasal;
                        }
                        double xn = nz * 0.7;
                        double yb = bb0 * xn - bb0 * bpx2 - ba1 * bpy1 - ba2 * bpy2;
                        bpx2 = bpx1; bpx1 = xn; bpy2 = bpy1; bpy1 = yb;
                        sample = x + yb * noise * 1.6;
                    }
                    lp += (sample - lp) * lpA;
                    double mixed = lp;
                    if (samplerMix != null) mixed += samplerMix[b0 + k];
                    output[b0 + k] = (float)Math.Tanh(mixed);
                }
            }
            return output;
        }

        /// <summary>Frecuencia fundamental (Hz) en la muestra sampleIndex de la frase.</summary>
        private static double F0At(RenderPhrase phrase, float[] pitches, double t0, int sampleIndex, int pitchStart) {
            if (pitches == null || pitches.Length == 0) return 261.63;
            double ms = t0 + sampleIndex * 1000.0 / SampleRate;
            double idx = (phrase.timeAxis.MsPosToNonExactTickPos(ms) - pitchStart) / 5.0;
            double cents = Curve(pitches, idx);
            double hz = 440.0 * Math.Pow(2.0, (cents / 100.0 - 69.0) / 12.0);
            return Math.Clamp(hz, 55.0, 1400.0);
        }
    }
}
