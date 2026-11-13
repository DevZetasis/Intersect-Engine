namespace Intersect.Framework.Core.GameObjects.Monsters;
public enum MonsterHealingScope { Party = 0, AllOwned = 1 }
[Flags] public enum MonsterRecovery { HP = 1, PP = 2, Both = HP | PP, Status = 4, All = Both | Status }
