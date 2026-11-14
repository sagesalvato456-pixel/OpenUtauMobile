// OpenUtau.Core/Vocaloid/VocaloidSinger.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// USinger implementation wrapping a loaded VOCALOID voicebank (V1..V6, a .vcld package
// or a loose folder). Every generation reports USingerType.Vocaloid4, the only value the
// core enum has; the real generation is in EngineVersion.
// Synthesis is intentionally deferred — this layer handles identity,
// avatar/portrait, metadata, and binary-asset paths. Wire a renderer
// once the DSP bridge is ready.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Vocaloid {

    public class VocaloidSinger : USinger, IDisposable {

        // ── USinger identity ──────────────────────────────────────────────────

        public override string Id              => _id;
        public override string Name            => _config.Name;
        public override Dictionary<string, string> LocalizedNames => _localizedNames;
        public override IList<string> SearchTerms  => _searchTerms;
        public override USingerType SingerType => USingerType.Vocaloid4;
        public override string BasePath        => _basePath;
        public override string Author          => _config.Author;
        public override string Voice           => _config.Voice;
        public override string Location        => _vcldPath;    // .vcld file or voicebank folder
        public override string Web             => _config.Web;
        public override string Version         => _config.Version;
        public override string OtherInfo       => string.Empty;
        public override IList<string> Errors   => _pkg.Errors;

        /// <summary>Engine generation (V1..V6). Unknown when neither manifest nor names say.</summary>
        public VocaloidVersion EngineVersion => _version;

        // ── avatar / portrait ─────────────────────────────────────────────────
        // Avatar and portrait bytes come from inside the zip (or a loose image) so we
        // keep them in memory; no on-disk path exposed.

        public override string  Avatar          => string.Empty;  // bytes-only path
        public override byte[]  AvatarData      => GetAvatarBytes();
        public override string  Portrait        => string.Empty;
        public override float   PortraitOpacity => _config.PortraitOpacity;
        public override int     PortraitHeight  => 0;

        public override string DefaultPhonemizer => _config.DefaultPhonemizer ?? string.Empty;
        public override string Sample            => string.Empty;
        public override Encoding TextFileEncoding => Encoding.UTF8;

        // VOCALOID voicebanks expose no UTAU-style oto entries
        public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
        public override IList<UOto>     Otos     => emptyOtos;

        // ── binary asset paths (for the renderer / DSP bridge) ───────────────
        // Valid after EnsureLoaded(); a .vcld is extracted to the cache on that call.

        /// <summary>Absolute path to the .ddb (samples).</summary>
        public string DdbPath => _pkg.DdbPath;

        /// <summary>Absolute path to the .ddi (index).</summary>
        public string DdiPath => _pkg.DdiPath;

        /// <summary>Absolute path to the .vvd (small voicebank info file), empty when the bank has none.</summary>
        public string VvdPath => _pkg.VvdPath;

        // ── internals ─────────────────────────────────────────────────────────

        // _loadGate serialises loads (a .vcld extraction can take seconds); _stateGate only guards the
        // fields below for an instant, so the UI thread reading Name/AvatarData never waits on a load.
        private readonly object _loadGate = new();
        private readonly object _stateGate = new();
        private bool _avatarChecked;
        private readonly string _vcldPath;
        private readonly string _basePath;
        private readonly string _id;
        private VocaloidPackageConfig _config;
        private VocaloidPackageData   _pkg;
        private VocaloidVersion       _version;
        private readonly Dictionary<string, string> _localizedNames;
        private readonly List<string> _searchTerms;

        private bool _disposed;

        // ── ctor ──────────────────────────────────────────────────────────────

        /// <param name="vcldPath">Absolute path to the .vcld archive or to the voicebank folder.</param>
        /// <param name="basePath">The singers root directory it was found under.</param>
        public VocaloidSinger(string vcldPath, string basePath) {
            _vcldPath = vcldPath;
            _basePath = basePath;

            // id = path relative to singers root, without extension
            _id = Directory.Exists(vcldPath)
                ? Path.GetRelativePath(basePath, vcldPath)
                : Path.ChangeExtension(Path.GetRelativePath(basePath, vcldPath), null);

            // placeholder until the manifest is read
            _config         = new VocaloidPackageConfig { Name = Path.GetFileNameWithoutExtension(vcldPath) };
            _pkg            = new VocaloidPackageData();
            _version        = VocaloidVersions.Detect(Path.GetFileNameWithoutExtension(vcldPath));
            _localizedNames = new Dictionary<string, string>();
            _searchTerms    = new List<string>();

            found  = true;
            loaded = false;

            // Name, author and avatar without touching the big binary files.
            try {
                ApplyPackage(VocaloidPackageLoader.LoadMetadata(vcldPath));
            } catch (Exception ex) {
                Log.Warning(ex, $"[VocaloidSinger] cannot read metadata of {vcldPath}");
            }
        }

        // ── lifecycle ─────────────────────────────────────────────────────────

        public override void EnsureLoaded() {
            if (loaded) return;
            lock (_loadGate) {
                if (loaded) return;
                Load();
            }
        }

        public override void Reload() {
            lock (_loadGate) {
                loaded = false;
                Load();
            }
        }

        private void Load() {
            try {
                string extractRoot = Path.Combine(
                    PathManager.Inst.CachePath, VocaloidPackageLoader.CacheFolderName);
                Directory.CreateDirectory(extractRoot);

                ApplyPackage(VocaloidPackageLoader.Load(_vcldPath, extractRoot));

                loaded = _pkg.Errors.Count == 0;
                if (!loaded)
                    Log.Warning($"[VocaloidSinger] {_id} loaded with errors: {string.Join("; ", _pkg.Errors)}");
                else
                    Log.Information($"[VocaloidSinger] loaded {_id} ({VocaloidVersions.Display(_version)}, ddb={DdbPath}, ddi={DdiPath}, vvd={VvdPath})");
            } catch (Exception ex) {
                Log.Error(ex, $"[VocaloidSinger] failed to load {_id}");
                _pkg.Errors.Add(ex.Message);
                loaded = false;
            }
        }

        private void ApplyPackage(VocaloidPackageData pkg) {
            lock (_stateGate) {
                _pkg    = pkg;
                _config = pkg.Config;
                if (string.IsNullOrWhiteSpace(_config.Name)) {
                    _config.Name = Path.GetFileNameWithoutExtension(_vcldPath);
                }
                _version = pkg.EngineVersion != VocaloidVersion.Unknown
                    ? pkg.EngineVersion
                    : VocaloidVersions.Detect(_config.Name, Path.GetFileNameWithoutExtension(_vcldPath));

                _localizedNames.Clear();
                if (_config.LocalizedNames != null)
                    foreach (var kv in _config.LocalizedNames)
                        _localizedNames[kv.Key] = kv.Value;

                _searchTerms.Clear();
                if (_config.SearchTerms != null)
                    _searchTerms.AddRange(_config.SearchTerms);

                _avatarChecked = true; // the loader already looked for the image
            }
        }

        private byte[] GetAvatarBytes() {
            lock (_stateGate) {
                if (_pkg.AvatarBytes != null || _avatarChecked || _disposed) {
                    return _pkg.AvatarBytes ?? Array.Empty<byte>();
                }
            }
            // FreeMemory dropped it: read the image again (nothing is extracted), outside any lock.
            byte[]? bytes = null;
            try {
                bytes = VocaloidPackageLoader.LoadMetadata(_vcldPath).AvatarBytes;
            } catch (Exception ex) {
                Log.Warning(ex, $"[VocaloidSinger] cannot read avatar of {_id}");
            }
            lock (_stateGate) {
                _pkg.AvatarBytes ??= bytes;
                _avatarChecked = true;
                return _pkg.AvatarBytes ?? Array.Empty<byte>();
            }
        }

        public override void FreeMemory() {
            // portrait/avatar bytes are small but free them on memory pressure
            lock (_stateGate) {
                _pkg.AvatarBytes   = null;
                _pkg.PortraitBytes = null;
                _avatarChecked     = false;
            }
            loaded = false;
        }

        /// <summary>
        /// Deletes the copy of this .vcld extracted to the cache. Call it BEFORE deleting the .vcld
        /// (the manifest names the cache folder). No-op for loose folders.
        /// </summary>
        public void DeleteCache() {
            if (Directory.Exists(_vcldPath)) return;
            lock (_loadGate) {
                loaded = false;
                VocaloidPackageLoader.DeleteCache(_vcldPath, PathManager.Inst.CachePath);
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose() {
            if (_disposed) return;
            FreeMemory();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
