using Intersect.Localization;

namespace Intersect.Client.Localization;

public static partial class Strings
{
    public partial struct Monsters
    {
        public static LocalizedString Title = "Monsters";
        public static LocalizedString Refresh = "Refresh";
        public static LocalizedString Move = "Store / Retrieve";
        public static LocalizedString Store = "Store in storage";
        public static LocalizedString Retrieve = "Add to party";
        public static LocalizedString Level = "Lv. {00}";
        public static LocalizedString Placement = "{00} #{01}";
        public static LocalizedString Waiting = "Loading your monsters...";
        public static LocalizedString Counts = "Party: {00}/{01}   Stored: {02}";
        public static LocalizedString Entry = "{00} #{01}: {02} — {03} — Lv. {04}";
        public static LocalizedString Party = "Party";
        public static LocalizedString Storage = "Storage";
        public static LocalizedString MissingSpecies = "Missing species ({00})";
    }
}
