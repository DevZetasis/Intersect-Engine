using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Config;
using Intersect.Editor.General;
using Intersect.Framework.Core.GameObjects.Maps;
using Intersect.Framework.Core.GameObjects.Monsters;
using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;
using Graphics = Intersect.Editor.Core.Graphics;

namespace Intersect.Editor.Forms.DockingElements;

internal sealed class MapEncounterEditor : DarkForm
{
    private readonly record struct TableChoice(Guid Id, string Text)
    {
        public override string ToString() => Text;
    }

    private enum BrushMode { Table, Safe, Clear }

    private sealed class TilePreview(int width, int height, Bitmap? art, MapEncounters draft) : Control
    {
        public Func<int, int, string>? Describe { get; init; }
        public Action<int, int, bool>? PaintTile { get; init; }
        private readonly ToolTip _tip = new();
        private Point? _lastPaint;
        private Point? _lastHover;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var tileWidth = ClientSize.Width / (float)width;
            var tileHeight = ClientSize.Height / (float)height;
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (art != null) e.Graphics.DrawImage(art, ClientRectangle);
            using var safe = new SolidBrush(DrawingColor.FromArgb(165, DrawingColor.DeepSkyBlue));
            using var encounter = new SolidBrush(DrawingColor.FromArgb(155, DrawingColor.Orange));
            using var grid = new Pen(DrawingColor.FromArgb(100, DrawingColor.White));
            foreach (var (key, tile) in draft.Tiles)
            {
                var x = key & 0xffff;
                var y = key >> 16;
                if (x >= width || y < 0 || y >= height) continue;
                e.Graphics.FillRectangle(tile == null || tile.TableId == Guid.Empty ? safe : encounter,
                    x * tileWidth, y * tileHeight, tileWidth, tileHeight);
            }
            for (var x = 1; x < width; x++) e.Graphics.DrawLine(grid, x * tileWidth, 0, x * tileWidth, ClientSize.Height);
            for (var y = 1; y < height; y++) e.Graphics.DrawLine(grid, 0, y * tileHeight, ClientSize.Width, y * tileHeight);
        }

        private Point? TileAt(MouseEventArgs e) => ClientRectangle.Contains(e.Location) && ClientSize.Width > 0 && ClientSize.Height > 0
            ? new Point(e.X * width / ClientSize.Width, e.Y * height / ClientSize.Height)
            : null;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _lastPaint = null;
            Apply(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button is MouseButtons.Left or MouseButtons.Right) Apply(e);
            else if (TileAt(e) is { } point && point != _lastHover)
            {
                _lastHover = point;
                _tip.SetToolTip(this, Describe?.Invoke(point.X, point.Y));
            }
        }

        private void Apply(MouseEventArgs e)
        {
            if (e.Button is not (MouseButtons.Left or MouseButtons.Right) || TileAt(e) is not { } point || point == _lastPaint)
                return;
            if (_lastPaint is { } previous)
            {
                var steps = Math.Max(Math.Abs(point.X - previous.X), Math.Abs(point.Y - previous.Y));
                for (var step = 1; step < steps; step++)
                    PaintTile?.Invoke(previous.X + (point.X - previous.X) * step / steps,
                        previous.Y + (point.Y - previous.Y) * step / steps, e.Button == MouseButtons.Right);
            }
            PaintTile?.Invoke(point.X, point.Y, e.Button == MouseButtons.Right);
            _lastPaint = point;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    public static void Open(IWin32Window parent)
    {
        if (Globals.CurrentMap is not { } map) return;
        Bitmap? art = null;
        if (Globals.MapGrid?.Loaded == true)
        {
            lock (Graphics.GraphicsLock) art = Graphics.ScreenShotMap();
        }
        using (art)
        using (var editor = new MapEncounterEditor(map, art))
        {
            if (editor.ShowDialog(parent) != DialogResult.OK || Globals.CurrentMap != map) return;
            Globals.MapEditorWindow.PrepUndoState();
            map.Encounters = editor._draft;
            Globals.MapEditorWindow.AddUndoState();
            Graphics.TilePreviewUpdated = true;
        }
    }

    private readonly MapEncounters _draft;

    internal MapEncounterEditor(MapDescriptor map, Bitmap? art)
    {
        _draft = map.Encounters?.Copy() ?? new();
        var width = Options.Instance.Map.MapWidth;
        var height = Options.Instance.Map.MapHeight;
        Text = $"Map Encounters â€” {map.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 740);
        MinimumSize = new Size(625, 450);
        BackColor = DrawingColor.FromArgb(45, 45, 48);
        ForeColor = DrawingColor.Gainsboro;

        var choices = MonsterEncounterTableDescriptor.Lookup.Values.Cast<MonsterEncounterTableDescriptor>().OrderBy(table => table.Name)
            .Select(table => new TableChoice(table.Id, table.Enabled ? table.Name : table.Name + " (disabled)")).ToList();
        foreach (var id in _draft.Tiles.Values.Select(tile => tile?.TableId ?? Guid.Empty).Append(_draft.DefaultTableId).Distinct())
            if (id != Guid.Empty && choices.All(choice => choice.Id != id)) choices.Add(new(id, $"Missing ({id})"));

        var mode = List("Disabled", "Painted only", "Whole map");
        mode.SelectedIndex = Enum.IsDefined(_draft.Mode) ? (int)_draft.Mode : 0;
        var defaultTable = List(choices.Prepend(new(Guid.Empty, "None")).Cast<object>().ToArray());
        defaultTable.SelectedItem = defaultTable.Items.Cast<TableChoice>().First(choice => choice.Id == _draft.DefaultTableId);
        var mapRate = Rate(_draft.Rate);
        var brush = List("Encounter table", "No Encounter", "Clear override");
        var brushTable = List(choices.Cast<object>().ToArray());
        var brushRate = Rate(null);
        var legend = new DarkLabel { Text = "Orange: table override   Blue: No Encounter   Right-drag: clear override", AutoSize = true };
        var coordinate = new DarkLabel { Text = "Hover over a tile to see its coordinates and assignment.", AutoSize = true };

        string DescribeTile(int x, int y)
        {
            var tile = _draft.Tiles.GetValueOrDefault(MapEncounters.TileKey(x, y));
            var name = tile == null ? "Inherit map settings" : tile.TableId == Guid.Empty ? "No Encounter"
                : choices.FirstOrDefault(choice => choice.Id == tile.TableId).Text ?? "Missing table";
            coordinate.Text = $"Tile ({x}, {y})  {name}" + (tile?.Rate is int rate ? $" ({rate}%)" : "");
            return coordinate.Text;
        }

        var preview = new TilePreview(width, height, art, _draft)
        {
            Size = new Size(Math.Max(16, Math.Min(18, 610 / width)) * width,
                Math.Max(16, Math.Min(18, 540 / height)) * height),
            BackColor = DrawingColor.FromArgb(29, 32, 35),
            Describe = DescribeTile,
            PaintTile = (x, y, erase) =>
            {
                var key = MapEncounters.TileKey(x, y);
                var action = erase ? BrushMode.Clear : (BrushMode)brush.SelectedIndex;
                if (action == BrushMode.Clear) _draft.Tiles.Remove(key);
                else if (action == BrushMode.Safe) _draft.Tiles[key] = new(Guid.Empty);
                else if (brushTable.SelectedItem is TableChoice selected) _draft.Tiles[key] = new(selected.Id, ReadRate(brushRate));
                DescribeTile(x, y);
            },
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
        var apply = new DarkButton { Text = "Apply", Width = 95 };
        var cancel = new DarkButton { Text = "Cancel", Width = 95, DialogResult = DialogResult.Cancel };
        apply.Click += (_, _) =>
        {
            _draft.Mode = (MapEncounterMode)mode.SelectedIndex;
            _draft.DefaultTableId = ((TableChoice)defaultTable.SelectedItem).Id;
            _draft.Rate = ReadRate(mapRate);
            if (!_draft.IsValid(width, height, id => MonsterEncounterTableDescriptor.Get(id) != null))
            {
                DarkMessageBox.ShowWarning("Choose a default table for whole-map mode and replace or clear missing table assignments.", Text);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        bottom.Controls.Add(apply);
        bottom.Controls.Add(cancel);
        CancelButton = cancel;

        var controls = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(6) };
        void Row(string left, Control leftControl, string right, Control rightControl, int row)
        {
            controls.Controls.Add(new DarkLabel { Text = left, AutoSize = true }, 0, row);
            controls.Controls.Add(leftControl, 1, row);
            controls.Controls.Add(new DarkLabel { Text = right, AutoSize = true }, 2, row);
            controls.Controls.Add(rightControl, 3, row);
        }
        Row("Map mode", mode, "Default table", defaultTable, 0);
        Row("Map rate (-1 = table)", mapRate, "Paint brush", brush, 1);
        Row("Tile table", brushTable, "Tile rate (-1 = table)", brushRate, 2);
        controls.Controls.Add(legend, 0, 3);
        controls.SetColumnSpan(legend, 4);
        controls.Controls.Add(coordinate, 0, 4);
        controls.SetColumnSpan(coordinate, 4);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        scroll.Controls.Add(preview);
        Controls.Add(scroll);
        Controls.Add(controls);
        Controls.Add(bottom);
    }

    private static DarkComboBox List(params object[] items)
    {
        var control = new DarkComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        control.Items.AddRange(items);
        if (items.Length > 0) control.SelectedIndex = 0;
        return control;
    }

    private static DarkNumericUpDown Rate(int? value) => new()
    {
        Minimum = -1, Maximum = 100, Value = Math.Clamp(value ?? -1, -1, 100), Width = 80,
    };

    private static int? ReadRate(DarkNumericUpDown field) => field.Value < 0 ? null : (int)field.Value;
}
