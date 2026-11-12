// OpenUtau.Core/Vocaloid/VocaloidPackage.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// Parses a .vcld package (zip) and its package-config.json manifest.
// Binary assets: .ddb (phoneme DB), .ddi (voice descriptor), .vvd (voice waveform data).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    // ── package-config.json schema ───────────────────────────────────────────

    public class VocaloidPackageConfig {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        // localized names map, e.g. { "ja-JP": "初音ミク" }
        [JsonPropertyName("localizedNames")]
        public Dictionary<string, string>? LocalizedNames { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("voice")]
        public string Voice { get; set; } = string.Empty;

        [JsonPropertyName("web")]
        public string Web { get; set; } = string.Empty;

        [JsonPropertyName("sample")]
        public string Sample { get; set; } = string.Empty;

        // relative path inside the .vcld zip to the avatar image
        [JsonPropertyName("image")]
        public string Image { get; set; } = "profile.png";

        // relative path inside the .vcld zip to the portrait/background image
        [JsonPropertyName("portrait")]
        public string Portrait { get; set; } = "background.png";

        [JsonPropertyName("portraitOpacity")]
        public float PortraitOpacity { get; set; } = 0.67f;

        // .ddb file path inside zip (phoneme database)
        [JsonPropertyName("ddb")]
        public string Ddb { get; set; } = string.Empty;

        // .ddi file path inside zip (voice descriptor index)
        [JsonPropertyName("ddi")]
        public string Ddi { get; set; } = string.Empty;
        // .vvd file path inside zip (voice waveform data)
        [JsonPropertyName("vvd")]
        public string Vvd { get; set; } = string.Empty;

        // phonemizer type override, same convention as VoicebankConfig.DefaultPhonemizer
        [JsonPropertyName("defaultPhonemizer")]
        public string DefaultPhonemizer { get; set; } = string.Empty;

        // search keywords
        [JsonPropertyName("searchTerms")]
        public List<string>? SearchTerms { get; set; }
    }

    // ── extracted / resolved package data ────────────────────────────────────

    /// <summary>
    /// All data extracted from a .vcld archive. Extracted to a temp folder
    /// under PathManager.Inst.CachePath on first load; avatar/portrait bytes
    /// kept in memory after that.
    /// </summary>
    public class VocaloidPackageData {
        public VocaloidPackageConfig Config { get; set; } = new();

        // Absolute paths to the extracted binary assets
        public string DdbPath  { get; set; } = string.Empty;
        public string DdiPath  { get; set; } = string.Empty;
        public string VvdPath  { get; set; } = string.Empty;

        public byte[]? AvatarBytes   { get; set; }
        public byte[]? PortraitBytes { get; set; }

        public List<string> Errors { get; } = new();
    }

    // ── loader ────────────────────────────────────────────────────────────────

    public static class VocaloidPackageLoader {
        private static readonly JsonSerializerOptions _jsonOpts = new() {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            AllowTrailingCommas         = true,
        };

        /// <summary>
        /// Opens a .vcld (zip) archive, reads package-config.json, extracts
        /// .ddb/.ddi/.vvd to <paramref name="extractRoot"/> and returns the
        /// fully populated <see cref="VocaloidPackageData"/>.
        /// </summary>
        public static VocaloidPackageData Load(string vcldPath, string extractRoot) {
            var pkg = new VocaloidPackageData();

            if (!File.Exists(vcldPath)) {
                pkg.Errors.Add($"[vcld] file not found: {vcldPath}");
                return pkg;
            }

            try {
                using var zip = ZipFile.OpenRead(vcldPath);

                // ── 1. parse package-config.json ──────────────────────────
                var configEntry = zip.GetEntry("package-config.json");
                if (configEntry == null) {
                    pkg.Errors.Add($"[vcld] missing package-config.json in {vcldPath}");
                    return pkg;
                }
                using (var stream = configEntry.Open()) {
                    pkg.Config = JsonSerializer.Deserialize<VocaloidPackageConfig>(stream, _jsonOpts)
                                 ?? new VocaloidPackageConfig();
                }

                // ── 2. derive extract dir name ────────────────────────────
                string pkgId = SanitizeDirName(
                    string.IsNullOrWhiteSpace(pkg.Config.Id)
                        ? Path.GetFileNameWithoutExtension(vcldPath)
                        : pkg.Config.Id);
                string extractDir = Path.Combine(extractRoot, pkgId);
                Directory.CreateDirectory(extractDir);

                // ── 3. extract binary assets ──────────────────────────────
                pkg.DdbPath = ExtractEntry(zip, pkg.Config.Ddb, extractDir, pkg.Errors);
                pkg.DdiPath = ExtractEntry(zip, pkg.Config.Ddi, extractDir, pkg.Errors);
                pkg.VvdPath = ExtractEntry(zip, pkg.Config.Vvd, extractDir, pkg.Errors);

                // ── 4. load avatar / portrait into memory ─────────────────
                pkg.AvatarBytes   = ReadEntryBytes(zip, pkg.Config.Image);
                pkg.PortraitBytes = ReadEntryBytes(zip, pkg.Config.Portrait);

            } catch (Exception ex) {
                Log.Error(ex, $"[vcld] failed to load {vcldPath}");
                pkg.Errors.Add($"[vcld] exception: {ex.Message}");
            }

            return pkg;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string ExtractEntry(ZipArchive zip, string entryPath,
                                           string destDir, List<string> errors) {
            if (string.IsNullOrWhiteSpace(entryPath)) return string.Empty;

            var entry = zip.GetEntry(entryPath)
                     ?? zip.GetEntry(Path.GetFileName(entryPath)); // fallback: flat name
            if (entry == null) {
                errors.Add($"[vcld] missing entry: {entryPath}");
                return string.Empty;
            }

            string dest = Path.Combine(destDir, Path.GetFileName(entryPath));
            // only re-extract if stale
            if (!File.Exists(dest) || new FileInfo(dest).Length != entry.Length) {
                using var src  = entry.Open();
                using var file = File.Create(dest);
                src.CopyTo(file);
            }
            return dest;
        }

        private static byte[]? ReadEntryBytes(ZipArchive zip, string entryPath) {
            if (string.IsNullOrWhiteSpace(entryPath)) return null;
            var entry = zip.GetEntry(entryPath)
                     ?? zip.GetEntry(Path.GetFileName(entryPath));
            if (entry == null) return null;
            using var stream = entry.Open();
            using var ms     = new MemoryStream((int)entry.Length);
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static string SanitizeDirName(string raw) {
            foreach (char c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
