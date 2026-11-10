using Intersect.Client.Framework.Gwen.Control;

namespace Intersect.Client.Interface.Game.Monsters;

/// <summary>Original primitive landscape; custom images are drawn above this safe default.</summary>
internal sealed class MonsterBattleScene(Base parent) : Panel(parent, "BattleScene")
{
    protected override void Render(Intersect.Client.Framework.Gwen.Skin.Base skin)
    {
        skin.DrawRectFill(new(0, 0, Width, Height), new Color(255, 184, 215, 222));
        skin.DrawRectFill(new(0, Height * 43 / 100, Width, Height), new Color(255, 109, 150, 128));
        void Ground(int x, int y, int w, int h)
        {
            for (var row = 0; row < Math.Max(1, h); row += 2)
            {
                var extent = (int)(w * Math.Sqrt(Math.Max(0, 1 - Math.Pow(2d * row / Math.Max(1, h) - 1, 2))));
                skin.DrawRectFill(new(x + (w - extent) / 2, y + row, extent, 2), new Color(255, 204, 201, 154));
            }
        }
        Ground(Width * 57 / 100, Height * 38 / 100, Width * 42 / 100, Height * 13 / 100);
        Ground(0, Height * 83 / 100, Width * 49 / 100, Height * 16 / 100);
    }
}
