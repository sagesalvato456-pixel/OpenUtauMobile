// OpenUtau.Core/Format/VPR.cs
// Lang: C# 10, Target: net10.0 / Android (OpenUtauMobile-dev / OpenLoid-Flame)
// Imports Vocaloid 5/6 .vpr project files into UProject.
//
// .vpr = zip archive containing:
//   Project/sequence.json   — full project JSON
//   (optional) Project/Audio/*.wav — rendered audio (ignored)
//
// Resolution: .vpr uses 480 ticks/quarter — same as UProject.resolution (480).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Format {

    // ── JSON model ────────────────────────────────────────────────────────────

    internal class VprTempoEvent {
        // ticks from project start
        [JsonPropertyName("pos")] public int Pos { get; set; }
        // BPM * 100  (12000 = 120.00 BPM)
        [JsonPropertyName("value")] public int Value { get; set; }
    }

    internal class VprTempoBlock {
        [JsonPropertyName("events")] public List<VprTempoEvent> Events { get; set; } = new();
        // some V5 exports use "global" single-value key
        [JsonPropertyName("global")] public VprTempoEvent? Global { get; set; }
    }

    internal class VprTimeSigEvent {
        [JsonPropertyName("bar")]   public int Bar   { get; set; }
        [JsonPropertyName("numer")] public int Numer { get; set; }
        [JsonPropertyName("denom")] public int Denom { get; set; }
    }

    internal class VprTimeSigBlock {
        [JsonPropertyName("events")] public List<VprTimeSigEvent> Events { get; set; } = new();
    }

    internal class VprNote {
        [JsonPropertyName("pos")]        public int    Pos        { get; set; }
        [JsonPropertyName("duration")]   public int    Duration   { get; set; }
        [JsonPropertyName("noteNumber")] public int    NoteNumber { get; set; }
        [JsonPropertyName("lyric")]      public string Lyric      { get; set; } = "a";
        [JsonPropertyName("phoneme")]    public string Phoneme    { get; set; } = "";
        // velocity 0-127
        [JsonPropertyName("velocity")]   public int    Velocity   { get; set; } = 64;
    }

    internal class VprPart {
        [JsonPropertyName("pos")]      public int           Pos      { get; set; }
        [JsonPropertyName("duration")] public int           Duration { get; set; }
        [JsonPropertyName("name")]     public string        Name     { get; set; } = "";
        [JsonPropertyName("notes")]    public List<VprNote> Notes    { get; set; } = new();
    }

    internal class VprTrack {
        // 0 = voice, 1 = audio/wave
        [JsonPropertyName("type")]  public int           Type  { get; set; }
        [JsonPropertyName("name")]  public string        Name  { get; set; } = "";
        [JsonPropertyName("parts")] public List<VprPart> Parts { get; set; } = new();
    }

    internal class VprSequence {
        [JsonPropertyName("title")]   public string          Title   { get; set; } = "";
        [JsonPropertyName("tempo")]   public VprTempoBlock?  Tempo   { get; set; }
        [JsonPropertyName("timeSig")] public VprTimeSigBlock? TimeSig { get; set; }
        [JsonPropertyName("tracks")]  public List<VprTrack>  Tracks  { get; set; } = new();
        // some exports use "masterTrack" instead of "tempo"
        [JsonPropertyName("masterTrack")] public VprTempoBlock? MasterTempo { get; set; }
    }

    // ── importer ──────────────────────────────────────────────────────────────

    public static class VPR {

        // string inside sequence.json that identifies this as a .vpr file
        public const string VprMagic = "\"noteNumber\":";

        private static readonly JsonSerializerOptions _opts = new() {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            AllowTrailingCommas         = true,
            NumberHandling              = JsonNumberHandling.AllowReadingFromString,
        };

        public static UProject Load(string vprPath) {
            Log.Information($"[VPR] loading {vprPath}");

            VprSequence seq;
            using (var zip = ZipFile.OpenRead(vprPath)) {
                var entry = zip.GetEntry("Project/sequence.json")
                         ?? zip.GetEntry("project/sequence.json")
                         ?? FindSequenceEntry(zip);

                if (entry == null)
                    throw new FileFormatException($"[VPR] cannot find sequence.json inside {vprPath}");

                using var stream = entry.Open();
                seq = JsonSerializer.Deserialize<VprSequence>(stream, _opts)
                      ?? throw new FileFormatException("[VPR] sequence.json is empty or invalid");
            }

            return BuildProject(seq, vprPath);
        }

        // ── builder ───────────────────────────────────────────────────────────

        static UProject BuildProject(VprSequence seq, string sourcePath) {
            var project = new UProject();
            Ustx.Ustx.AddDefaultExpressions(project);
            project.FilePath = sourcePath;
            project.name = string.IsNullOrWhiteSpace(seq.Title)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : seq.Title;

            // ── tempos ────────────────────────────────────────────────────────
            project.tempos.Clear();
            var tempoBlock = seq.Tempo ?? seq.MasterTempo;
            if (tempoBlock != null) {
                if (tempoBlock.Global != null && tempoBlock.Events.Count == 0)
                    tempoBlock.Events.Add(tempoBlock.Global);
                foreach (var ev in tempoBlock.Events) {
                    project.tempos.Add(new UTempo {
                        position = ev.Pos,
                        bpm      = ev.Value / 100.0,
                    });
                }
            }
            if (project.tempos.Count == 0)
                project.tempos.Add(new UTempo { position = 0, bpm = 120.0 });
            project.tempos.Sort((a, b) => a.position.CompareTo(b.position));
            project.tempos[0].position = 0;

            // ── time signatures ───────────────────────────────────────────────
            project.timeSignatures.Clear();
            if (seq.TimeSig != null) {
                foreach (var ev in seq.TimeSig.Events) {
                    project.timeSignatures.Add(new UTimeSignature {
                        barPosition = ev.Bar,
                        beatPerBar  = ev.Numer,
                        beatUnit    = ev.Denom,
                    });
                }
            }
            if (project.timeSignatures.Count == 0)
                project.timeSignatures.Add(new UTimeSignature { barPosition = 0, beatPerBar = 4, beatUnit = 4 });
            project.timeSignatures.Sort((a, b) => a.barPosition.CompareTo(b.barPosition));
            project.timeSignatures[0].barPosition = 0;

            // ── tracks + parts ────────────────────────────────────────────────
            project.tracks.Clear();
            var missingVoice = USinger.CreateMissing("");

            foreach (var vTrack in seq.Tracks) {
                if (vTrack.Type != 0) continue; // skip audio tracks

                var uTrack = new UTrack(project) {
                    Singer  = missingVoice,
                    TrackNo = project.tracks.Count,
                    TrackName = string.IsNullOrWhiteSpace(vTrack.Name)
                        ? $"Track{project.tracks.Count + 1}"
                        : vTrack.Name,
                };
                project.tracks.Add(uTrack);

                foreach (var vPart in vTrack.Parts) {
                    var uPart = new UVoicePart {
                        trackNo  = uTrack.TrackNo,
                        position = vPart.Pos,
                        name     = string.IsNullOrWhiteSpace(vPart.Name) ? "Part" : vPart.Name,
                    };

                    foreach (var vNote in vPart.Notes) {
                        if (vNote.Duration <= 0) continue;

                        var uNote = UNote.Create();
                        uNote.position = vNote.Pos;
                        uNote.duration = vNote.Duration;
                        uNote.tone     = vNote.NoteNumber;
                        uNote.lyric    = string.IsNullOrWhiteSpace(vNote.Lyric) ? "a" : vNote.Lyric;
                        // velocity mapped via tuning offset: 0-127 → -50..+50 cents around center
                        // expressions need a live descriptor (set at AfterLoad) so we skip raw exp adds;
                        // the phonemizer / renderer reads lyric + tone only at this stage.
                        uPart.notes.Add(uNote);
                    }

                    // duration: use declared value, floor to cover all notes
                    int notesEnd = 0;
                    foreach (var n in uPart.notes)
                        notesEnd = Math.Max(notesEnd, n.End);
                    uPart.Duration = Math.Max(vPart.Duration > 0 ? vPart.Duration : 0, notesEnd + 480);

                    project.parts.Add(uPart);
                }
            }

            if (project.tracks.Count == 0)
                project.tracks.Add(new UTrack(project) { TrackNo = 0 });

            project.AfterLoad();
            Log.Information($"[VPR] loaded — tracks:{project.tracks.Count} parts:{project.parts.Count}");
            return project;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        static ZipArchiveEntry? FindSequenceEntry(ZipArchive zip) {
            foreach (var e in zip.Entries)
                if (e.Name.Equals("sequence.json", StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }
    }
}
