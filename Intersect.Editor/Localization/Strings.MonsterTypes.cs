using Intersect.Localization;

namespace Intersect.Editor.Localization;

public static partial class Strings
{
    public partial struct MonsterTypeEditor
    {
        public static LocalizedString Menu = "Monster Type Editor";
        public static LocalizedString Id = "Type UUID";
        public static LocalizedString Name = "Type Name";
        public static LocalizedString Description = "Description";
        public static LocalizedString Color = "Display Color";
        public static LocalizedString ChooseColor = "Choose...";
        public static LocalizedString Direction = "This type ATTACKS the defending types below. Unset relationships are neutral.";
        public static LocalizedString Effectiveness = "Defending type";
        public static LocalizedString Missing = "Missing type ({00})";
        public static LocalizedString Immune = "0× — Immune";
        public static LocalizedString Resistant = "0.5× — Resistant";
        public static LocalizedString Neutral = "1× — Neutral";
        public static LocalizedString Weak = "2× — Weak";
        public static LocalizedString Invalid = "Enter a name, a #RRGGBB color, and valid effectiveness relationships.";
        public static LocalizedString DeletePrompt = "Delete this type? Types referenced by species or other types cannot be deleted.";
    }
}
