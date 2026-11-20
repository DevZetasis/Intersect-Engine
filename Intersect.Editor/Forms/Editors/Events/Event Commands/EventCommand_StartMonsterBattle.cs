using DarkUI.Controls;
using DarkUI.Forms;
using Intersect.Editor.Localization;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors.Events.Event_Commands;

public sealed class EventCommandStartMonsterBattle : UserControl
{
    private sealed record SpeciesChoice(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly StartMonsterBattleCommand _command;
    private readonly FrmEvent? _editor;
    private readonly DarkComboBox _species = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DarkCheckBox _allowCapture = new() { Dock = DockStyle.Top, Height = 28, Text = "Allow Capture" };
    private readonly DarkCheckBox _awardExperience = new() { Dock = DockStyle.Top, Height = 28, Text = "Award Experience" };
    private readonly DarkNumericUpDown _experienceMultiplier = new() { Dock = DockStyle.Top, Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = 0.05m };
    private readonly DarkNumericUpDown _level = new() { Dock = DockStyle.Top, Minimum = 1 };

    public EventCommandStartMonsterBattle(StartMonsterBattleCommand command, FrmEvent? editor)
    {
        _command = command;
        _editor = editor;
        Text = "Start Monster Battle";
        Size = new Size(390, 292);
        MinimumSize = Size;
        BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
        ForeColor = System.Drawing.Color.Gainsboro;

        var choices = MonsterSpeciesDescriptor.Lookup.Values.Cast<MonsterSpeciesDescriptor>()
            .OrderBy(item => item.Name).Select(item => new SpeciesChoice(item.Id, item.Name)).ToList();
        choices.Insert(0, new(Guid.Empty, "Select a monster species"));
        if (choices.All(item => item.Id != command.SpeciesId))
            choices.Insert(1, new(command.SpeciesId, $"Missing species ({command.SpeciesId})"));
        _species.Items.AddRange(choices.Cast<object>().ToArray());
        _species.SelectedItem = choices.First(item => item.Id == command.SpeciesId);
        _level.Maximum = Options.Instance.Monsters.Battle.MaxLevel;
        _level.Value = Math.Clamp(command.Level, 1, (int)_level.Maximum);

        var fields = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) };
        _allowCapture.Checked = command.AllowCapture;
        _awardExperience.Checked = command.AwardExperience;
        _experienceMultiplier.Value = Math.Clamp(command.ExperienceMultiplier, 0, 10);
        _experienceMultiplier.Enabled = _awardExperience.Checked;
        _awardExperience.CheckedChanged += (_, _) => _experienceMultiplier.Enabled = _awardExperience.Checked;
        fields.Controls.Add(_experienceMultiplier);
        fields.Controls.Add(new DarkLabel { Text = "Experience multiplier", Dock = DockStyle.Top, Height = 26 });
        fields.Controls.Add(_awardExperience);
        fields.Controls.Add(_allowCapture);
        fields.Controls.Add(_level);
        fields.Controls.Add(new DarkLabel { Text = "Opponent level", Dock = DockStyle.Top, Height = 26 });
        fields.Controls.Add(_species);
        fields.Controls.Add(new DarkLabel { Text = "Monster species", Dock = DockStyle.Top, Height = 26 });

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5) };
        var save = new DarkButton { Text = Strings.EventChangeVital.okay, Width = 100 };
        save.Click += (_, _) =>
        {
            if (!TryApply())
            {
                DarkMessageBox.ShowWarning("Select an existing species and a level within the server's monster level limit.", Text);
                return;
            }
            if (_editor != null) _editor.FinishCommandEdit(); else Dispose();
        };
        var cancel = new DarkButton { Text = Strings.EventChangeVital.cancel, Width = 100 };
        cancel.Click += (_, _) => { if (_editor != null) _editor.CancelCommandEdit(); else Dispose(); };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        Controls.Add(fields);
        Controls.Add(footer);
    }

    private bool TryApply()
    {
        var draft = new StartMonsterBattleCommand
        {
            SpeciesId = (_species.SelectedItem as SpeciesChoice)?.Id ?? Guid.Empty,
            Level = (int)_level.Value,
        };
        if (!draft.IsValid(Options.Instance.Monsters.Battle.MaxLevel, id => MonsterSpeciesDescriptor.Get(id) != null)) return false;
        _command.SpeciesId = draft.SpeciesId;
        _command.Level = draft.Level;
        if (_editor != null) _command.EnsureCaptureBranch(_editor.CurrentPage.CommandLists);
        _command.AllowCapture = _allowCapture.Checked;
        _command.AwardExperience = _awardExperience.Checked;
        _command.ExperienceMultiplier = _experienceMultiplier.Value;
        return true;
    }
}
