using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Config;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterExperienceCurve : EditorForm
{
    private const GameObjectType ObjectType = GameObjectType.MonsterExperienceCurve;
    private readonly GameObjectList _list = new() { Dock = DockStyle.Fill };
    private readonly DarkTextBox _name = new() { Dock = DockStyle.Top };
    private readonly DarkTextBox _folder = new() { Dock = DockStyle.Top };
    private readonly DarkTextBox _thresholds = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly DarkComboBox _preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Dictionary<Guid, Draft> _drafts = [];
    private Draft? _selected;
    private Draft? _copied;
    private bool _populating;

    private sealed record Draft(Guid Id, string Name, string Folder, string Thresholds, string Original);

    public FrmMonsterExperienceCurve()
    {
        Text = "Monster Experience Curve Editor";
        Size = new Size(770, 570);
        MinimumSize = new Size(630, 450);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;

        var side = new Panel { Dock = DockStyle.Left, Width = 240, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        _list.Init(() => { }, SelectCurve, Create, Copy, Undo, Paste, DeleteCurve);
        AddButton(tools, "New", Create);
        AddButton(tools, "Copy", Copy);
        AddButton(tools, "Paste", Paste);
        AddButton(tools, "Undo", Undo);
        AddButton(tools, "Delete", DeleteCurve);
        side.Controls.Add(_list);
        side.Controls.Add(tools);

        var editor = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var presets = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        _preset.Items.AddRange(["Fast", "Medium Fast", "Medium Slow", "Slow"]);
        _preset.SelectedIndex = 1;
        presets.Controls.Add(_preset);
        AddButton(presets, "Copy preset thresholds", PopulatePreset);
        editor.Controls.Add(_thresholds);
        editor.Controls.Add(new DarkLabel { Text = "Cumulative EXP, one line per level starting at level 1", Dock = DockStyle.Top, Height = 24 });
        editor.Controls.Add(presets);
        editor.Controls.Add(_folder);
        editor.Controls.Add(new DarkLabel { Text = "Folder", Dock = DockStyle.Top, Height = 24 });
        editor.Controls.Add(_name);
        editor.Controls.Add(new DarkLabel { Text = "Name", Dock = DockStyle.Top, Height = 24 });

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft };
        _btnCancel = AddButton(actions, "Cancel", (_, _) => Close());
        _btnSave = AddButton(actions, "Save", SaveCurves);
        Controls.Add(editor);
        Controls.Add(side);
        Controls.Add(actions);
        FormClosed += (_, _) => Globals.CurrentEditor = -1;
    }

    private static DarkButton AddButton(Control parent, string caption, EventHandler click)
    {
        var button = new DarkButton { Text = caption, AutoSize = true, MinimumSize = new Size(60, 26) };
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }

    private static Draft Read(MonsterExperienceCurveDescriptor curve) =>
        new(curve.Id, curve.Name, curve.Folder, string.Join(Environment.NewLine, curve.Thresholds), curve.JsonData);

    public void InitEditor()
    {
        SaveDraft();
        var catalog = MonsterExperienceCurveDescriptor.Lookup.Values.Cast<MonsterExperienceCurveDescriptor>().ToArray();
        foreach (var id in _drafts.Keys.Where(id => MonsterExperienceCurveDescriptor.Get(id) == null).ToArray())
            _drafts.Remove(id);
        if (_selected != null && MonsterExperienceCurveDescriptor.Get(_selected.Id) == null) _selected = null;
        var entries = catalog.Select(curve => _drafts.GetValueOrDefault(curve.Id) ?? Read(curve)).OrderBy(draft => draft.Name).ToArray();
        _populating = true;
        try
        {
            _list.Repopulate(entries.Select(draft => new KeyValuePair<Guid, KeyValuePair<string, string>>(draft.Id,
                new(draft.Name, draft.Folder))).ToArray(), entries.Select(draft => draft.Folder)
                .Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        }
        finally { _populating = false; }
        if (_selected == null && entries.Length > 0) SelectCurve(entries[0].Id);
        else ShowDraft();
    }

    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type == ObjectType) InitEditor();
    }

    private void SelectCurve(Guid id)
    {
        if (_populating || MonsterExperienceCurveDescriptor.Get(id) is not { } curve) return;
        SaveDraft();
        _selected = _drafts.GetValueOrDefault(id) ?? Read(curve);
        ShowDraft();
    }

    private void ShowDraft()
    {
        _name.Enabled = _folder.Enabled = _thresholds.Enabled = _selected != null;
        _name.Text = _selected?.Name ?? "";
        _folder.Text = _selected?.Folder ?? "";
        _thresholds.Text = _selected?.Thresholds ?? "";
    }

    private void SaveDraft()
    {
        if (_selected == null) return;
        _selected = _selected with { Name = _name.Text, Folder = _folder.Text.Trim(), Thresholds = _thresholds.Text };
        _drafts[_selected.Id] = _selected;
    }

    private void Create(object? sender, EventArgs args) => PacketSender.SendCreateObject(ObjectType);

    private void Copy(object? sender, EventArgs args)
    {
        SaveDraft();
        _copied = _selected;
    }

    private void Paste(object? sender, EventArgs args)
    {
        if (_selected == null || _copied == null) return;
        _selected = _selected with { Name = _copied.Name, Folder = _copied.Folder, Thresholds = _copied.Thresholds };
        ShowDraft();
    }

    private void Undo(object? sender, EventArgs args)
    {
        if (_selected != null && MonsterExperienceCurveDescriptor.Get(_selected.Id) is { } curve)
            _selected = _drafts[curve.Id] = Read(curve);
        ShowDraft();
    }

    private void PopulatePreset(object? sender, EventArgs args)
    {
        if (_selected == null) return;
        var preset = _preset.SelectedIndex switch
        {
            0 => MonsterGrowthPreset.Fast,
            2 => MonsterGrowthPreset.MediumSlow,
            3 => MonsterGrowthPreset.Slow,
            _ => MonsterGrowthPreset.MediumFast,
        };
        _thresholds.Lines = Enumerable.Range(1, Options.Instance.Monsters.Battle.MaxLevel)
            .Select(level => MonsterExperience.Threshold(preset, level).ToString()).ToArray();
    }

    private void DeleteCurve(object? sender, EventArgs args)
    {
        if (_selected == null || MonsterExperienceCurveDescriptor.Get(_selected.Id) is not { } curve) return;
        if (MonsterSpeciesDescriptor.Lookup.Values.Cast<MonsterSpeciesDescriptor>().Any(species => species.ExperienceCurveId == curve.Id))
        {
            DarkMessageBox.ShowWarning("Remove this curve from all species before deleting it.", Text);
            return;
        }
        if (DarkMessageBox.ShowWarning("Delete this experience curve?", Text, DarkDialogButton.YesNo, Icon) == DialogResult.Yes)
            PacketSender.SendDeleteObject(curve);
    }

    private void SaveCurves(object? sender, EventArgs args)
    {
        SaveDraft();
        var cap = Options.Instance.Monsters.Battle.MaxLevel;
        var changed = new List<MonsterExperienceCurveDescriptor>();
        foreach (var draft in _drafts.Values)
        {
            var curve = new MonsterExperienceCurveDescriptor(draft.Id);
            curve.Load(draft.Original);
            curve.Name = draft.Name;
            curve.Folder = draft.Folder;
            var lines = draft.Thresholds.TrimEnd().Split('\n');
            var thresholds = new long[lines.Length];
            if (lines.Length > MonsterExperience.MaximumSupportedLevel ||
                Enumerable.Range(0, lines.Length).Any(index => !long.TryParse(lines[index], out thresholds[index])))
            {
                DarkMessageBox.ShowWarning($"Enter one valid whole EXP value per level for {draft.Name}.", Text);
                return;
            }
            curve.Thresholds = thresholds;
            if (!curve.IsValid(cap))
            {
                DarkMessageBox.ShowWarning($"{draft.Name} must start at 0 and strictly increase through level {cap}.", Text);
                return;
            }
            if (curve.JsonData != draft.Original) changed.Add(curve);
        }
        foreach (var curve in changed) PacketSender.SendSaveObject(curve);
        Close();
    }
}
