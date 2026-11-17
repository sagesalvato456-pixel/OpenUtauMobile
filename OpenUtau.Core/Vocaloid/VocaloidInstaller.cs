// OpenUtau.Core/Vocaloid/VocaloidInstaller.cs
// Installs a VOCALOID voicebank archive (.zip/.rar/.7z/.uar containing .ddb/.ddi/.vvd,
// or a .vcld package) into the singers install path so VocaloidSingerLoader can find it.
// Same asset family for VOCALOID 1..6, see VocaloidVersion.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtau.Core.Vocaloid {

    public static class VocaloidInstaller {

        /// <summary>Folder an install is extracted into before it replaces the old one. The loader skips it.</summary>
        public const string StagingSuffix = ".ou-installing";

        /// <summary>Folder the old install waits in while the new one moves in. The loader skips it.</summary>
        public const string BackupSuffix = ".ou-old";

        // Voicebank archives often carry the Windows installer too. Nothing here can run it.
        private static readonly HashSet<string> kSkippedExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".exe", ".msi", ".dll", ".bat", ".cmd", ".com", ".scr",
        };

        /// <returns>The file (.vcld) or folder the voicebank was installed to.</returns>
        public static string Install(string archivePath, string basePath,
                                     Action<double, string>? progress, Encoding archiveEncoding) {
            Directory.CreateDirectory(basePath);
            progress?.Invoke(0, "Analyzing archive...");

            // A bare .vcld is already a ready package: check it, then copy it.
            if (VocaloidAssets.HasExtension(archivePath, VocaloidVersions.VcldExtension)) {
                ValidatePackage(archivePath);
                string target = Path.Combine(basePath,
                    VocaloidPackageLoader.SanitizeDirName(Path.GetFileName(archivePath)));
                File.Copy(archivePath, target, true);
                progress?.Invoke(100, "Done");
                return target;
            }

            string folderName = VocaloidPackageLoader.SanitizeDirName(Path.GetFileNameWithoutExtension(archivePath));
            string destRoot = Path.Combine(basePath, folderName);
            string staging = destRoot + StagingSuffix;
            string backup = destRoot + BackupSuffix;

            var readerOptions = new ReaderOptions {
                ArchiveEncoding = new ArchiveEncoding { Forced = archiveEncoding },
            };
            var extractionOptions = new ExtractionOptions { Overwrite = true };

            using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);
            var entries = archive.Entries.Where(e => !e.IsDirectory && e.Key != null).ToList();
            if (!entries.Any(e => IsVocaloidAsset(e.Key!))) {
                throw new InvalidDataException("No .ddb/.ddi/.vvd/.vcld files found in the archive.");
            }

            // Extract next to the target and swap at the end: a failed or cancelled install
            // never leaves a half voicebank, and files of an older version do not linger.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try {
                int total = entries.Count;
                int count = 0;
                int skipped = 0;
                foreach (var entry in entries) {
                    string key = entry.Key!.Replace('\\', '/');
                    progress?.Invoke(100.0 * ++count / total, key);
                    if (kSkippedExtensions.Contains(Path.GetExtension(key))) {
                        continue;
                    }
                    if (!TryResolve(staging, key, out string dest)) {
                        skipped++; // path escapes the install folder
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.WriteToFile(dest, extractionOptions);
                }
                if (skipped > 0) {
                    Log.Warning($"[VocaloidInstaller] skipped {skipped} unsafe path(s) in {archivePath}");
                }
                SwapIn(staging, destRoot, backup);
            } catch {
                TryDelete(staging);
                throw;
            }
            return destRoot;
        }

        /// <summary>
        /// Maps an archive entry name to a path inside <paramref name="root"/>.
        /// False when it would land outside (".." segments, absolute paths, drive letters).
        /// Names that merely contain two dots ("Miku..ddb") are fine.
        /// </summary>
        internal static bool TryResolve(string root, string key, out string dest) {
            dest = string.Empty;
            string relative = key.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0) return false;
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(rootFull, relative));
            if (!full.StartsWith(rootFull, StringComparison.Ordinal)) return false;
            dest = full;
            return true;
        }

        /// <summary>
        /// After a voicebank folder is deleted, removes the folders it leaves empty, climbing up
        /// to (never including) a singers root.
        /// </summary>
        public static void PruneEmptyParents(string path) {
            try {
                var roots = PathManager.Inst.SingersPaths
                    .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar)
                    .ToList();
                string? dir = Path.GetDirectoryName(Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                while (!string.IsNullOrEmpty(dir)) {
                    string withSeparator = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!roots.Any(r => withSeparator.StartsWith(r, StringComparison.Ordinal) && withSeparator != r)) break;
                    if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            } catch (Exception ex) {
                Log.Warning(ex, $"[VocaloidInstaller] cannot prune empty folders above {path}");
            }
        }

        private static void ValidatePackage(string vcldPath) {
            try {
                using var zip = ZipFile.OpenRead(vcldPath);
                bool hasManifest = zip.Entries.Any(e => string.Equals(
                    Path.GetFileName(e.FullName.Replace('\\', '/')), "package-config.json",
                    StringComparison.OrdinalIgnoreCase));
                if (!hasManifest) {
                    throw new InvalidDataException("The .vcld package has no package-config.json.");
                }
            } catch (InvalidDataException) {
                throw;
            } catch (Exception ex) {
                throw new InvalidDataException("The .vcld file is not a valid package: " + ex.Message, ex);
            }
        }

        private static void SwapIn(string staging, string destRoot, string backup) {
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            bool hadOld = Directory.Exists(destRoot);
            if (hadOld) Directory.Move(destRoot, backup);
            try {
                Directory.Move(staging, destRoot);
            } catch {
                if (hadOld && !Directory.Exists(destRoot)) Directory.Move(backup, destRoot);
                throw;
            }
            if (hadOld) TryDelete(backup);
        }

        private static void TryDelete(string dir) {
            try {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            } catch (Exception ex) {
                Log.Warning(ex, $"[VocaloidInstaller] cannot delete {dir}");
            }
        }

        static bool IsVocaloidAsset(string key) {
            string ext = Path.GetExtension(key).ToLowerInvariant();
            return ext == VocaloidVersions.DdbExtension || ext == VocaloidVersions.DdiExtension
                || ext == VocaloidVersions.VvdExtension || ext == VocaloidVersions.VcldExtension;
        }
    }
}
