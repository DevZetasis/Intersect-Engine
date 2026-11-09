using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.General;
using Intersect.Editor.Localization;
using Intersect.Editor.Networking;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

public sealed class FrmMonsterNature : EditorForm
{
    private readonly GameObjectList _natureList = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<Guid, MonsterNatureDescriptor> _drafts = new();
    private readonly Dictionary<Guid, string> _originals = new();
    private readonly DarkTextBox _id = new() { ReadOnly = true };
    private readonly DarkTextBox _name = new();
    private readonly DarkTextBox _description = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical };
    private readonly DarkTextBox _folder = new();
    private readonly DarkComboBox _increasedStat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkComboBox _decreasedStat = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkNumericUpDown _increasePercentage = PercentageInput();
    private readonly DarkNumericUpDown _decreasePercentage = PercentageInput();
    private readonly DarkNumericUpDown _selectionWeight = new() { Minimum = 0, Maximum = 100000, Value = 100 };
    private readonly DarkTextBox _preview = new() { Multiline = true, ReadOnly = true, Height = 110, TabStop = false, ScrollBars = ScrollBars.Vertical };
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
    private readonly ToolTip _toolTip = new();
    private sealed record StatChoice(MonsterStat Stat, string Name) { public override string ToString() => Name; }
    private MonsterNatureDescriptor? _selected;
    private string? _copied;
    private bool _loading;
    private static DarkNumericUpDown PercentageInput() => new() { Minimum = 0, Maximum = 50, Value = 10, DecimalPlaces = 2 };

    public FrmMonsterNature()
    {
        Text = Strings.MonsterNatureEditor.Menu;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(840, 740);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;
        _natureList.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _natureList.ForeColor = ForeColor;
        _natureList.Init(() => { }, SelectNature, NewNature, CopyNature, UndoNature, PasteNature, DeleteNature);
        var listPanel = new Panel { Dock = DockStyle.Left, Width = 245, Padding = new Padding(8) };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        AddButton(tools, Strings.MonsterSpeciesEditor.New, NewNature);
        AddButton(tools, Strings.MonsterSpeciesEditor.Copy, CopyNature);
        AddButton(tools, Strings.MonsterSpeciesEditor.Paste, PasteNature);
        AddButton(tools, Strings.MonsterSpeciesEditor.Undo, UndoNature);
        AddButton(tools, Strings.MonsterSpeciesEditor.Delete, DeleteNature);
        listPanel.Controls.Add(_natureList);
        listPanel.Controls.Add(tools);
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(Strings.MonsterNatureEditor.Id, _id);
        AddField(Strings.MonsterNatureEditor.Name, _name);
        AddField(Strings.MonsterNatureEditor.Description, _description);
        AddField(Strings.MonsterSpeciesEditor.Folder, _folder);
        AddField(Strings.MonsterNatureEditor.IncreasedStat, _increasedStat);
        AddField(Strings.MonsterNatureEditor.IncreasePercentage, _increasePercentage);
        AddField(Strings.MonsterNatureEditor.DecreasedStat, _decreasedStat);
        AddField(Strings.MonsterNatureEditor.DecreasePercentage, _decreasePercentage);
        AddField(Strings.MonsterNatureEditor.SelectionWeight, _selectionWeight);
        AddField(string.Empty, new DarkLabel { Text = Strings.MonsterNatureEditor.WeightHelp, Height = 62 });
        AddField(Strings.MonsterNatureEditor.Preview, _preview);
        var choices = new[]
        {
            new StatChoice(MonsterStat.None, Strings.General.None),
            new StatChoice(MonsterStat.Attack, Strings.MonsterNatureEditor.Attack),
            new StatChoice(MonsterStat.Defense, Strings.MonsterNatureEditor.Defense),
            new StatChoice(MonsterStat.SpecialAttack, Strings.MonsterNatureEditor.SpecialAttack),
            new StatChoice(MonsterStat.SpecialDefense, Strings.MonsterNatureEditor.SpecialDefense),
            new StatChoice(MonsterStat.Speed, Strings.MonsterNatureEditor.Speed),
        };
        _increasedStat.Items.AddRange(choices);
        _decreasedStat.Items.AddRange(choices);
        _toolTip.SetToolTip(_selectionWeight, Strings.MonsterNatureEditor.WeightHelp);
        _toolTip.SetToolTip(_increasePercentage, Strings.MonsterNatureEditor.PercentageHelp);
        _toolTip.SetToolTip(_decreasePercentage, Strings.MonsterNatureEditor.PercentageHelp);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        _btnCancel = AddButton(buttons, Strings.MonsterSpeciesEditor.Cancel, (_, _) => Close());
        _btnSave = AddButton(buttons, Strings.MonsterSpeciesEditor.Save, SaveNatures);
        Controls.Add(_fields);
        Controls.Add(listPanel);
        Controls.Add(buttons);
        _name.TextChanged += EditFields;
        _description.TextChanged += EditFields;
        _folder.TextChanged += EditFields;
        _folder.Leave += (_, _) => InitEditor();
        _increasedStat.SelectedIndexChanged += EditFields;
        _decreasedStat.SelectedIndexChanged += EditFields;
        foreach (var number in new[] { _increasePercentage, _decreasePercentage, _selectionWeight }) number.ValueChanged += EditFields;
        FormClosed += (_, _) => Globals.CurrentEditor = -1;
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ConstrainToWorkingArea(Screen.FromControl(this).WorkingArea);
    }
    private void ConstrainToWorkingArea(System.Drawing.Rectangle area)
    {
        // Preserve DPI-scaled controls and scrolling while keeping Save/Cancel on screen.
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new System.Drawing.Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
    private static DarkButton AddButton(Control parent, string text, EventHandler handler)
    {
        var button = new DarkButton { Text = text, AutoSize = true, MinimumSize = new Size(65, 26) };
        button.Click += handler;
        parent.Controls.Add(button);
        return button;
    }
    private void AddField(string label, Control control)
    {
        var row = _fields.RowCount++;
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(new DarkLabel { Text = label, AutoSize = true, Margin = new Padding(0, 7, 8, 7) }, 0, row);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 4, 0, 4);
        _fields.Controls.Add(control, 1, row);
    }
    public void InitEditor()
    {
        foreach (var id in _drafts.Keys.Where(id => MonsterNatureDescriptor.Get(id) == null).ToArray())
        {
            _drafts.Remove(id);
            _originals.Remove(id);
        }
        var natures = MonsterNatureDescriptor.Lookup.Values.Cast<MonsterNatureDescriptor>()
            .Select(nature => _drafts.GetValueOrDefault(nature.Id) ?? nature).OrderBy(nature => nature.Name).ToArray();
        _natureList.Repopulate(natures.Select(nature => new KeyValuePair<Guid, KeyValuePair<string, string>>(nature.Id, new(nature.Name, nature.Folder))).ToArray(),
            natures.Select(nature => nature.Folder).Where(folder => !string.IsNullOrEmpty(folder)).Distinct().Order().ToList(), false, false, string.Empty);
        if (_selected != null && MonsterNatureDescriptor.Get(_selected.Id) == null) _selected = null;
        if (_selected == null && natures.Length > 0)
        {
            _natureList.SelectedNode = _natureList.Nodes.Cast<TreeNode>()
                .SelectMany(node => node.Tag is Guid ? new[] { node } : node.Nodes.Cast<TreeNode>()).First();
            SelectNature((Guid)_natureList.SelectedNode.Tag);
        }
        ShowFields();
    }
    protected override void GameObjectUpdatedDelegate(GameObjectType type)
    {
        if (type != GameObjectType.MonsterNature) return;
        foreach (var (id, draft) in _drafts)
            if (draft.JsonData == _originals[id] && MonsterNatureDescriptor.Get(id) is { } source)
            {
                draft.Load(source.JsonData);
                draft.MakeBackup();
                _originals[id] = draft.JsonData;
            }
        InitEditor();
    }
    private void SelectNature(Guid id)
    {
        if (!MonsterNatureDescriptor.TryGet(id, out var source)) return;
        if (!_drafts.TryGetValue(id, out var draft))
        {
            draft = new MonsterNatureDescriptor(id);
            draft.Load(source.JsonData);
            draft.MakeBackup();
            _drafts.Add(id, draft);
            _originals.Add(id, draft.JsonData);
        }
        _selected = draft;
        ShowFields();
    }
    private void ShowFields()
    {
        _fields.Enabled = _selected != null;
        if (_selected == null) { _preview.Text = string.Empty; return; }
        _loading = true;
        try
        {
            _id.Text = _selected.Id.ToString();
            _name.Text = _selected.Name;
            _description.Text = _selected.Description;
            _folder.Text = _selected.Folder;
            _increasedStat.SelectedItem = _increasedStat.Items.Cast<StatChoice>().First(choice => choice.Stat == _selected.Modifiers.Increase.Stat);
            _decreasedStat.SelectedItem = _decreasedStat.Items.Cast<StatChoice>().First(choice => choice.Stat == _selected.Modifiers.Decrease.Stat);
            _increasePercentage.Value = _selected.Modifiers.Increase.Percentage;
            _decreasePercentage.Value = _selected.Modifiers.Decrease.Percentage;
            _selectionWeight.Value = _selected.SelectionWeight;
            UpdatePreview();
        }
        finally { _loading = false; }
    }
    private void EditFields(object? sender, EventArgs e)
    {
        if (_loading || _selected == null) return;
        _selected.Name = _name.Text;
        _selected.Description = _description.Text;
        _selected.Folder = _folder.Text;
        _selected.Modifiers.Increase.Stat = (_increasedStat.SelectedItem as StatChoice)?.Stat ?? MonsterStat.None;
        _selected.Modifiers.Decrease.Stat = (_decreasedStat.SelectedItem as StatChoice)?.Stat ?? MonsterStat.None;
        _selected.Modifiers.Increase.Percentage = _increasePercentage.Value;
        _selected.Modifiers.Decrease.Percentage = _decreasePercentage.Value;
        _selected.SelectionWeight = (int)_selectionWeight.Value;
        _natureList.UpdateText(_selected.Name);
        UpdatePreview();
    }
    private void UpdatePreview()
    {
        if (_selected == null) return;
        _increasePercentage.Enabled = _selected.Modifiers.Increase.Stat != MonsterStat.None;
        _decreasePercentage.Enabled = _selected.Modifiers.Decrease.Stat != MonsterStat.None;
        var lines = new List<string> { Strings.MonsterNatureEditor.NatureSummary.ToString(_selected.Name) };
        if (_selected.Modifiers.Increase.Stat != MonsterStat.None)
            lines.Add(Strings.MonsterNatureEditor.IncreaseSummary.ToString(_increasedStat.Text, _increasePercentage.Value.ToString("0.##")));
        if (_selected.Modifiers.Decrease.Stat != MonsterStat.None)
            lines.Add(Strings.MonsterNatureEditor.DecreaseSummary.ToString(_decreasedStat.Text, _decreasePercentage.Value.ToString("0.##")));
        if (_selected.Modifiers.Increase.Stat == MonsterStat.None && _selected.Modifiers.Decrease.Stat == MonsterStat.None)
            lines.Add(Strings.MonsterNatureEditor.Neutral);
        lines.Add(Strings.MonsterNatureEditor.WeightSummary.ToString(_selected.SelectionWeight));
        if (_selected.SelectionWeight == 0) lines.Add(Strings.MonsterNatureEditor.Excluded);
        if (_selected.Modifiers.HasConflict) lines.Add(Strings.MonsterNatureEditor.Conflict);
        _preview.Text = string.Join(Environment.NewLine, lines);
    }
    private void NewNature(object? sender, EventArgs e) => PacketSender.SendCreateObject(GameObjectType.MonsterNature);
    private void CopyNature(object? sender, EventArgs e) => _copied = _selected?.JsonData;
    private void PasteNature(object? sender, EventArgs e)
    {
        if (_selected == null || _copied == null) return;
        _selected.Load(_copied, keepCreationTime: true);
        InitEditor();
    }
    private void UndoNature(object? sender, EventArgs e) { _selected?.RestoreBackup(); InitEditor(); }
    private void DeleteNature(object? sender, EventArgs e)
    {
        if (_selected != null && DarkMessageBox.ShowWarning(Strings.MonsterNatureEditor.DeletePrompt, Text,
                DarkDialogButton.YesNo, Icon) == DialogResult.Yes) PacketSender.SendDeleteObject(_selected);
    }
    private void SaveNatures(object? sender, EventArgs e)
    {
        var changed = _drafts.Values.Where(nature => nature.JsonData != _originals[nature.Id]).ToArray();
        if (changed.Any(nature => nature.Modifiers?.HasConflict == true))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterNatureEditor.Conflict, Text);
            return;
        }
        if (changed.Any(nature => !nature.IsValid))
        {
            DarkMessageBox.ShowWarning(Strings.MonsterNatureEditor.Invalid, Text);
            return;
        }
        foreach (var nature in changed) PacketSender.SendSaveObject(nature);
        Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
