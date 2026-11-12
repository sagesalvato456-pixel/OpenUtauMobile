// OpenUtau.Core/Vocaloid/VocaloidSingerLoader.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// Scans SingersPaths for .vcld archives and returns VocaloidSinger instances.
// Mirrors the pattern of Classic.ClassicSingerLoader and Vogen.VogenSingerLoader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    public static class VocaloidSingerLoader {

        private const string kVcldExtension = ".vcld";

        /// <summary>
        /// Scans every path in <see cref="PathManager.Inst.SingersPaths"/> for
        /// <c>.vcld</c> archives and returns a <see cref="VocaloidSinger"/> for
        /// each one found. Mirrors ClassicSingerLoader.FindAllSingers() contract.
        /// </summary>
        public static IEnumerable<USinger> FindAllSingers() {
            var singers = new List<USinger>();

            foreach (string root in PathManager.Inst.SingersPaths) {
                if (!Directory.Exists(root)) continue;

                // search top-level and one level deep (matches classic UTAU convention)
                var vcldFiles = Directory
                    .EnumerateFiles(root, $"*{kVcldExtension}", SearchOption.AllDirectories);

                foreach (string vcld in vcldFiles) {
                    try {
                        var singer = new VocaloidSinger(vcld, root);
                        singers.Add(singer);
                        Log.Information($"[VocaloidSingerLoader] found {vcld}");
                    } catch (Exception ex) {
                        Log.Error(ex, $"[VocaloidSingerLoader] skipping {vcld}");
                    }
                }
            }

            return singers;
        }
    }
}
