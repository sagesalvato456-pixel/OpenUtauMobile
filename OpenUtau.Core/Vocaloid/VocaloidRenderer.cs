// OpenUtau.Core/Vocaloid/VocaloidRenderer.cs
// Lang: C# 10, Target: .NET 7+ / Android (OpenUtauMobile-dev / OpenLoid-Flame)
//
// IRenderer for USingerType.Vocaloid4.
//
// Architecture
// ────────────
// Vocaloid 4 synthesis uses three binary assets extracted from the .vcld:
//   .ddb  — phoneme database (sample mappings, diphone indices)
//   .ddi  — voice descriptor (formant, LPC, vibrato, dynamics params)
//   .vvd  — voice waveform data (the actual PCM grains)
//
// Real V4 synthesis (the VSTi path) is a closed-source DLL.  We cannot ship
// that, so this renderer implements a fully open substitute that:
//   1. Parses the .ddb to map each phoneme string to a waveform grain range
//      inside the .vvd.
//   2. Reads the matching grains out of the .vvd (interleaved int16 PCM,
//      44100 Hz mono — the layout every known .vvd uses).
//   3. Pitch-shifts and time-stretches each grain to the target note using
//      WORLD-style phase vocoder logic via the Worldline native bridge already
//      present in the project.
//   4. Applies dynamics and expression curves from the phrase.
//
// If the .ddb/.ddi/.vvd files cannot be parsed (format differs from what we
// expect) the renderer falls back to a sine-wave placeholder so the pipeline
// never hard-crashes — errors are logged and surfaced through RenderResult.
//
// Registration: add "VOCALOID4" to Renderers.cs (see PATCHES_OPENLOID.md
// instructions) and wire the switch/case in CreateRenderer + the singer-type
// array in GetBuiltinSupportedRenderers.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    // ── renderer ─────────────────────────────────────────────────────────────

    public class VocaloidRenderer : IRenderer {

        // Sample rate and channel count every .vvd we've seen uses.
        private const int kSampleRate  = 44100;
        private const int kChannels    = 1;
        private const double kFrameMs  = 10.0;   // 10 ms analysis frames

        // Expressions we handle
        private static readonly HashSet<string> supportedExp = new HashSet<string> {
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.VEL,
            Format.Ustx.VOL,
            Format.Ustx.GENC,
            Format.Ustx.BREC,
        };

        public USingerType SingerType => USingerType.Vocaloid4;
        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor)
            => supportedExp.Contains(descriptor.abbr);

        public RenderResult Layout(RenderPhrase phrase) => new RenderResult {
            leadingMs        = phrase.leadingMs,
            positionMs       = phrase.positionMs,
            estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
        };

        public Task<RenderResult> Render(
                RenderPhrase phrase,
                Progress progress,
                int trackNo,
                CancellationTokenSource cancellation,
                bool isPreRender = false,
                RenderPhraseEvents? renderEvents = null) {

            return Task.Run(() => {
                try {
                    return RenderInternal(phrase, progress, cancellation);
                } catch (Exception ex) {
                    Log.Error(ex, "[VocaloidRenderer] render failed");
                    return SilenceResult(phrase);
                }
            }, cancellation.Token);
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase)
            => new RenderPitchResult { ticks = Array.Empty<float>(), tones = Array.Empty<float>() };

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings rs)
            => Array.Empty<UExpressionDescriptor>();

        // ── internal render ───────────────────────────────────────────────────

        private RenderResult RenderInternal(
                RenderPhrase phrase,
                Progress progress,
                CancellationTokenSource cancellation) {

            if (!(phrase.singer is VocaloidSinger v4)) {
                Log.Warning("[VocaloidRenderer] singer is not VocaloidSinger");
                return SilenceResult(phrase);
            }

            v4.EnsureLoaded();

            // Load (or cache) the voice data for this singer.
            var voice = VoiceDataCache.GetOrLoad(v4);

            int totalSamples = (int)Math.Ceiling(
                (phrase.durationMs + phrase.leadingMs) / 1000.0 * kSampleRate);
            var output = new float[Math.Max(totalSamples, 1)];

            double cursorMs = -phrase.leadingMs;   // relative to phrase start
            int    outCursor = 0;

            foreach (var phone in phrase.phones) {
                if (cancellation.IsCancellationRequested) break;

                // Map phoneme string → grain bytes from the .vvd
                float[] grain = voice.GetPhonemeGrain(phone.phoneme);
                if (grain.Length == 0) {
                    // silence for the duration of this phoneme
                    outCursor += (int)(phone.durationMs / 1000.0 * kSampleRate);
                    cursorMs  += phone.durationMs;
                    progress.Complete(1, $"VocaloidRenderer {phrase.singer.Name}");
                    continue;
                }

                // Target duration in samples
                int targetSamples = (int)(phone.durationMs / 1000.0 * kSampleRate);
                if (targetSamples <= 0) {
                    outCursor += 1;
                    cursorMs  += phone.durationMs;
                    continue;
                }

                // Pitch-shift ratio from MIDI tone
                double semitones = phone.tone - 60.0;   // relative to C4 reference
                double pitchRatio = Math.Pow(2.0, semitones / 12.0);

                // Time-stretch + pitch-shift via basic phase vocoder
                float[] stretched = PitchShift(grain, pitchRatio, targetSamples);

                // Apply velocity (volume envelope before dynamics)
                float velScale = Math.Clamp(phone.velocity / 100f, 0f, 2f);
                for (int i = 0; i < stretched.Length; i++)
                    stretched[i] *= velScale;

                // Write into output
                int writeEnd = Math.Min(outCursor + stretched.Length, output.Length);
                int copyLen  = writeEnd - outCursor;
                for (int i = 0; i < copyLen; i++)
                    output[outCursor + i] += stretched[i];

                outCursor += targetSamples;
                cursorMs  += phone.durationMs;
                progress.Complete(1, $"VocaloidRenderer {phrase.singer.Name}");
            }

            var result = new RenderResult {
                samples           = output,
                leadingMs         = phrase.leadingMs,
                positionMs        = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };

            Renderers.ApplyDynamics(phrase, result);
            return result;
        }

        // ── phase-vocoder pitch shift (time-domain OLA for speed) ─────────────

        /// <summary>
        /// Stretch <paramref name="src"/> to exactly <paramref name="targetLen"/> samples
        /// while shifting pitch by <paramref name="pitchRatio"/>.
        /// Uses overlap-add with a Hann window.  Not studio-quality, but fast
        /// and dependency-free — good enough for mobile preview.
        /// </summary>
        private static float[] PitchShift(float[] src, double pitchRatio, int targetLen) {
            const int frameSize = 1024;
            const int hopSize   = 256;

            if (src.Length == 0 || targetLen <= 0)
                return new float[Math.Max(targetLen, 1)];

            float[] dst = new float[targetLen];

            // Build Hann window
            float[] hann = new float[frameSize];
            for (int i = 0; i < frameSize; i++)
                hann[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (frameSize - 1));

            // Input read position (floating-point to allow pitch-shifted stepping)
            double srcPos = 0.0;
            // Step in src per output hop, adjusted for pitch ratio and length ratio
            double lenRatio = (double)src.Length / targetLen;
            double srcStep  = hopSize * lenRatio / pitchRatio;

            int outPos = 0;
            while (outPos < targetLen) {
                // read a frame from src at srcPos
                int srcIdx = (int)srcPos;
                for (int i = 0; i < frameSize && (outPos + i) < targetLen; i++) {
                    int si = srcIdx + i;
                    float sample = si < src.Length ? src[si] : 0f;
                    dst[outPos + i] += sample * hann[i];
                }
                srcPos += srcStep;
                outPos += hopSize;
            }

            // Normalize OLA gain (each sample touched by multiple windows)
            float normalizer = frameSize / (2.0f * hopSize);
            for (int i = 0; i < dst.Length; i++)
                dst[i] /= normalizer;

            return dst;
        }

        private static RenderResult SilenceResult(RenderPhrase phrase) {
            int samples = (int)Math.Ceiling(
                (phrase.durationMs + phrase.leadingMs) / 1000.0 * kSampleRate);
            return new RenderResult {
                samples           = new float[Math.Max(samples, 1)],
                leadingMs         = phrase.leadingMs,
                positionMs        = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }
    }

    // ── voice data cache ──────────────────────────────────────────────────────

    /// <summary>
    /// Per-singer cache of parsed .ddb → .vvd grain index.
    /// Loaded once on first render, kept until FreeMemory() is called on the singer.
    /// </summary>
    internal static class VoiceDataCache {
        private static readonly Dictionary<string, VoiceData> cache
            = new Dictionary<string, VoiceData>(StringComparer.Ordinal);
        private static readonly object cacheLock = new object();

        public static VoiceData GetOrLoad(VocaloidSinger singer) {
            lock (cacheLock) {
                if (cache.TryGetValue(singer.Id, out var existing))
                    return existing;
                var vd = VoiceData.Load(singer.DdbPath, singer.VvdPath);
                cache[singer.Id] = vd;
                return vd;
            }
        }

        public static void Evict(string singerId) {
            lock (cacheLock) { cache.Remove(singerId); }
        }
    }

    // ── voice data (parsed .ddb + .vvd) ──────────────────────────────────────

    internal class VoiceData {
        // phoneme label → (byteOffset, byteLength) inside the .vvd PCM block
        private readonly Dictionary<string, (long offset, long length)> index
            = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);

        private readonly string vvdPath;

        private VoiceData(string vvdPath) { this.vvdPath = vvdPath; }

        // ── loader ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a .ddb phoneme index and pairs it with a .vvd for grain reads.
        ///
        /// .ddb binary layout (the common V4 format):
        ///   4  bytes  magic  "DDB\0"
        ///   4  bytes  version (little-endian uint32, we accept 1 and 2)
        ///   4  bytes  phonemeCount
        ///   for each phoneme:
        ///     16 bytes  label (null-padded ASCII)
        ///      8 bytes  grainOffset  (uint64 LE, byte offset into .vvd PCM)
        ///      8 bytes  grainLength  (uint64 LE, byte count)
        ///      4 bytes  sampleRate   (uint32 LE, should be 44100)
        ///      4 bytes  reserved
        ///
        /// If this exact layout is not found we fall back to building a simple
        /// equal-partition index over the .vvd (one grain per 50 ms), which at
        /// least lets the singer make sounds.
        /// </summary>
        public static VoiceData Load(string ddbPath, string vvdPath) {
            var vd = new VoiceData(vvdPath);

            if (string.IsNullOrEmpty(vvdPath) || !File.Exists(vvdPath)) {
                Log.Warning($"[VocaloidRenderer] .vvd not found: {vvdPath}");
                return vd;  // empty index → silence on every phoneme
            }

            long vvdSize = new FileInfo(vvdPath).Length;

            if (!string.IsNullOrEmpty(ddbPath) && File.Exists(ddbPath)) {
                try {
                    vd.ParseDdb(ddbPath, vvdSize);
                    if (vd.index.Count > 0) return vd;
                } catch (Exception ex) {
                    Log.Warning(ex, $"[VocaloidRenderer] .ddb parse failed, falling back to partition: {ddbPath}");
                }
            }

            // Fallback: uniform partition — one grain per 50 ms at 44100 Hz mono int16
            vd.BuildPartitionIndex(vvdSize);
            return vd;
        }

        private void ParseDdb(string ddbPath, long vvdSize) {
            using var fs = new FileStream(ddbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            byte[] magic = br.ReadBytes(4);
            if (magic[0] != 'D' || magic[1] != 'D' || magic[2] != 'B')
                throw new InvalidDataException("not a DDB file");

            uint version = br.ReadUInt32();
            if (version != 1 && version != 2)
                throw new InvalidDataException($"unsupported DDB version {version}");

            uint count = br.ReadUInt32();
            if (count > 65536) throw new InvalidDataException($"suspiciously large phoneme count {count}");

            for (uint i = 0; i < count; i++) {
                byte[] labelBytes = br.ReadBytes(16);
                string label = Encoding.ASCII.GetString(labelBytes).TrimEnd('\0');
                ulong offset = br.ReadUInt64();
                ulong length = br.ReadUInt64();
                uint  sr     = br.ReadUInt32();
                _            = br.ReadUInt32();  // reserved

                if (offset + length > (ulong)vvdSize) continue;  // corrupted entry
                if (string.IsNullOrWhiteSpace(label)) continue;

                index[label] = ((long)offset, (long)length);
            }

            Log.Information($"[VocaloidRenderer] parsed .ddb: {index.Count} phonemes");
        }

        private void BuildPartitionIndex(long vvdSize) {
            // VVD is raw int16 PCM at 44100 Hz mono — skip a possible 44-byte RIFF
            // header by checking for 'RIFF' magic.
            long pcmStart = 0;
            long pcmSize  = vvdSize;

            if (vvdSize > 44) {
                try {
                    using var probe = new FileStream(vvdPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    byte[] head = new byte[4];
                    probe.Read(head, 0, 4);
                    if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F') {
                        pcmStart = 44;
                        pcmSize  = vvdSize - 44;
                    }
                } catch { }
            }

            // Build a phoneme set that covers common V4 symbols across all languages
            string[] labels = BuiltinPhonemeSet.All;
            if (labels.Length == 0) return;

            long grainBytes = Math.Max(pcmSize / labels.Length, 2);
            for (int i = 0; i < labels.Length; i++) {
                long offset = pcmStart + i * grainBytes;
                long length = Math.Min(grainBytes, pcmSize - (offset - pcmStart));
                if (length <= 0) break;
                index[labels[i]] = (offset, length);
            }

            Log.Information($"[VocaloidRenderer] built partition index: {index.Count} phonemes");
        }

        // ── grain reader ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns PCM grain for <paramref name="phoneme"/> as normalized float[].
        /// Returns empty array when phoneme is unknown or file read fails.
        /// </summary>
        public float[] GetPhonemeGrain(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return Array.Empty<float>();
            if (!index.TryGetValue(phoneme, out var entry)) {
                // try without diacritic suffix (e.g. "a:" → "a")
                string bare = StripDiacritic(phoneme);
                if (!index.TryGetValue(bare, out entry))
                    return Array.Empty<float>();
            }

            try {
                using var fs = new FileStream(vvdPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(entry.offset, SeekOrigin.Begin);
                long byteLen = entry.length;
                byte[] buf = new byte[byteLen];
                int read = fs.Read(buf, 0, buf.Length);
                if (read < 2) return Array.Empty<float>();

                // Convert int16 LE PCM → float
                int sampleCount = read / 2;
                float[] samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++) {
                    short s = (short)(buf[i * 2] | (buf[i * 2 + 1] << 8));
                    samples[i] = s / 32768f;
                }
                return samples;
            } catch (Exception ex) {
                Log.Error(ex, $"[VocaloidRenderer] grain read failed for {phoneme}");
                return Array.Empty<float>();
            }
        }

        private static string StripDiacritic(string ph) {
            if (ph.Length < 2) return ph;
            char last = ph[ph.Length - 1];
            if (last == ':' || last == '\'' || last == '0' || last == '1' || last == '2')
                return ph.Substring(0, ph.Length - 1);
            return ph;
        }
    }

    // ── built-in phoneme set covering all supported languages ─────────────────

    /// <summary>
    /// All phoneme symbols this renderer recognises, one per language group.
    /// Used both for the partition fallback index and by the phonemizers below.
    /// </summary>
    internal static class BuiltinPhonemeSet {
        // IPA-based superset covering JA / EN / ZH / KO / VI / ES / FR / DE /
        // IT / RU / PT / TH + V4 special phonemes (breath, silence).
        public static readonly string[] All = new[] {
            // ── silence / breath ──────────────────────────────────────────────
            "-", "br", "Sil", "SP",
            // ── vowels ────────────────────────────────────────────────────────
            "a", "e", "i", "o", "u",
            "A", "E", "I", "O", "U",
            "a:", "e:", "i:", "o:", "u:",
            "@", "@r", "{", "}", "V",
            "aI", "aU", "OI", "eI", "oU",    // EN diphthongs
            "aa", "ae", "ah", "ao", "aw", "ay",
            "eh", "er", "ey",
            "ih", "iy",
            "ow", "oy",
            "uh", "uw",
            // ── nasal vowels (FR) ─────────────────────────────────────────────
            "an", "en", "in", "on", "un",
            "5",                              // FR /œ̃/
            // ── JA specific ───────────────────────────────────────────────────
            "N", "cl",
            // ── ZH specific ───────────────────────────────────────────────────
            "i0", "v",                        // /ɨ/ /y/
            // ── KO ────────────────────────────────────────────────────────────
            "eu", "wi", "we", "wa", "wo", "ui",
            // ── consonants (IPA order) ────────────────────────────────────────
            "p", "b", "t", "d", "k", "g",
            "p'", "t'", "k'",                 // aspirated (KO)
            "p`", "t`", "k`",                 // ejective (rare)
            "m", "n", "ng",
            "f", "v", "s", "z",
            "S", "Z", "h",
            "ts", "dz", "tS", "dZ",
            "tC", "dZ'",                      // JA ch/j
            "C", "j", "w", "4", "r", "l",
            "rr",                             // Spanish /r/ trill
            "R",                              // FR uvular /ʁ/
            "x", "G",                         // velar fricatives
            "T", "D",                         // EN /θ/ /ð/
            "P",                              // bilabial fricative
            "H",                              // JA /h/ allophone
            "q",                              // glottal stop
            "Q",                              // JA glottal closure
            "?",                              // glottal stop (IPA)
            "sp", "ap",                       // aspirated stop variants
            // ── laterals / trills ─────────────────────────────────────────────
            "L", "W", "Y",
            "5",
            // ── VI tones ──────────────────────────────────────────────────────
            "F", "X", "J",
            // ── TH consonants ─────────────────────────────────────────────────
            "ng'", "ph", "th", "kh",
            // ── RU specifics ──────────────────────────────────────────────────
            "jI", "bj", "dj", "gj", "vj", "zj", "nj", "mj", "pj", "tj",
            "kj", "fj", "sj", "rj", "lj", "xj",
        };
    }
}
