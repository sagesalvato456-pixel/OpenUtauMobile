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


        public static string BuildReport(string id, VocaloidVersion version, string ddi, string ddb, string vvd) {
            var sb = new StringBuilder();
            sb.AppendLine($"OpenLoid-Flame bank probe · {id} · {VocaloidVersions.Display(version)}");
            sb.AppendLine($"UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            AppendFile(sb, ".ddi", ddi, 256);
            AppendFile(sb, ".ddb", ddb, 256);
            AppendFile(sb, ".vvd", vvd, 0);
            AppendVvd(sb, vvd);
            AppendDdiPhonemes(sb, ddi);
            AppendDdbFrames(sb, ddb);
            return sb.ToString();
        }

        private static void AppendFile(StringBuilder sb, string label, string path, int headBytes) {
            sb.AppendLine();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                sb.AppendLine($"[{label}] not present");
                return;
            }
            try {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] head = new byte[(int)Math.Min(headBytes, fs.Length)];
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

        /// <summary>El .vvd es texto ofuscado: cada byte se XORea con 0x1A (espacios y saltos de línea quedan igual).</summary>
        private static void AppendVvd(StringBuilder sb, string path) {
            sb.AppendLine();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try {
                byte[] raw = File.ReadAllBytes(path);
                var text = new StringBuilder(raw.Length);
                foreach (byte b in raw) {
                    bool plain = b == 0x20 || b == 0x0D || b == 0x0A;
                    text.Append((char)(plain ? b : b ^ 0x1A));
                }
                sb.AppendLine("[.vvd decoded, xor 0x1A]");
                sb.AppendLine(text.ToString());
            } catch (Exception ex) {
                sb.AppendLine($"[.vvd decoded] cannot read: {ex.Message}");
            }
        }

        /// <summary>Lista los textos cortos (nombres de fonema) de la sección PHDC del .ddi.</summary>
        private static void AppendDdiPhonemes(StringBuilder sb, string path) {
            sb.AppendLine();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buf = new byte[(int)Math.Min(fs.Length, 262144)];
                int read = fs.Read(buf, 0, buf.Length);
                int phdc = IndexOf(buf, read, new byte[] { 0x50, 0x48, 0x44, 0x43 }, 0);
                if (phdc < 0) { sb.AppendLine("[.ddi PHDC] tag not found in the first 256 KB"); return; }
                uint size = phdc + 8 <= read ? BitConverter.ToUInt32(buf, phdc + 4) : 0;
                int end = (int)Math.Min((long)phdc + 8 + size, read);
                sb.AppendLine($"[.ddi PHDC] at 0x{phdc:x} block size {size} (0x{size:x})");
                var line = new StringBuilder();
                int count = 0;
                int i = phdc + 8;
                while (i < end && count < 400) {
                    if (buf[i] >= 0x21 && buf[i] < 0x7F) {
                        int j = i;
                        var run = new StringBuilder();
                        while (j < end && buf[j] >= 0x21 && buf[j] < 0x7F && run.Length < 12) { run.Append((char)buf[j]); j++; }
                        line.Append($"{i:x}:{run} ");
                        count++;
                        i = j;
                    } else i++;
                }
                sb.AppendLine(line.ToString());
                sb.AppendLine($"[.ddi PHDC] {count} text runs listed");
            } catch (Exception ex) {
                sb.AppendLine($"[.ddi PHDC] cannot read: {ex.Message}");
            }
        }

        /// <summary>Busca las marcas FRM2 (frames) en los primeros 8 MB del .ddb.</summary>
        private static void AppendDdbFrames(StringBuilder sb, string path) {
            sb.AppendLine();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buf = new byte[(int)Math.Min(fs.Length, 8L * 1024 * 1024)];
                int read = fs.Read(buf, 0, buf.Length);
                byte[] tag = new byte[] { 0x46, 0x52, 0x4D, 0x32 };
                sb.AppendLine("[.ddb FRM2 tags in the first 8 MB] offset:size(u32 after tag)");
                int pos = 0, n = 0;
                while (n < 60 && (pos = IndexOf(buf, read, tag, pos)) >= 0) {
                    uint size = pos + 8 <= read ? BitConverter.ToUInt32(buf, pos + 4) : 0;
                    sb.Append($"{pos:x}:{size} ");
                    pos += 4;
                    n++;
                }
                sb.AppendLine();
                sb.AppendLine($"[.ddb] {n} FRM2 tags listed");
                // 64 bytes después de la cabecera del primer frame
                if (read > 0x80) {
                    var hex = new StringBuilder();
                    for (int i = 0x80; i < Math.Min(read, 0x1C0); i++) hex.Append(buf[i].ToString("x2")).Append(' ');
                    sb.AppendLine($"[.ddb] bytes 0x80..0x1bf: {hex}");
                }
            } catch (Exception ex) {
                sb.AppendLine($"[.ddb frames] cannot read: {ex.Message}");
            }
        }

        private static int IndexOf(byte[] buf, int length, byte[] pattern, int start) {
            for (int i = Math.Max(0, start); i + pattern.Length <= length; i++) {
                bool ok = true;
                for (int k = 0; k < pattern.Length; k++) {
                    if (buf[i + k] != pattern[k]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
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
