// OpenUtau.Core/Vocaloid/VocaloidRenderer.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// IRenderer de USingerType.Vocaloid4 (todas las generaciones V1..V6).
//
// Qué había antes y por qué no sonaba: el renderer trataba el .vvd como si fuera audio PCM y
// leía el .ddb con un formato inventado. El .vvd es un archivo de información pequeño y el
// .ddb/.ddi reales son binarios propios de VOCALOID, así que el resultado era siempre
// silencio. Además colocaba los fonemas uno tras otro en vez de en su posición.
//
// Ahora: cada fonema se coloca en su posición real y se sintetiza con FlameVoice (glotis +
// formantes + ruido), siguiendo la curva de pitch de la frase (vibrato y PITD incluidos),
// DYN, GENC, BREC y TENC. El audio sale aunque el banco no se pueda leer.
// La voz Flame es masculina (DefaultLoid). La expresión (cute, rapper, cold...) se elige con un
// flame-expression.txt junto al banco o con FlameVoice.Expression.
// Las muestras reales del .ddb necesitan un decodificador aparte (ver VOCALOID-SUPPORT.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    public class VocaloidRenderer : IRenderer {

        /// <summary>Cola de relajación (ms) detrás de la última nota.</summary>
        private const double kTailMs = 30.0;

        private static readonly HashSet<string> supportedExp = new HashSet<string> {
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.VEL,
            Format.Ustx.VOL,
            Format.Ustx.GENC,
            Format.Ustx.BREC,
            Format.Ustx.TENC,
        };

        public USingerType SingerType => USingerType.Vocaloid4;
        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor)
            => supportedExp.Contains(descriptor.abbr);

        public RenderResult Layout(RenderPhrase phrase) => new RenderResult {
            leadingMs         = phrase.leadingMs,
            positionMs        = phrase.positionMs,
            estimatedLengthMs = phrase.durationMs + phrase.leadingMs + kTailMs,
        };

        public Task<RenderResult> Render(
                RenderPhrase phrase,
                Progress progress,
                int trackNo,
                CancellationTokenSource cancellation,
                bool isPreRender = false,
                RenderPhraseEvents? renderEvents = null) {

            return Task.Run(() => {
                string info = $"VocaloidRenderer {phrase.singer.Name}";
                progress.Complete(0, info);

                float[] samples;
                try {
                    samples = FlameVoice.Synthesize(phrase, kTailMs, cancellation.Token,
                        FlameVoice.ResolveExpression(phrase.singer?.Location));
                } catch (Exception ex) {
                    Log.Error(ex, "[VocaloidRenderer] render failed");
                    samples = new float[Math.Max(1, (int)Math.Ceiling(
                        (phrase.durationMs + phrase.leadingMs + kTailMs) / 1000.0 * FlameVoice.SampleRate))];
                }

                var result = new RenderResult {
                    samples           = samples,
                    leadingMs         = phrase.leadingMs,
                    positionMs        = phrase.positionMs,
                    estimatedLengthMs = phrase.durationMs + phrase.leadingMs + kTailMs,
                };
                Renderers.ApplyDynamics(phrase, result);
                progress.Complete(phrase.phones.Length, info);
                return result;
            }, cancellation.Token);
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase)
            => new RenderPitchResult { ticks = Array.Empty<float>(), tones = Array.Empty<float>() };

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings rs)
            => Array.Empty<UExpressionDescriptor>();

        public override string ToString() => Renderers.VOCALOID4;
    }
}
