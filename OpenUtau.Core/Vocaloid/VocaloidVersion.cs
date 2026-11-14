// OpenUtau.Core/Vocaloid/VocaloidVersion.cs
// Lang: C# 10, Target: .NET 7 / Android (OpenUtau Mobile dev branch)
// VOCALOID engine generations (V1..V6) and helpers to recognise them.
//
// Voicebank layout notes:
//  - V2 to V5 voicebanks are a .ddb (samples) and a .ddi (index) sharing one base name,
//    plus a small .vvd info file next to them (seen on V2 to V4). Installed banks live
//    in a folder named after the bank's 16 character component id.
//  - VOCALOID6 plays V3/V4/V5 voicebanks on its "VOCALOID" tracks, so those load
//    like any other bank. The native V6 banks (VOCALOID:AI) are neural, protected
//    and have no public format: they cannot be loaded here.
//  - Nothing inside the files is parsed to tell the generations apart. The version
//    is a label taken from the package manifest or from the bank's own names.

using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenUtau.Core.Vocaloid {

    public enum VocaloidVersion {
        Unknown = 0,
        V1 = 1,
        V2 = 2,
        V3 = 3,
        V4 = 4,
        V5 = 5,
        V6 = 6,
    }

    public static class VocaloidVersions {

        public const string DdbExtension = ".ddb";
        public const string DdiExtension = ".ddi";
        public const string VvdExtension = ".vvd";
        public const string VcldExtension = ".vcld";

        private static readonly Regex longTag = new(
            @"vocaloid\s*([1-6])(?![0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "V3", "v4x", "V6.5" ... but not "V10" or "Vampire".
        private static readonly Regex shortTag = new(
            @"v([1-6])(?:x|\.[0-9]+)?(?![0-9a-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Parses "3", "v3", "V4X", "VOCALOID2", "Vocaloid 5"... Returns Unknown when nothing matches.</summary>
        public static VocaloidVersion Parse(string? text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return VocaloidVersion.Unknown;
            }
            string t = text.Trim();
            if (t.Length == 1 && t[0] >= '1' && t[0] <= '6') {
                return (VocaloidVersion)(t[0] - '0');
            }
            var m = longTag.Match(t);
            if (!m.Success) {
                m = shortTag.Match(t);
            }
            return m.Success ? (VocaloidVersion)(m.Groups[1].Value[0] - '0') : VocaloidVersion.Unknown;
        }

        /// <summary>First hint that names a version wins. Pass the most specific hint first.</summary>
        public static VocaloidVersion Detect(params string?[] hints) {
            foreach (string? hint in hints) {
                var version = Parse(hint);
                if (version != VocaloidVersion.Unknown) {
                    return version;
                }
            }
            return VocaloidVersion.Unknown;
        }

        /// <summary>Reads a manifest value that may be a number (3) or a string ("v3").</summary>
        public static VocaloidVersion FromJson(JsonElement element) {
            switch (element.ValueKind) {
                case JsonValueKind.Number:
                    return element.TryGetInt32(out int n) && n >= 1 && n <= 6
                        ? (VocaloidVersion)n : VocaloidVersion.Unknown;
                case JsonValueKind.String:
                    return Parse(element.GetString());
                default:
                    return VocaloidVersion.Unknown;
            }
        }

        public static string Display(VocaloidVersion version) {
            return version == VocaloidVersion.Unknown ? "VOCALOID" : $"VOCALOID{(int)version}";
        }

        public static string ShortName(VocaloidVersion version) {
            return version == VocaloidVersion.Unknown ? "V?" : $"V{(int)version}";
        }
    }
}
