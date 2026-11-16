// OpenUtau.Core/Vocaloid/VocaloidBankProbe.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Diagnóstico de un banco VOCALOID: guarda en la caché un informe con tamaño y primeros bytes
// (hex + ASCII) de los .ddi/.ddb/.vvd. Solo lee 128 bytes de cada archivo. Sirve para
// identificar la cabecera real del formato y escribir un decodificador de muestras sin
// adivinar offsets.

using System;
using System.IO;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    internal static class VocaloidBankProbe {

        private const int kHeadBytes = 128;

        public static string BuildReport(string id, VocaloidVersion version, string ddi, string ddb, string vvd) {
            var sb = new StringBuilder();
            sb.AppendLine($"OpenLoid-Flame bank probe · {id} · {VocaloidVersions.Display(version)}");
            sb.AppendLine($"UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            AppendFile(sb, ".ddi", ddi);
            AppendFile(sb, ".ddb", ddb);
            AppendFile(sb, ".vvd", vvd);
            return sb.ToString();
        }

        private static void AppendFile(StringBuilder sb, string label, string path) {
            sb.AppendLine();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                sb.AppendLine($"[{label}] not present");
                return;
            }
            try {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] head = new byte[(int)Math.Min(kHeadBytes, fs.Length)];
                int read = fs.Read(head, 0, head.Length);
                sb.AppendLine($"[{label}] {Path.GetFileName(path)} · {fs.Length} bytes");
                for (int off = 0; off < read; off += 16) {
                    int n = Math.Min(16, read - off);
                    var hex = new StringBuilder();
                    var asc = new StringBuilder();
                    for (int i = 0; i < n; i++) {
                        byte b = head[off + i];
                        hex.Append(b.ToString("x2")).Append(' ');
                        asc.Append(b >= 32 && b < 127 ? (char)b : '.');
                    }
                    sb.AppendLine($"{off:x4}  {hex.ToString().PadRight(48)} {asc}");
                }
            } catch (Exception ex) {
                sb.AppendLine($"[{label}] cannot read: {ex.Message}");
            }
        }

        /// <summary>Copia el informe a Descargas para verlo desde Termux o el gestor de archivos.</summary>
        private static void CopyToDownloads(string fileName, string report) {
            try {
                const string downloads = "/storage/emulated/0/Download";
                if (!Directory.Exists(downloads)) return;
                string target = Path.Combine(downloads, fileName);
                File.WriteAllText(target, report);
                Log.Information($"[VocaloidProbe] copy saved to {target}");
            } catch (Exception ex) {
                Log.Warning(ex, "[VocaloidProbe] cannot copy the report to Downloads (storage permission?)");
            }
        }

        /// <summary>Escribe el informe en la caché de VOCALOID. Nunca lanza.</summary>
        public static void TryWrite(string id, VocaloidVersion version, string ddi, string ddb, string vvd) {
            try {
                string dir = Path.Combine(PathManager.Inst.CachePath, VocaloidPackageLoader.CacheFolderName);
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "probe-" + VocaloidPackageLoader.SanitizeDirName(id) + ".txt");
                string report = BuildReport(id, version, ddi, ddb, vvd);
                File.WriteAllText(file, report);
                Log.Information($"[VocaloidProbe] bank report saved to {file}");
                CopyToDownloads(Path.GetFileName(file), report);
            } catch (Exception ex) {
                Log.Warning(ex, "[VocaloidProbe] cannot write the bank report");
            }
        }
    }
}
