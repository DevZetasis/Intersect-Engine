using Intersect.Localization;

namespace Intersect.Editor.Localization;

public static partial class Strings
{
    public partial struct MonsterNatureEditor
    {
        public static LocalizedString Menu = "Monster Nature Editor";
        public static LocalizedString Id = "Nature UUID";
        public static LocalizedString Name = "Nature Name";
        public static LocalizedString Description = "Description";
        public static LocalizedString IncreasedStat = "Increased Stat";
        public static LocalizedString DecreasedStat = "Decreased Stat";
        public static LocalizedString IncreasePercentage = "Increase (%)";
        public static LocalizedString DecreasePercentage = "Decrease (%)";
        public static LocalizedString SelectionWeight = "Selection Weight";
        public static LocalizedString Attack = "Attack";
        public static LocalizedString Defense = "Defense";
        public static LocalizedString SpecialAttack = "Special Attack";
        public static LocalizedString SpecialDefense = "Special Defense";
        public static LocalizedString Speed = "Speed";
        public static LocalizedString Preview = "Definition Preview";
        public static LocalizedString NatureSummary = "Nature: {00}";
        public static LocalizedString IncreaseSummary = "{00}: +{01}%";
        public static LocalizedString DecreaseSummary = "{00}: -{01}%";
        public static LocalizedString WeightSummary = "Selection Weight: {00}";
        public static LocalizedString Neutral = "Neutral (no stat modifiers)";
        public static LocalizedString Excluded = "Excluded from future random nature selection.";
        public static LocalizedString WeightHelp = "Relative weight, not a percentage or encounter rate. Default 100; 0 excludes this nature from future random selection. Range: 0–100000.";
        public static LocalizedString PercentageHelp = "Magnitude 0–50%. Increase/decrease determines direction. None ignores this magnitude.";
        public static LocalizedString Conflict = "A nature cannot increase and decrease the same stat. Choose different stats or None.";
        public static LocalizedString Invalid = "Enter a name, non-HP stats, percentage magnitudes 0–50, and an integer selection weight 0–100000.";
        public static LocalizedString DeletePrompt = "Permanently delete this nature? This cannot be canceled.";
    }
}
