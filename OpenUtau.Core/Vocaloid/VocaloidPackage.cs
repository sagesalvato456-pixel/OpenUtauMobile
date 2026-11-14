// OpenUtau.Core/Vocaloid/VocaloidPackage.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// Parses a .vcld package (zip) with its package-config.json manifest, or a loose
// voicebank folder. Binary assets: .ddb (samples), .ddi (index), .vvd (small info file).
// Works for VOCALOID 1..6 voicebanks, see VocaloidVersion.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        // .ddb file path inside zip (samples). Optional: found by extension when empty.
        [JsonPropertyName("ddb")]
        public string Ddb { get; set; } = string.Empty;

        // .ddi file path inside zip (index). Optional: found by extension when empty.
        [JsonPropertyName("ddi")]
        public string Ddi { get; set; } = string.Empty;

        // .vvd file path inside zip (small voicebank info file shared by V2..V5). Optional.
        [JsonPropertyName("vvd")]
        public string Vvd { get; set; } = string.Empty;

        // Engine generation. Number or string: 3, "3", "v3", "VOCALOID3". Optional.
        [JsonPropertyName("vocaloidVersion")]
        public JsonElement VocaloidVersionRaw { get; set; }

        [JsonIgnore]
        public VocaloidVersion EngineVersion => VocaloidVersions.FromJson(VocaloidVersionRaw);

        // phonemizer type override, same convention as VoicebankConfig.DefaultPhonemizer
        [JsonPropertyName("defaultPhonemizer")]
        public string DefaultPhonemizer { get; set; } = string.Empty;

        // search keywords
        [JsonPropertyName("searchTerms")]
        public List<string>? SearchTerms { get; set; }
    }

    // ── extracted / resolved package data ────────────────────────────────────

    /// <summary>
    /// All data extracted from a .vcld archive or read from a loose folder. Binary assets
    /// of a .vcld are extracted under PathManager.Inst.CachePath on first load;
    /// avatar/portrait bytes are kept in memory.
    /// </summary>
    public class VocaloidPackageData {
        public VocaloidPackageConfig Config { get; set; } = new();
        public VocaloidVersion EngineVersion { get; set; } = VocaloidVersion.Unknown;

        // Absolute paths to the binary assets
        public string DdbPath { get; set; } = string.Empty;
        public string DdiPath { get; set; } = string.Empty;
        public string VvdPath { get; set; } = string.Empty;

        public byte[]? AvatarBytes { get; set; }
        public byte[]? PortraitBytes { get; set; }

        public List<string> Errors { get; } = new();
    }

    // ── asset pairing ─────────────────────────────────────────────────────────

    internal readonly record struct VocaloidAssetSet(string? Ddb, string? Ddi, string? Vvd);

    internal static class VocaloidAssets {

        public static bool HasExtension(string path, string extension) {
            return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        public static string BaseName(string path) {
            return Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        }

        /// <summary>
        /// Picks one .ddb/.ddi/.vvd set. A .ddb and .ddi that share a base name belong together;
        /// when several sets exist the biggest .ddb wins. Falls back to the biggest file of each kind.
        /// </summary>
        public static VocaloidAssetSet Pick(IEnumerable<(string Path, long Size)> files) {
            var all = files.ToList();
            var ddbs = all.Where(f => HasExtension(f.Path, VocaloidVersions.DdbExtension))
                .OrderByDescending(f => f.Size).ToList();
            var ddis = all.Where(f => HasExtension(f.Path, VocaloidVersions.DdiExtension))
                .OrderByDescending(f => f.Size).ToList();
            var vvds = all.Where(f => HasExtension(f.Path, VocaloidVersions.VvdExtension))
                .OrderByDescending(f => f.Size).ToList();

            string? ddb = null;
            string? ddi = null;
            foreach (var candidate in ddbs) {
                string name = BaseName(candidate.Path);
                var mate = ddis.FirstOrDefault(d => string.Equals(BaseName(d.Path), name, StringComparison.OrdinalIgnoreCase));
                if (mate.Path != null) {
                    ddb = candidate.Path;
                    ddi = mate.Path;
                    break;
                }
            }
            if (ddb == null) {
                ddb = ddbs.Count > 0 ? ddbs[0].Path : null;
                ddi = ddis.Count > 0 ? ddis[0].Path : null;
            }

            string? vvd = null;
            if (ddb != null) {
                string name = BaseName(ddb);
                var mate = vvds.FirstOrDefault(v => string.Equals(BaseName(v.Path), name, StringComparison.OrdinalIgnoreCase));
                vvd = mate.Path;
            }
            if (vvd == null && vvds.Count > 0) {
                vvd = vvds[0].Path;
            }
            return new VocaloidAssetSet(ddb, ddi, vvd);
        }
    }

    // ── loader ────────────────────────────────────────────────────────────────

    public static class VocaloidPackageLoader {

        /// <summary>Folder under PathManager.Inst.CachePath that holds extracted .vcld assets.</summary>
        public const string CacheFolderName = "vocaloid";

        private const string kConfigName = "package-config.json";
        private const long kMaxImageBytes = 16L * 1024 * 1024;

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
            return LoadCore(vcldPath, extractRoot, extractAssets: true);
        }

        /// <summary>
        /// Same as <see cref="Load"/> but extracts nothing: manifest, names and avatar only.
        /// Cheap enough to run for every singer in the list.
        /// </summary>
        public static VocaloidPackageData LoadMetadata(string vcldPath) {
            return LoadCore(vcldPath, string.Empty, extractAssets: false);
        }

        private static VocaloidPackageData LoadCore(string vcldPath, string extractRoot, bool extractAssets) {
            if (Directory.Exists(vcldPath)) {
                return LoadFromDirectory(vcldPath);
            }

            var pkg = new VocaloidPackageData();
            if (!File.Exists(vcldPath)) {
                pkg.Errors.Add($"[vcld] file not found: {vcldPath}");
                return pkg;
            }

            try {
                using var zip = ZipFile.OpenRead(vcldPath);

                // ── 1. parse package-config.json ──────────────────────────
                var configEntry = FindEntry(zip, kConfigName);
                if (configEntry == null) {
                    pkg.Errors.Add($"[vcld] missing {kConfigName} in {vcldPath}");
                    return pkg;
                }
                using (var stream = configEntry.Open()) {
                    pkg.Config = JsonSerializer.Deserialize<VocaloidPackageConfig>(stream, _jsonOpts)
                                 ?? new VocaloidPackageConfig();
                }

                // ── 2. find the binary assets: manifest names win, otherwise by extension ──
                var found = VocaloidAssets.Pick(zip.Entries
                    .Where(e => e.Length > 0)
                    .Select(e => (e.FullName, e.Length)));
                string ddbName = FirstNonEmpty(pkg.Config.Ddb, found.Ddb);
                string ddiName = FirstNonEmpty(pkg.Config.Ddi, found.Ddi);
                string vvdName = FirstNonEmpty(pkg.Config.Vvd, found.Vvd);

                pkg.EngineVersion = pkg.Config.EngineVersion != VocaloidVersion.Unknown
                    ? pkg.Config.EngineVersion
                    : VocaloidVersions.Detect(pkg.Config.Id, pkg.Config.Name,
                        string.IsNullOrEmpty(ddbName) ? null : VocaloidAssets.BaseName(ddbName),
                        Path.GetFileNameWithoutExtension(vcldPath));

                // ── 3. extract binary assets ──────────────────────────────
                if (extractAssets) {
                    string extractDir = Path.Combine(extractRoot, PackageDirName(pkg.Config, vcldPath));
                    Directory.CreateDirectory(extractDir);
                    pkg.DdbPath = ExtractEntry(zip, ddbName, extractDir, pkg.Errors, required: true);
                    pkg.DdiPath = ExtractEntry(zip, ddiName, extractDir, pkg.Errors, required: true);
                    pkg.VvdPath = ExtractEntry(zip, vvdName, extractDir, pkg.Errors, required: false);
                }

                // ── 4. load avatar / portrait into memory ─────────────────
                pkg.AvatarBytes   = ReadEntryBytes(zip, pkg.Config.Image);
                pkg.PortraitBytes = ReadEntryBytes(zip, pkg.Config.Portrait);

            } catch (Exception ex) {
                Log.Error(ex, $"[vcld] failed to load {vcldPath}");
                pkg.Errors.Add($"[vcld] exception: {ex.Message}");
            }

            return pkg;
        }

        /// <summary>
        /// Loads a loose voicebank folder (no .vcld wrapper): picks the .ddb/.ddi/.vvd set it
        /// contains and reads an optional package-config.json / profile image.
        /// </summary>
        public static VocaloidPackageData LoadFromDirectory(string dir) {
            var pkg = new VocaloidPackageData();
            try {
                string configPath = Path.Combine(dir, kConfigName);
                if (File.Exists(configPath)) {
                    pkg.Config = JsonSerializer.Deserialize<VocaloidPackageConfig>(
                        File.ReadAllText(configPath), _jsonOpts) ?? new VocaloidPackageConfig();
                }

                var files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly)
                    .Select(f => new FileInfo(f)).ToList();
                var set = VocaloidAssets.Pick(files.Select(f => (f.FullName, f.Length)));
                pkg.DdbPath = set.Ddb ?? string.Empty;
                pkg.DdiPath = set.Ddi ?? string.Empty;
                pkg.VvdPath = set.Vvd ?? string.Empty; // optional
                if (pkg.DdbPath.Length == 0) pkg.Errors.Add($"[vocaloid] no .ddb in {dir}");
                if (pkg.DdiPath.Length == 0) pkg.Errors.Add($"[vocaloid] no .ddi in {dir}");

                if (string.IsNullOrWhiteSpace(pkg.Config.Name)) {
                    pkg.Config.Name = GuessName(dir, pkg.DdbPath);
                }
                if (string.IsNullOrWhiteSpace(pkg.Config.DefaultPhonemizer)) {
                    pkg.Config.DefaultPhonemizer = DefaultPhonemizerName;
                }

                string? parentName = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                pkg.EngineVersion = pkg.Config.EngineVersion != VocaloidVersion.Unknown
                    ? pkg.Config.EngineVersion
                    : VocaloidVersions.Detect(pkg.Config.Name,
                        pkg.DdbPath.Length == 0 ? null : Path.GetFileNameWithoutExtension(pkg.DdbPath),
                        new DirectoryInfo(dir).Name, parentName);

                string? avatar = PickImage(files, "profile", "avatar", "icon", "image", "cover");
                if (avatar != null) pkg.AvatarBytes = ReadImage(avatar);
                string? portrait = PickImage(files, "background", "portrait");
                if (portrait != null && portrait != avatar) pkg.PortraitBytes = ReadImage(portrait);
            } catch (Exception ex) {
                Log.Error(ex, $"[vocaloid] failed to load folder {dir}");
                pkg.Errors.Add($"[vocaloid] exception: {ex.Message}");
            }
            return pkg;
        }

        /// <summary>Phonemizer used for banks that do not name one.</summary>
        public const string DefaultPhonemizerName = "OpenUtau.Core.Vocaloid.VocaloidJapanesePhonemizer";

        /// <summary>
        /// Deletes the extracted copy of a .vcld from the cache (every "vocaloid*" cache folder,
        /// so caches left by older builds go too). Call it before deleting the .vcld itself.
        /// </summary>
        public static void DeleteCache(string vcldPath, string cacheBase) {
            try {
                if (string.IsNullOrWhiteSpace(cacheBase) || !Directory.Exists(cacheBase)) {
                    return;
                }
                var config = new VocaloidPackageConfig();
                if (File.Exists(vcldPath)) {
                    try {
                        using var zip = ZipFile.OpenRead(vcldPath);
                        var entry = FindEntry(zip, kConfigName);
                        if (entry != null) {
                            using var stream = entry.Open();
                            config = JsonSerializer.Deserialize<VocaloidPackageConfig>(stream, _jsonOpts) ?? config;
                        }
                    } catch (Exception ex) {
                        Log.Warning(ex, $"[vcld] cannot read manifest of {vcldPath} while cleaning the cache");
                    }
                }
                string pkgDir = PackageDirName(config, vcldPath);
                foreach (string folder in Directory.EnumerateDirectories(cacheBase, "vocaloid*")) {
                    string target = Path.Combine(folder, pkgDir);
                    if (Directory.Exists(target)) {
                        Directory.Delete(target, true);
                        Log.Information($"[vcld] removed cache {target}");
                    }
                }
            } catch (Exception ex) {
                Log.Warning(ex, $"[vcld] failed to clean the cache of {vcldPath}");
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────

        internal static string PackageDirName(VocaloidPackageConfig config, string vcldPath) {
            return SanitizeDirName(string.IsNullOrWhiteSpace(config.Id)
                ? Path.GetFileNameWithoutExtension(vcldPath)
                : config.Id);
        }

        private static string FirstNonEmpty(string? preferred, string? fallback) {
            if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
            return fallback ?? string.Empty;
        }

        /// <summary>Installed banks sit in a folder named after a 16 character component id.</summary>
        internal static bool IsComponentId(string name) {
            if (name.Length != 16) return false;
            bool letter = false, digit = false;
            foreach (char c in name) {
                if (c >= '0' && c <= '9') digit = true;
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) letter = true;
                else return false;
            }
            return letter && digit;
        }

        /// <summary>Folder name, unless it is a component id: then the parent folder, then the .ddb name.</summary>
        internal static string GuessName(string dir, string ddbPath) {
            var info = new DirectoryInfo(dir);
            string name = info.Name;
            if (!IsComponentId(name)) {
                return name;
            }
            string? parent = info.Parent?.Name;
            if (!string.IsNullOrWhiteSpace(parent) && !IsComponentId(parent)) {
                return parent;
            }
            return ddbPath.Length > 0 ? Path.GetFileNameWithoutExtension(ddbPath) : name;
        }

        private static string? PickImage(IEnumerable<FileInfo> files, params string[] preferredNames) {
            var images = files.Where(f => {
                string ext = f.Extension.ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
            }).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string preferred in preferredNames) {
                var hit = images.FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f.Name), preferred, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit.FullName;
            }
            // Only the avatar falls back to "any image"; the portrait needs an explicit name.
            return preferredNames.Contains("profile") && images.Count > 0 ? images[0].FullName : null;
        }

        private static byte[]? ReadImage(string path) {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > kMaxImageBytes) return null;
            return File.ReadAllBytes(path);
        }

        private static ZipArchiveEntry? FindEntry(ZipArchive zip, string entryPath) {
            if (string.IsNullOrWhiteSpace(entryPath)) return null;
            string normalized = entryPath.Replace('\\', '/').TrimStart('/');
            var entry = zip.GetEntry(entryPath) ?? zip.GetEntry(normalized);
            if (entry != null) return entry;
            entry = zip.Entries.FirstOrDefault(e => string.Equals(
                e.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
            if (entry != null) return entry;
            // last resort: flat name anywhere in the archive
            string fileName = Path.GetFileName(normalized);
            return zip.Entries.FirstOrDefault(e => e.Length > 0 && string.Equals(
                Path.GetFileName(e.FullName.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractEntry(ZipArchive zip, string entryPath,
                                           string destDir, List<string> errors, bool required) {
            if (string.IsNullOrWhiteSpace(entryPath)) {
                if (required) errors.Add("[vcld] package has no voicebank file of this kind (.ddb/.ddi)");
                return string.Empty;
            }

            var entry = FindEntry(zip, entryPath);
            string fileName = Path.GetFileName(entryPath.Replace('\\', '/'));
            if (entry == null) {
                if (required) errors.Add($"[vcld] missing entry: {entryPath}");
                return string.Empty;
            }
            if (fileName.Length == 0 || fileName == "." || fileName == "..") {
                errors.Add($"[vcld] invalid entry name: {entryPath}");
                return string.Empty;
            }

            string dest = Path.Combine(destDir, SanitizeDirName(fileName));
            ExtractIfStale(entry, dest);
            return dest;
        }

        /// <summary>
        /// Extracts through a ".part" file so an interrupted run never leaves a half file behind,
        /// and stamps the zip's timestamp on the result so a replaced package is noticed.
        /// </summary>
        private static void ExtractIfStale(ZipArchiveEntry entry, string dest) {
            DateTime stamp = entry.LastWriteTime.UtcDateTime;
            if (File.Exists(dest)) {
                var existing = new FileInfo(dest);
                if (existing.Length == entry.Length
                    && Math.Abs((existing.LastWriteTimeUtc - stamp).TotalSeconds) <= 2) {
                    return;
                }
            }
            string temp = dest + ".part";
            using (var src = entry.Open())
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                src.CopyTo(file, 1 << 20);
            }
            try {
                File.SetLastWriteTimeUtc(temp, stamp);
            } catch (Exception ex) {
                Log.Warning(ex, $"[vcld] cannot stamp {temp}");
            }
            File.Move(temp, dest, overwrite: true);
        }

        private static byte[]? ReadEntryBytes(ZipArchive zip, string entryPath) {
            var entry = FindEntry(zip, entryPath);
            if (entry == null || entry.Length > kMaxImageBytes) return null;
            using var stream = entry.Open();
            using var ms     = new MemoryStream((int)entry.Length);
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        internal static string SanitizeDirName(string raw) {
            foreach (char c in Path.GetInvalidFileNameChars())
                raw = raw.Replace(c, '_');
            raw = raw.Trim();
            // ".", ".." and empty names would point at the parent folder.
            return raw.Length == 0 || raw.Trim('.').Length == 0 ? "package" : raw;
        }
    }
}
