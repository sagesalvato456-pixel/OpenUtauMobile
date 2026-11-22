// OpenUtau.Core/Vocaloid/AIBotSinger.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Cantante "AIBot" PREINSTALADO: vive dentro de la app, no necesita .vcld ni carpeta con
// .ddb/.ddi. La voz sale de AIBotVoice (sintetizador Flame), así que no depende de ningún
// archivo del disco. Aparece siempre primero en la lista de cantantes.
//
// ── Cómo conectarlo (2 líneas) ───────────────────────────────────────────────────────────
// 1) VocaloidSingerLoader.FindAllSingers(), justo antes de "return singers;":
//        AIBotSinger.AddTo(singers);
// 2) VocaloidRenderer.Render(), en lugar de FlameVoice.Synthesize(...):
//        samples = AIBotVoice.Matches(phrase.singer.Name)
//            ? AIBotVoice.Synthesize(phrase, kTailMs, cancellation.Token)
//            : FlameVoice.Synthesize(phrase, kTailMs, cancellation.Token);
//
// Necesita AIBotVoice.cs en el mismo namespace.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    public class AIBotSinger : USinger {

        public const string SingerId = "AIBot";

        /// <summary>Fonemizador por defecto de AIBot (cambia a VocaloidSpanishPhonemizer para español).</summary>
        private const string kPhonemizer = "OpenUtau.Core.Vocaloid.VocaloidEnglishPhonemizer";

        private readonly Dictionary<string, string> _localizedNames = new Dictionary<string, string>();
        private readonly List<string> _searchTerms = new List<string> { "aibot", "ai bot", "flame", "male", "hard", "default" };
        private readonly List<string> _errors = new List<string>();
        private readonly Dictionary<string, UOto> _otoCache = new Dictionary<string, UOto>(StringComparer.Ordinal);
        private readonly string _basePath;
        private byte[]? _avatar;

        // ── identidad ─────────────────────────────────────────────────────────

        public override string Id => SingerId;
        public override string Name => SingerId;
        public override Dictionary<string, string> LocalizedNames => _localizedNames;
        public override IList<string> SearchTerms => _searchTerms;
        public override USingerType SingerType => USingerType.Vocaloid4;
        public override string BasePath => _basePath;
        public override string Author => "OpenLoid-Flame";
        public override string Voice => "AIBot (male, 17, deep / hard / direct, sometimes sweet)";
        public override string Location => "(preinstalled)";
        public override string Web => string.Empty;
        public override string Version => "1.0";
        public override string OtherInfo =>
            "Preinstalled Flame voice. Tension (TENC) up = harder, down = sweeter. Breathiness (BREC) adds air.";
        public override IList<string> Errors => _errors;

        // ── avatar generado en código (sin archivos) ──────────────────────────

        public override string Avatar => string.Empty;
        public override byte[] AvatarData => _avatar ??= BuildAvatar();
        public override string Portrait => string.Empty;
        public override float PortraitOpacity => 0.67f;
        public override int PortraitHeight => 0;

        public override string DefaultPhonemizer => kPhonemizer;
        public override string Sample => string.Empty;
        public override Encoding TextFileEncoding => Encoding.UTF8;

        // ── sin oto.ini: el fonema vale si el catálogo lo conoce ──────────────

        public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
        public override IList<UOto> Otos => emptyOtos;

        public override bool TryGetOto(string phoneme, out UOto oto) {
            oto = default!;
            if (!VocaloidPhonemeSet.Contains(phoneme)) return false;
            lock (_otoCache) {
                if (!_otoCache.TryGetValue(phoneme, out UOto? cached)) {
                    cached = UOto.OfDummy(phoneme);
                    _otoCache[phoneme] = cached;
                }
                oto = cached;
            }
            return true;
        }

        public override Dictionary<string, UOto> GetSuggestions(string text, bool isAlias) {
            var result = new Dictionary<string, UOto>();
            foreach (string symbol in VocaloidPhonemeSet.AllSymbols) {
                if (!string.IsNullOrEmpty(text) && symbol.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (TryGetOto(symbol, out UOto oto)) result[symbol] = oto;
                if (result.Count >= 40) break;
            }
            return result;
        }

        // ── ciclo de vida: no hay nada que cargar ─────────────────────────────

        public AIBotSinger() {
            string root = string.Empty;
            try {
                root = PathManager.Inst.SingersPaths.FirstOrDefault() ?? string.Empty;
            } catch {
                // PathManager aún no inicializado: BasePath vacío no rompe nada
            }
            _basePath = root;
            found = true;
            loaded = true;
        }

        public override void EnsureLoaded() { loaded = true; }
        public override void Reload() { loaded = true; }
        public override void FreeMemory() { _avatar = null; }

        /// <summary>Agrega AIBot al principio de la lista, salvo que ya haya un cantante con ese nombre.</summary>
        public static void AddTo(List<USinger> singers) {
            if (singers.Any(s => string.Equals(s.Name, SingerId, StringComparison.OrdinalIgnoreCase))) return;
            singers.Insert(0, new AIBotSinger());
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Avatar: cara de bot (fondo oscuro, ojos cian, boca roja) como PNG 128x128
        // ══════════════════════════════════════════════════════════════════════

        private static byte[] BuildAvatar() {
            const int W = 128, H = 128;
            var raw = new byte[H * (1 + W * 3)];
            int pos = 0;
            for (int y = 0; y < H; y++) {
                raw[pos++] = 0; // filtro "none"
                for (int x = 0; x < W; x++) {
                    byte r = 24, g = 26, b = 34;
                    if (x < 4 || x >= W - 4 || y < 4 || y >= H - 4) { r = 60; g = 64; b = 78; }
                    else if (y >= 40 && y < 66 && ((x >= 26 && x < 54) || (x >= 74 && x < 102))) { r = 70; g = 220; b = 255; }
                    else if (y >= 88 && y < 98 && x >= 36 && x < 92) { r = 235; g = 70; b = 70; }
                    raw[pos++] = r; raw[pos++] = g; raw[pos++] = b;
                }
            }

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            var ihdr = new byte[13];
            WriteBE(ihdr, 0, W); WriteBE(ihdr, 4, H);
            ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0; // 8 bits, RGB
            WriteChunk(png, "IHDR", ihdr);

            // zlib con un solo bloque "stored" (raw cabe en 65535 bytes)
            using var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x01);
            z.WriteByte(0x01);
            z.WriteByte((byte)(raw.Length & 0xFF)); z.WriteByte((byte)(raw.Length >> 8));
            z.WriteByte((byte)(~raw.Length & 0xFF)); z.WriteByte((byte)((~raw.Length >> 8) & 0xFF));
            z.Write(raw, 0, raw.Length);
            var adler = new byte[4];
            WriteBE(adler, 0, unchecked((int)Adler32(raw)));
            z.Write(adler, 0, 4);
            WriteChunk(png, "IDAT", z.ToArray());

            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void WriteBE(byte[] buf, int offset, int value) {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static void WriteChunk(Stream s, string type, byte[] data) {
            var head = new byte[4];
            WriteBE(head, 0, data.Length);
            s.Write(head, 0, 4);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFFu;
            crc = Crc(crc, typeBytes);
            crc = Crc(crc, data);
            var tail = new byte[4];
            WriteBE(tail, 0, unchecked((int)(crc ^ 0xFFFFFFFFu)));
            s.Write(tail, 0, 4);
        }

        private static uint[]? _crcTable;

        private static uint Crc(uint crc, byte[] data) {
            if (_crcTable == null) {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++) {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                _crcTable = t;
            }
            for (int i = 0; i < data.Length; i++) crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint Adler32(byte[] data) {
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; i++) {
                a = (a + data[i]) % 65521u;
                b = (b + a) % 65521u;
            }
            return (b << 16) | a;
        }
    }
}
