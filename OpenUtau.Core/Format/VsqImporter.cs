// OpenUtau.Core/Format/VsqImporter.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// Imports VOCALOID2 .vsq sequences (and VOCALOID1 files that carry the same text).
//
// A .vsq is a Standard MIDI File. Tempo and time signature are ordinary MIDI meta events;
// notes, lyrics and phonemes live in text meta events ("DM:0000:" + a slice of an INI-like text).
// The sequence is rewritten as a VOCALOID3 .vsqx document and handed to the existing VSQx
// loader, so one parser keeps owning the vsqx schema. Whatever goes wrong while converting,
// the file is imported as a plain MIDI file, which is what happened before this class existed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Format {

    public static class VsqImporter {

        private const string kTextPrefix = "DM:";
        private const int kSniffBytes = 8192;

        // ── detection ─────────────────────────────────────────────────────────

        /// <summary>True for a Standard MIDI File whose first bytes already carry VSQ text.</summary>
        public static bool LooksLikeVsq(string file) {
            try {
                using var stream = File.OpenRead(file);
                var head = new byte[kSniffBytes];
                int count = stream.Read(head, 0, head.Length);
                if (count < 14 || Encoding.ASCII.GetString(head, 0, 4) != "MThd") {
                    return false;
                }
                return Encoding.ASCII.GetString(head, 0, count).Contains("DM:0000:", StringComparison.Ordinal);
            } catch {
                return false;
            }
        }

        // ── import ────────────────────────────────────────────────────────────

        /// <summary>Reads a .vsq. Falls back to the plain MIDI import when it cannot be converted.</summary>
        public static UProject? Load(string file) {
            string? temp = null;
            try {
                byte[]? vsqx = ConvertToVsqx3(file);
                if (vsqx != null) {
                    string dir = Path.Combine(PathManager.Inst.CachePath, "vsq-import");
                    Directory.CreateDirectory(dir);
                    temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".vsqx");
                    File.WriteAllBytes(temp, vsqx);
                    UProject project = VSQx.Load(temp);
                    if (project != null) {
                        // an import is a new, unsaved project: never point it at the temp file
                        if (string.Equals(project.FilePath, temp, StringComparison.OrdinalIgnoreCase)) {
                            project.FilePath = string.Empty;
                        }
                        return project;
                    }
                }
            } catch (Exception ex) {
                Log.Warning(ex, $"[VsqImporter] cannot convert {file}; importing it as plain MIDI");
            } finally {
                if (temp != null) {
                    try { File.Delete(temp); } catch { /* cache file, the OS will clean it */ }
                }
            }
            return MidiWriter.LoadProject(file);
        }

        // ── conversion ────────────────────────────────────────────────────────

        private sealed record MetaEvent(long Tick, byte Type, byte[] Data);
        private sealed record VsqNote(long Tick, long Length, int NoteNumber, int Velocity, string Lyric, string Phonemes);
        private sealed record VsqTrack(string Name, List<VsqNote> Notes);

        /// <summary>The .vsq as UTF-8 vsqx 3 bytes, or null when it holds no notes.</summary>
        internal static byte[]? ConvertToVsqx3(string file) {
            var (division, tracks) = ReadMidi(File.ReadAllBytes(file));
            if (tracks.Count == 0) return null;

            // master track: tempo, time signature, pre-measure
            var master = tracks[0];
            var tempos = master.Where(e => e.Type == 0x51 && e.Data.Length == 3)
                .Select(e => (e.Tick, Micros: (e.Data[0] << 16) | (e.Data[1] << 8) | e.Data[2]))
                .Where(t => t.Micros > 0).ToList();
            if (tempos.Count == 0 || tempos[0].Tick != 0) tempos.Insert(0, (0, 500000));
            var signatures = master.Where(e => e.Type == 0x58 && e.Data.Length >= 2)
                .Select(e => (e.Tick, Numerator: (int)e.Data[0], Denominator: 1 << Math.Min((int)e.Data[1], 6)))
                .Where(s => s.Numerator > 0).ToList();
            if (signatures.Count == 0 || signatures[0].Tick != 0) signatures.Insert(0, (0, 4, 4));

            int preMeasure = 1;
            var masterSections = ParseSections(ReadVsqText(master));
            if (TryGetInt(masterSections, "Master", "PreMeasure", out int pm) && pm >= 0) preMeasure = pm;

            long measureTicks = MeasureTicks(division, signatures[0].Numerator, signatures[0].Denominator);
            long partStart = preMeasure * measureTicks;

            var vocalTracks = new List<VsqTrack>();
            for (int i = 1; i < tracks.Count; i++) {
                var sections = ParseSections(ReadVsqText(tracks[i]));
                var notes = ReadNotes(sections);
                if (notes.Count == 0) continue;
                string name = sections.TryGetValue("Common", out var common) ? Get(common, "Name") : string.Empty;
                if (name.Length == 0) name = $"Track{vocalTracks.Count + 1}";
                vocalTracks.Add(new VsqTrack(name, notes));
            }
            if (vocalTracks.Count == 0) return null;

            return WriteVsqx3(division, preMeasure, partStart, tempos, signatures, vocalTracks);
        }

        private static long MeasureTicks(int division, int numerator, int denominator) {
            return Math.Max(1, (long)division * 4 * numerator / denominator);
        }

        // ── VSQ text ──────────────────────────────────────────────────────────

        private static string ReadVsqText(List<MetaEvent> events) {
            var chunks = new List<(int Sequence, byte[] Payload)>();
            foreach (var e in events) {
                // "DM:0000:" + payload; the four digits order the slices
                if (e.Type != 0x01 || e.Data.Length < 8 || e.Data[7] != (byte)':') continue;
                if (Encoding.ASCII.GetString(e.Data, 0, 3) != kTextPrefix) continue;
                if (!int.TryParse(Encoding.ASCII.GetString(e.Data, 3, 4), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int sequence)) continue;
                chunks.Add((sequence, e.Data.AsSpan(8).ToArray()));
            }
            if (chunks.Count == 0) return string.Empty;
            // join the raw bytes first: a multi-byte character can straddle two slices
            byte[] bytes = chunks.OrderBy(c => c.Sequence).SelectMany(c => c.Payload).ToArray();
            try {
                return new UTF8Encoding(false, true).GetString(bytes);
            } catch (ArgumentException) {
                try {
                    return Encoding.GetEncoding(932).GetString(bytes); // Shift-JIS, what Japanese editors write
                } catch (Exception) {
                    return Encoding.Latin1.GetString(bytes);
                }
            }
        }

        private static Dictionary<string, List<KeyValuePair<string, string>>> ParseSections(string text) {
            var sections = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
            List<KeyValuePair<string, string>>? current = null;
            foreach (string raw in text.Split('\n')) {
                string line = raw.Trim('\r', ' ', '\t', '\0');
                if (line.Length == 0) continue;
                if (line[0] == '[' && line[^1] == ']') {
                    current = new List<KeyValuePair<string, string>>();
                    sections[line[1..^1]] = current;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (current == null || eq <= 0) continue;
                current.Add(new KeyValuePair<string, string>(line[..eq], line[(eq + 1)..]));
            }
            return sections;
        }

        private static string Get(List<KeyValuePair<string, string>> section, string key) {
            foreach (var kv in section) {
                if (kv.Key == key) return kv.Value;
            }
            return string.Empty;
        }

        private static bool TryGetInt(Dictionary<string, List<KeyValuePair<string, string>>> sections,
                                      string section, string key, out int value) {
            value = 0;
            return sections.TryGetValue(section, out var s)
                && int.TryParse(Get(s, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        // [EventList] maps a tick to "ID#nnnn"; [ID#nnnn] holds the note, its LyricHandle points at [h#nnnn].
        private static List<VsqNote> ReadNotes(Dictionary<string, List<KeyValuePair<string, string>>> sections) {
            var notes = new List<VsqNote>();
            if (!sections.TryGetValue("EventList", out var events)) return notes;
            foreach (var entry in events) {
                if (!long.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long tick)) continue;
                if (!sections.TryGetValue(entry.Value, out var id) || Get(id, "Type") != "Anote") continue;
                if (!long.TryParse(Get(id, "Length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long length)
                    || length <= 0) continue;
                if (!int.TryParse(Get(id, "Note#"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int noteNumber)) continue;
                if (!int.TryParse(Get(id, "Dynamics"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int velocity)) {
                    velocity = 64;
                }

                string lyric = "a";
                string phonemes = "a";
                if (sections.TryGetValue(Get(id, "LyricHandle"), out var handle)) {
                    // L0="lyric","phonemes",protect,...
                    var m = Regex.Match(Get(handle, "L0"), "^\"(.*?)\",\"(.*?)\"");
                    if (m.Success) {
                        lyric = m.Groups[1].Value;
                        phonemes = m.Groups[2].Value;
                    }
                }
                notes.Add(new VsqNote(tick, length, Math.Clamp(noteNumber, 0, 127), Math.Clamp(velocity, 0, 127), lyric, phonemes));
            }
            return notes.OrderBy(n => n.Tick).ToList();
        }

        // ── MIDI ──────────────────────────────────────────────────────────────

        private static (int Division, List<List<MetaEvent>> Tracks) ReadMidi(byte[] data) {
            if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 4) != "MThd") {
                throw new InvalidDataException("Not a Standard MIDI File.");
            }
            int headerLength = ReadInt(data, 4, 4);
            int division = ReadInt(data, 12, 2);
            if (division == 0 || (division & 0x8000) != 0) {
                throw new InvalidDataException("SMPTE time division is not used by VSQ files.");
            }

            var tracks = new List<List<MetaEvent>>();
            int pos = 8 + headerLength;
            while (pos + 8 <= data.Length) {
                string tag = Encoding.ASCII.GetString(data, pos, 4);
                int length = ReadInt(data, pos + 4, 4);
                pos += 8;
                int end = length < 0 ? data.Length : (int)Math.Min((long)data.Length, (long)pos + length);
                if (tag == "MTrk") tracks.Add(ReadTrack(data, pos, end));
                pos = end;
            }
            return (division, tracks);
        }

        private static List<MetaEvent> ReadTrack(byte[] data, int pos, int end) {
            var metas = new List<MetaEvent>();
            long tick = 0;
            int status = 0;
            while (pos < end) {
                tick += ReadVariable(data, ref pos, end);
                if (pos >= end) break;
                int first = data[pos];
                if (first == 0xFF) {
                    byte type = data[pos + 1];
                    pos += 2;
                    int length = (int)ReadVariable(data, ref pos, end);
                    if (length < 0 || pos + length > end) break;
                    metas.Add(new MetaEvent(tick, type, data.AsSpan(pos, length).ToArray()));
                    pos += length;
                    if (type == 0x2F) break; // end of track
                } else if (first == 0xF0 || first == 0xF7) {
                    pos++;
                    int length = (int)ReadVariable(data, ref pos, end);
                    pos += Math.Max(0, length);
                    status = 0;
                } else {
                    if ((first & 0x80) != 0) {
                        status = first;
                        pos++;
                    } // else running status: the byte is already data
                    int kind = status & 0xF0;
                    pos += (kind == 0xC0 || kind == 0xD0) ? 1 : 2;
                }
            }
            return metas;
        }

        private static long ReadVariable(byte[] data, ref int pos, int end) {
            long value = 0;
            for (int i = 0; i < 4 && pos < end; i++) {
                byte b = data[pos++];
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return value;
            }
            return value;
        }

        private static int ReadInt(byte[] data, int pos, int bytes) {
            int value = 0;
            for (int i = 0; i < bytes; i++) value = (value << 8) | data[pos + i];
            return value;
        }

        // ── vsqx 3 ────────────────────────────────────────────────────────────

        // XML 1.0 rejects most control characters; a stray one in a lyric must not sink the import.
        private static string XmlSafe(string text) {
            return new string(text.Where(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c)).ToArray());
        }

        private static byte[] WriteVsqx3(int division, int preMeasure, long partStart,
                                         List<(long Tick, int Micros)> tempos,
                                         List<(long Tick, int Numerator, int Denominator)> signatures,
                                         List<VsqTrack> tracks) {
            string ns = VSQx.vsq3NameSpace;
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using var stream = new MemoryStream();
            using (var w = XmlWriter.Create(stream, settings)) {
                void Start(string name) => w.WriteStartElement(name, ns);
                void Text(string name, object value) {
                    Start(name);
                    w.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
                void Cdata(string name, string value) {
                    Start(name);
                    w.WriteCData(XmlSafe(value).Replace("]]>", "]] >"));
                    w.WriteEndElement();
                }
                void Attr(string id, string value) {
                    Start("attr");
                    w.WriteAttributeString("id", id);
                    w.WriteString(value);
                    w.WriteEndElement();
                }

                w.WriteStartDocument();
                Start("vsq3");
                Cdata("vender", "Yamaha corporation");
                Cdata("version", "3.0.0.11");

                Start("mixer");
                Start("masterUnit");
                Text("outDev", 0); Text("retLevel", 0); Text("vol", 0);
                w.WriteEndElement();
                for (int i = 0; i < tracks.Count; i++) {
                    Start("vsUnit");
                    Text("vsTrackNo", i); Text("inGain", 0); Text("sendLevel", -898); Text("sendEnable", 0);
                    Text("mute", 0); Text("solo", 0); Text("pan", 64); Text("vol", 0);
                    w.WriteEndElement();
                }
                w.WriteEndElement(); // mixer

                Start("masterTrack");
                Cdata("seqName", "Imported VSQ");
                Cdata("comment", "Imported from a VOCALOID2 .vsq");
                Text("resolution", division);
                Text("preMeasure", preMeasure);
                long measureTicks = MeasureTicks(division, signatures[0].Numerator, signatures[0].Denominator);
                long previousTick = 0;
                long previousMeasure = 0;
                long previousLength = measureTicks;
                foreach (var s in signatures) {
                    long measure = previousMeasure + (s.Tick - previousTick) / previousLength;
                    Start("timeSig");
                    Text("posMes", measure); Text("nume", s.Numerator); Text("denomi", s.Denominator);
                    w.WriteEndElement();
                    previousTick = s.Tick;
                    previousMeasure = measure;
                    previousLength = MeasureTicks(division, s.Numerator, s.Denominator);
                }
                foreach (var t in tempos) {
                    Start("tempo");
                    Text("posTick", t.Tick);
                    Text("bpm", (long)Math.Round(60_000_000.0 / t.Micros * 100.0)); // bpm * 100
                    w.WriteEndElement();
                }
                w.WriteEndElement(); // masterTrack

                for (int i = 0; i < tracks.Count; i++) {
                    var track = tracks[i];
                    long end = track.Notes.Max(n => n.Tick + n.Length);
                    Start("vsTrack");
                    Text("vsTrackNo", i);
                    Cdata("trackName", track.Name);
                    Cdata("comment", track.Name);
                    Start("musicalPart");
                    Text("posTick", partStart);
                    Text("playTime", Math.Max(1, end - partStart));
                    Cdata("partName", track.Name);
                    Cdata("comment", "Imported part");
                    Start("stylePlugin");
                    Cdata("stylePluginID", "ACA9C502-A04B-42b5-B2EB-5CEA36D16FCE");
                    Cdata("stylePluginName", "VOCALOID2 Compatible Style");
                    Cdata("version", "3.0.0.1");
                    w.WriteEndElement();
                    Start("partStyle");
                    Attr("accent", "50"); Attr("bendDep", "8"); Attr("bendLen", "0"); Attr("decay", "50");
                    Attr("fallPort", "0"); Attr("opening", "127"); Attr("risePort", "0");
                    w.WriteEndElement();
                    Start("singer");
                    Text("posTick", 0); Text("vBS", 0); Text("vPC", 0);
                    w.WriteEndElement();
                    foreach (var note in track.Notes) {
                        Start("note");
                        Text("posTick", Math.Max(0, note.Tick - partStart));
                        Text("durTick", note.Length);
                        Text("noteNum", note.NoteNumber);
                        Text("velocity", note.Velocity);
                        Cdata("lyric", note.Lyric);
                        Cdata("phnms", note.Phonemes);
                        Start("noteStyle");
                        Attr("accent", "50"); Attr("bendDep", "8"); Attr("bendLen", "0"); Attr("decay", "50");
                        Attr("fallPort", "false"); Attr("opening", "127"); Attr("risePort", "false");
                        Attr("vibLen", "66"); Attr("vibType", "0");
                        w.WriteEndElement();
                        w.WriteEndElement(); // note
                    }
                    w.WriteEndElement(); // musicalPart
                    w.WriteEndElement(); // vsTrack
                }

                w.WriteEndElement(); // vsq3
                w.WriteEndDocument();
            }
            return stream.ToArray();
        }
    }
}
