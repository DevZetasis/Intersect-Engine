using Intersect.Localization;

namespace Intersect.Editor.Localization;

public static partial class Strings
{
    public partial struct MonsterMoveEditor
    {
        public static LocalizedString Menu = "Monster Move Editor";
        public static LocalizedString Id = "Move UUID";
        public static LocalizedString Name = "Move Name";
        public static LocalizedString Description = "Description";
        public static LocalizedString Type = "Elemental Type";
        public static LocalizedString Category = "Damage Category";
        public static LocalizedString Physical = "Physical";
        public static LocalizedString Special = "Special";
        public static LocalizedString Status = "Status";
        public static LocalizedString MaximumPP = "Maximum PP (1–999)";
        public static LocalizedString Power = "Base Power";
        public static LocalizedString Accuracy = "Accuracy (%)";
        public static LocalizedString AlwaysHits = "Always hits (ignore accuracy)";
        public static LocalizedString Priority = "Priority";
        public static LocalizedString Target = "Target";
        public static LocalizedString OneOpponent = "One opponent";
        public static LocalizedString Self = "Self";
        public static LocalizedString OneAlly = "One ally";
        public static LocalizedString AllOpponents = "All opposing monsters";
        public static LocalizedString AllBattlefield = "All monsters on the battlefield";
        public static LocalizedString Animation = "Animation (optional)";
        public static LocalizedString Missing = "Missing reference ({00})";
        public static LocalizedString Invalid = "Enter a name, an existing type, valid category/target, positive damaging power, Maximum PP 1–999, accuracy 1–100, and an existing optional animation.";
        public static LocalizedString DeletePrompt = "Delete this move? Moves referenced by species learnsets or owned monsters cannot be deleted.";
        public static LocalizedString Learnset = "Level Learnset";
        public static LocalizedString LearnLevel = "Learn Level";
        public static LocalizedString Move = "Move";
        public static LocalizedString NewEntry = "New Entry";
        public static LocalizedString Add = "Add";
        public static LocalizedString Remove = "Remove";
        public static LocalizedString LearnsetHelp = "New Entry clears selection. Choose a move/level, then Add. Select a row to edit the draft immediately.";
    }
}
