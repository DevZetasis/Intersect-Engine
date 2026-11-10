namespace Intersect.Server.Entities;

public partial class Player
{
    internal bool IsAvailableForMonsterPractice => InGame && Client != null && !IsDead && !IsBusy();
}
