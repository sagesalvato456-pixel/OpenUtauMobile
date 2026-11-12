// OpenUtau.Core/Vocaloid/VocaloidSinger.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// USinger implementation wrapping a loaded .vcld Vocaloid 4 voicebank.
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
        public override string Location        => _vcldPath;    // .vcld file = "location"
        public override string Web             => _config.Web;
        public override string Version         => _config.Version;
        public override string OtherInfo       => string.Empty;
        public override IList<string> Errors   => _pkg.Errors;

        // ── avatar / portrait ─────────────────────────────────────────────────
        // Avatar and portrait bytes come from inside the zip so we keep them
        // in memory; no on-disk path exposed.

        public override string  Avatar          => string.Empty;  // bytes-only path
        public override byte[]  AvatarData      => _pkg.AvatarBytes ?? Array.Empty<byte>();
        public override string  Portrait        => string.Empty;
        public override float   PortraitOpacity => _config.PortraitOpacity;
        public override int     PortraitHeight  => 0;

        public override string DefaultPhonemizer => _config.DefaultPhonemizer ?? string.Empty;
        public override string Sample            => string.Empty;
        public override Encoding TextFileEncoding => Encoding.UTF8;

        // V4 voicebanks expose no UTAU-style oto entries
        public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
        public override IList<UOto>     Otos     => emptyOtos;

        // ── binary asset paths (for the renderer / DSP bridge) ───────────────

        /// <summary>Absolute path to the extracted .ddb (phoneme database).</summary>
        public string DdbPath => _pkg.DdbPath;

        /// <summary>Absolute path to the extracted .ddi (voice descriptor index).</summary>
        public string DdiPath => _pkg.DdiPath;

        /// <summary>Absolute path to the extracted .vvd (voice waveform data).</summary>
        public string VvdPath => _pkg.VvdPath;

        // ── internals ─────────────────────────────────────────────────────────

        private readonly string _vcldPath;
        private readonly string _basePath;
        private readonly string _id;
        private VocaloidPackageConfig _config;
        private VocaloidPackageData   _pkg;
        private readonly Dictionary<string, string> _localizedNames;
        private readonly List<string> _searchTerms;

        private bool _disposed;

        // ── ctor ──────────────────────────────────────────────────────────────

        /// <param name="vcldPath">Absolute path to the .vcld archive.</param>
        /// <param name="basePath">The singers root directory it was found under.</param>
        public VocaloidSinger(string vcldPath, string basePath) {
            _vcldPath = vcldPath;
            _basePath = basePath;

            // id = path relative to singers root, without extension
            _id = Path.ChangeExtension(
                      Path.GetRelativePath(basePath, vcldPath), null);

            // placeholder until EnsureLoaded
            _config         = new VocaloidPackageConfig { Name = Path.GetFileNameWithoutExtension(vcldPath) };
            _pkg            = new VocaloidPackageData();
            _localizedNames = new Dictionary<string, string>();
            _searchTerms    = new List<string>();

            found  = true;
            loaded = false;
        }

        // ── lifecycle ─────────────────────────────────────────────────────────

        public override void EnsureLoaded() {
            if (loaded) return;
            Load();
        }

        public override void Reload() {
            loaded = false;
            Load();
        }

        private void Load() {
            try {
                string extractRoot = Path.Combine(
                    PathManager.Inst.CachePath, "vocaloid4");
                Directory.CreateDirectory(extractRoot);

                _pkg = VocaloidPackageLoader.Load(_vcldPath, extractRoot);
                _config = _pkg.Config;

                _localizedNames.Clear();
                if (_config.LocalizedNames != null)
                    foreach (var kv in _config.LocalizedNames)
                        _localizedNames[kv.Key] = kv.Value;

                _searchTerms.Clear();
                if (_config.SearchTerms != null)
                    _searchTerms.AddRange(_config.SearchTerms);

                loaded = _pkg.Errors.Count == 0;
                if (!loaded)
                    Log.Warning($"[VocaloidSinger] {_id} loaded with errors: {string.Join("; ", _pkg.Errors)}");
                else
                    Log.Information($"[VocaloidSinger] loaded {_id} (ddb={DdbPath}, ddi={DdiPath}, vvd={VvdPath})");
            } catch (Exception ex) {
                Log.Error(ex, $"[VocaloidSinger] failed to load {_id}");
                _pkg.Errors.Add(ex.Message);
                loaded = false;
            }
        }

        public override void FreeMemory() {
            // portrait/avatar bytes are small but free them on memory pressure
            if (_pkg != null) {
                _pkg.AvatarBytes   = null;
                _pkg.PortraitBytes = null;
            }
            loaded = false;
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
