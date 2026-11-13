using Intersect.Localization;

namespace Intersect.Editor.Localization;

public static partial class Strings
{
    public partial struct MonsterMoveEditor
    {
        public static LocalizedString InvalidEffects =
            "Check move effects. Use One Opponent for damaging moves with effects, and Self or One Opponent for Status moves. Self moves can only affect the user.";
        public static LocalizedString Effects = "Move effects";
        public static LocalizedString StatStage = "Modify stat stage";
        public static LocalizedString RestoreHP = "Restore HP";
        public static LocalizedString FixedHP = "Fixed HP";
        public static LocalizedString PercentHP = "% of maximum HP";
        public static LocalizedString EffectKind = "Effect";
        public static LocalizedString EffectTarget = "Recipient";
        public static LocalizedString EffectSelf = "Self";
        public static LocalizedString EffectOpponent = "Opponent";
        public static LocalizedString EffectStat = "Stat";
        public static LocalizedString HealingMethod = "Healing method";
        public static LocalizedString EffectAmount = "Amount";
        public static LocalizedString AddEffect = "Add effect";
        public static LocalizedString RemoveEffect = "Remove";
        public static LocalizedString AnimationPosition = "Effect position";
        public static LocalizedString AnimationTarget = "Target";
        public static LocalizedString AnimationAttacker = "Attacker";
        public static LocalizedString AnimationBoth = "Both";
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
