using Intersect.Localization;

namespace Intersect.Client.Localization;

public static partial class Strings
{
    public partial struct MonsterBattle
    {
        public static LocalizedString Title = "Monster Practice Battle";
        public static LocalizedString Forfeit = "Forfeit";
        public static LocalizedString Return = "Return to game";
        public static LocalizedString Turn = "Turn {00} \u2014 choose a command";
        public static LocalizedString Result = "Practice result: {00}";
        public static LocalizedString Actor = "{00} \u2014 Level {01}";
        public static LocalizedString Fight = "Fight";
        public static LocalizedString Bag = "Bag (unavailable)";
        public static LocalizedString Monsters = "Monsters (unavailable)";
        public static LocalizedString Run = "Run (unavailable)";
        public static LocalizedString Back = "Back";
        public static LocalizedString Next = "Next";
        public static LocalizedString Skip = "Skip playback";
        public static LocalizedString Unavailable = "Not implemented in practice battles.";
        public static LocalizedString NoArt = "Artwork unavailable";
        public static LocalizedString RearFallback = "Back artwork unavailable: overworld idle frame shown.";
        public static LocalizedString Experience = "EXP - rewards not implemented";
        public static LocalizedString Empty = "Empty move slot";
        public static LocalizedString Waiting = "Waiting for the server...";
    }
}
