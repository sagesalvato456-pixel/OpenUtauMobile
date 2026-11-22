// OpenUtau.Core/Vocaloid/FlameVoiceProfile.cs
// Lang: C# 10+, Target: .NET 8 / Android (OpenLoid-Flame)
//
// Perfil descriptivo de las voces integradas de Flame. DefaultLoidSinger.Profile lo pide
// (FlameVoiceProfile.DefaultLoid) y no existía, por eso fallaba el build con CS0246.
// Es solo datos: el sonido real sale de FlameVoice / AIBotVoice.

namespace OpenUtau.Core.Vocaloid {

    internal sealed class FlameVoiceProfile {

        public string Name { get; }
        public int Age { get; }
        public string Personality { get; }
        public string Mode { get; }

        public FlameVoiceProfile(string name, int age, string personality, string mode) {
            Name = name;
            Age = age;
            Personality = personality;
            Mode = mode;
        }

        public static readonly FlameVoiceProfile DefaultLoid =
            new FlameVoiceProfile("DefaultLoid", 16, "Default", "NPC");

        public static readonly FlameVoiceProfile AIBot =
            new FlameVoiceProfile("AIBot", 17, "Hard", "Bot");
    }
}
