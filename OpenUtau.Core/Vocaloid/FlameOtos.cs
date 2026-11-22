// OpenUtau.Core/Vocaloid/FlameOtos.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Ayudante compartido por los cantantes integrados (DefaultLoid, AIBot): como no tienen
// oto.ini, el fonema es válido si el catálogo VocaloidPhonemeSet lo conoce, y se devuelve un
// oto sintético para que el editor no lo marque en rojo (misma lógica que VocaloidSinger).

using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vocaloid {

    internal static class FlameOtos {

        private static readonly Dictionary<string, UOto> cache = new Dictionary<string, UOto>(StringComparer.Ordinal);

        public static bool TryGet(string phoneme, out UOto oto) {
            oto = default!;
            if (!VocaloidPhonemeSet.Contains(phoneme)) return false;
            lock (cache) {
                if (!cache.TryGetValue(phoneme, out UOto? cached)) {
                    cached = UOto.OfDummy(phoneme);
                    cache[phoneme] = cached;
                }
                oto = cached;
            }
            return true;
        }
    }
}
