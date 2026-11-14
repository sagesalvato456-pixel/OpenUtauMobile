// OpenUtau.Core/Vocaloid/VocaloidSingerLoader.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// Scans SingersPaths for VOCALOID voicebanks (V1..V6): .vcld packages and loose folders
// holding a .ddb together with its .ddi. Mirrors the pattern of Classic.ClassicSingerLoader
// and Vogen.VogenSingerLoader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    public static class VocaloidSingerLoader {

        private const string kVcldExtension = VocaloidVersions.VcldExtension;

        // Deep enough for "Vendor/Bank/CompID/", shallow enough to survive a symlink loop.
        private const int kMaxDepth = 8;

        /// <summary>
        /// Walks every path in <see cref="PathManager.Inst.SingersPaths"/> once and returns a
        /// <see cref="VocaloidSinger"/> for each <c>.vcld</c> package and each folder that holds a
        /// <c>.ddb</c> with a <c>.ddi</c> (no .vcld wrapper needed, e.g. extracted by the setup wizard).
        /// Mirrors ClassicSingerLoader.FindAllSingers() contract.
        /// </summary>
        public static IEnumerable<USinger> FindAllSingers() {
            var singers = new List<USinger>();
            foreach (string root in PathManager.Inst.SingersPaths) {
                if (!Directory.Exists(root)) continue;
                try {
                    Scan(root, singers);
                } catch (Exception ex) {
                    Log.Error(ex, $"[VocaloidSingerLoader] cannot scan {root}");
                }
            }
            return singers;
        }

        private static bool IsUtauMarker(string file) {
            string name = Path.GetFileName(file);
            return string.Equals(name, "oto.ini", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "character.txt", StringComparison.OrdinalIgnoreCase);
        }

        // Half-finished installs and the backup of a replaced install must not show up as singers.
        private static bool IsInstallerLeftover(string dir) {
            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return name.EndsWith(VocaloidInstaller.StagingSuffix, StringComparison.Ordinal)
                || name.EndsWith(VocaloidInstaller.BackupSuffix, StringComparison.Ordinal);
        }

        private static void Scan(string root, List<USinger> singers) {
            var options = new EnumerationOptions {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                // dot folders (.git, .thumbnails) and system folders never hold voicebanks
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            var pending = new Stack<(string Dir, int Depth)>();
            pending.Push((root, 0));

            while (pending.Count > 0) {
                var (dir, depth) = pending.Pop();
                List<string> files;
                try {
                    files = Directory.EnumerateFiles(dir, "*", options).ToList();
                } catch (Exception ex) {
                    Log.Warning(ex, $"[VocaloidSingerLoader] cannot read {dir}");
                    continue;
                }

                foreach (string file in files.Where(f => VocaloidAssets.HasExtension(f, kVcldExtension))) {
                    try {
                        singers.Add(new VocaloidSinger(file, root));
                        Log.Information($"[VocaloidSingerLoader] found {file}");
                    } catch (Exception ex) {
                        Log.Error(ex, $"[VocaloidSingerLoader] skipping {file}");
                    }
                }

                bool hasDdb = files.Any(f => VocaloidAssets.HasExtension(f, VocaloidVersions.DdbExtension));
                bool hasDdi = files.Any(f => VocaloidAssets.HasExtension(f, VocaloidVersions.DdiExtension));
                if (hasDdb && hasDdi) {
                    // a voicebank folder is a leaf: nothing below it is another bank
                    try {
                        singers.Add(new VocaloidSinger(dir, root));
                        Log.Information($"[VocaloidSingerLoader] found folder {dir}");
                    } catch (Exception ex) {
                        Log.Error(ex, $"[VocaloidSingerLoader] skipping folder {dir}");
                    }
                    continue;
                }

                // a UTAU voicebank is a leaf too: its subfolders hold samples, never other banks
                if (depth > 0 && files.Any(IsUtauMarker)) continue;

                if (depth >= kMaxDepth) continue;
                try {
                    var subdirs = Directory.EnumerateDirectories(dir, "*", options)
                        .Where(d => !IsInstallerLeftover(d))
                        .OrderByDescending(d => d, StringComparer.Ordinal).ToList(); // popped in ascending order
                    foreach (string sub in subdirs) {
                        pending.Push((sub, depth + 1));
                    }
                } catch (Exception ex) {
                    Log.Warning(ex, $"[VocaloidSingerLoader] cannot list {dir}");
                }
            }
        }
    }
}
