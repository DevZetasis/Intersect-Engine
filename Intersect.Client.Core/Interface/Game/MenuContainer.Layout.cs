using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;

namespace Intersect.Client.Interface.Game;

public partial class MenuContainer
{
    public override void ProcessAlignments()
    {
        base.ProcessAlignments();
        // Parent resizes realign children even when their own layout is otherwise clean.
        FitButtonsToViewport(this);
    }

    protected override void Layout(Intersect.Client.Framework.Gwen.Skin.Base skin)
    {
        FitButtonsToViewport(this);
        base.Layout(skin);
    }

    protected override void OnJsonReloaded()
    {
        base.OnJsonReloaded();
        FitButtonsToViewport(this);
    }

    // Measure docked widths, not the previous frame's positions. A saved eight-button
    // layout is too narrow after the Monsters button is appended, even after JSON reload.
    internal static void FitButtonsToViewport(Base bar)
    {
        if (bar.Children.OfType<Button>().SingleOrDefault(child => child.Name == "MonstersButton") is { } monsters)
        {
            // Older generated layouts persisted this new text button at 6x6 after
            // recursive SizeToChildren. Repair only this button, preserving existing icons.
            monsters.Font ??= bar.Skin.DefaultFont;
            monsters.FontSize = Math.Max(14, monsters.FontSize);
            monsters.AutoSizeToContents = false;
            var textWidth = bar.Skin.Renderer.MeasureText(monsters.Font, monsters.FontSize, monsters.Text).X;
            monsters.MinimumSize = new Point(Math.Max(80, textWidth + 16), 36);
            monsters.SetSize(Math.Max(monsters.Width, monsters.MinimumSize.X), Math.Max(monsters.Height, 36));
        }
        var buttons = bar.Children.Where(child => child.IsVisibleInParent && child.Dock.HasFlag(Pos.Left)).ToArray();
        if (buttons.Length == 0) return;
        var width = buttons.Sum(child => Math.Max(child.Width, child.MinimumSize.X) + child.Margin.Left + child.Margin.Right)
            + Math.Max(0, buttons.Length - 1) * bar.DockChildSpacing.Left + bar.Padding.Left + bar.Padding.Right;
        var height = buttons.Max(child => Math.Max(child.Height, child.MinimumSize.Y) + child.Margin.Top + child.Margin.Bottom)
            + bar.Padding.Top + bar.Padding.Bottom;
        bar.SetSize(Math.Max(bar.Width, width), Math.Max(bar.Height, height));
        if (bar.Parent is not { } parent) return;
        var left = parent.Padding.Left + bar.Margin.Left + Math.Max(4, bar.AlignmentPadding.Left);
        var top = parent.Padding.Top + bar.Margin.Top + Math.Max(4, bar.AlignmentPadding.Top);
        var right = parent.Width - parent.Padding.Right - bar.Margin.Right - Math.Max(4, bar.AlignmentPadding.Right) - bar.Width;
        var bottom = parent.Height - parent.Padding.Bottom - bar.Margin.Bottom - Math.Max(4, bar.AlignmentPadding.Bottom) - bar.Height;
        bar.MoveTo(Math.Clamp(bar.X, left, Math.Max(left, right)), Math.Clamp(bar.Y, top, Math.Max(top, bottom)));
    }
}
