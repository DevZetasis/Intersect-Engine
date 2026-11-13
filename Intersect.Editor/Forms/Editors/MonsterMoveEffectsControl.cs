using DarkUI.Controls;
using Intersect.Editor.Localization;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors;

/// <summary>Edits the selected move draft. The parent editor owns save, undo and cancellation.</summary>
internal sealed class MonsterMoveEffectsControl : FlowLayoutPanel
{
    private readonly ListBox _effects = new() { Width = 265, Height = 65 };
    private readonly DarkComboBox _kind = Choice(Strings.MonsterMoveEditor.StatStage, Strings.MonsterMoveEditor.RestoreHP, "Apply Status", "Cure Status");
    private readonly DarkComboBox _target = Choice(Strings.MonsterMoveEditor.EffectSelf, Strings.MonsterMoveEditor.EffectOpponent);
    private readonly DarkComboBox _stat = Choice("Attack", "Defense", "Special Attack", "Special Defense", "Speed");
    private readonly DarkComboBox _method = Choice(Strings.MonsterMoveEditor.FixedHP, Strings.MonsterMoveEditor.PercentHP);
    private readonly DarkComboBox _status = Choice();
    private readonly DarkNumericUpDown _chance = new() { Width = 120, Minimum = 1, Maximum = 100, Value = 100 };
    private sealed record StatusChoice(Guid? Id, string Name) { public override string ToString() => Name; }
    private readonly DarkNumericUpDown _amount = new() { Width = 120 };
    private readonly DarkButton _add = new() { Text = Strings.MonsterMoveEditor.AddEffect, AutoSize = true };
    private readonly DarkButton _remove = new() { Text = Strings.MonsterMoveEditor.RemoveEffect, AutoSize = true };
    private MonsterMoveDescriptor? _move;
    private bool _loading;

    internal MonsterMoveEffectsControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        _effects.BackColor = System.Drawing.Color.FromArgb(60, 63, 65);
        _effects.ForeColor = System.Drawing.Color.Gainsboro;
        Controls.Add(_effects);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([_add, _remove]);
        Controls.Add(buttons);
        AddField(Strings.MonsterMoveEditor.EffectKind, _kind);
        AddField(Strings.MonsterMoveEditor.EffectTarget, _target);
        AddField(Strings.MonsterMoveEditor.EffectStat, _stat);
        AddField(Strings.MonsterMoveEditor.HealingMethod, _method);
        AddField(Strings.MonsterMoveEditor.EffectAmount, _amount);
        AddField("Status", _status);
        AddField("Chance (%)", _chance);
        _status.SelectedIndexChanged += (_, _) => UpdateEffect();
        _chance.ValueChanged += (_, _) => UpdateEffect();
        _effects.SelectedIndexChanged += (_, _) => { if (!_loading) ShowEffect(); };
        foreach (var choice in new[] { _kind, _target, _stat, _method })
            choice.SelectedIndexChanged += (_, _) => UpdateEffect();
        _amount.ValueChanged += (_, _) => UpdateEffect();
        _add.Click += (_, _) => AddEffect();
        _remove.Click += (_, _) => RemoveEffect();
    }

    private static DarkComboBox Choice(params string[] items)
    {
        var combo = new DarkComboBox { Width = 142, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(items);
        return combo;
    }

    private void AddField(string text, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new DarkLabel { Text = text, Width = 100, Height = 25 });
        row.Controls.Add(control);
        Controls.Add(row);
    }

    internal void Bind(MonsterMoveDescriptor? move)
    {
        var previous = ReferenceEquals(_move, move) ? _effects.SelectedIndex : 0;
        _move = move;
        RefreshList(previous);
    }

    private void RefreshList(int index)
    {
        _loading = true;
        try
        {
            _effects.Items.Clear();
            if (_move != null)
            {
                foreach (var effect in _move.Effects) _effects.Items.Add(Describe(effect));
                if (_effects.Items.Count > 0) _effects.SelectedIndex = Math.Clamp(index, 0, _effects.Items.Count - 1);
            }
        }
        finally { _loading = false; }
        ShowEffect();
    }

    private static string Describe(MonsterMoveEffect effect) => effect.Kind == MonsterMoveEffectKind.StatStage
        ? $"{effect.Target}: {effect.Stat} {effect.Amount:+0;-0;0}"
        : effect.Kind == MonsterMoveEffectKind.ApplyStatus ? $"{effect.Target}: Apply {StatusName(effect.StatusId)} ({effect.Chance}%)"
        : effect.Kind == MonsterMoveEffectKind.CureStatus ? $"Self: Cure {StatusName(effect.StatusId)}"
        : $"Self: Restore {effect.Amount}{(effect.HealingMethod == MonsterHealingMethod.Percentage ? "% HP" : " HP")}";

    private static string StatusName(Guid? id) => id is { } value
        ? MonsterStatusDescriptor.Get(value)?.Name ?? value.ToString()
        : "active Major status";

    private void ShowEffect()
    {
        var index = _effects.SelectedIndex;
        var selected = _move != null && index >= 0 ? _move.Effects[index] : null;
        _add.Enabled = _move != null && _move.Effects.Count < MonsterMoveDescriptor.MaximumEffects;
        _remove.Enabled = _kind.Enabled = selected != null;
        _loading = true;
        try
        {
            var effect = selected ?? new MonsterMoveEffect();
            var healing = effect.Kind == MonsterMoveEffectKind.RestoreHP;
            var apply = effect.Kind == MonsterMoveEffectKind.ApplyStatus;
            var cure = effect.Kind == MonsterMoveEffectKind.CureStatus;
            _status.Items.Clear();
            _status.Items.Add(new StatusChoice(null, cure ? "Any Major status" : "Select a status"));
            foreach (var definition in MonsterStatusDescriptor.Lookup.Values.Cast<MonsterStatusDescriptor>().OrderBy(item => item.Name))
                _status.Items.Add(new StatusChoice(definition.Id, definition.Name));
            if (!_status.Items.Cast<StatusChoice>().Any(item => item.Id == effect.StatusId))
                _status.Items.Add(new StatusChoice(effect.StatusId, StatusName(effect.StatusId)));
            _status.SelectedItem = _status.Items.Cast<StatusChoice>().First(item => item.Id == effect.StatusId);
            _chance.Value = Math.Clamp(effect.Chance, 1, 100);
            _status.Enabled = selected != null && (apply || cure);
            _chance.Enabled = selected != null && apply;
            _kind.SelectedIndex = (int)effect.Kind;
            _target.SelectedIndex = healing ? 0 : (int)effect.Target;
            _stat.SelectedIndex = (int)effect.Stat - (int)MonsterStat.Attack;
            _method.SelectedIndex = (int)effect.HealingMethod;
            _target.Enabled = selected != null && !healing && !cure;
            _stat.Enabled = selected != null && !healing && !apply && !cure;
            _method.Enabled = selected != null && healing;
            _amount.Enabled = selected != null && !apply && !cure;
            SetAmountBounds(healing, effect.HealingMethod);
            _amount.Value = Math.Clamp(effect.Amount, (int)_amount.Minimum, (int)_amount.Maximum);
        }
        finally { _loading = false; }
    }

    private void SetAmountBounds(bool healing, MonsterHealingMethod method)
    {
        _amount.Minimum = healing ? 1 : -6;
        _amount.Maximum = healing ? method == MonsterHealingMethod.Percentage ? 100 : int.MaxValue : 6;
    }

    private void UpdateEffect()
    {
        var index = _effects.SelectedIndex;
        if (_loading || _move == null || index < 0) return;
        var previous = _move.Effects[index];
        var kind = (MonsterMoveEffectKind)_kind.SelectedIndex;
        var healing = kind == MonsterMoveEffectKind.RestoreHP;
        var method = (MonsterHealingMethod)_method.SelectedIndex;
        var amount = (int)_amount.Value;
        if (!healing && amount == 0) amount = previous.Amount < 0 ? 1 : -1;
        var next = new MonsterMoveEffect
        {
            Kind = kind,
            Target = healing || kind == MonsterMoveEffectKind.CureStatus ? MonsterMoveEffectTarget.Self : (MonsterMoveEffectTarget)_target.SelectedIndex,
            Stat = (MonsterStat)(_stat.SelectedIndex + (int)MonsterStat.Attack),
            HealingMethod = method,
            StatusId = kind is MonsterMoveEffectKind.ApplyStatus or MonsterMoveEffectKind.CureStatus ? (_status.SelectedItem as StatusChoice)?.Id : null,
            Chance = (int)_chance.Value,
            Amount = healing ? Math.Clamp(amount, 1, method == MonsterHealingMethod.Percentage ? 100 : int.MaxValue)
                : Math.Clamp(amount, -6, 6),
        };
        _move.Effects[index] = next;
        _loading = true;
        try { _effects.Items[index] = Describe(next); }
        finally { _loading = false; }
        ShowEffect();
    }

    private void AddEffect()
    {
        if (_move == null || _move.Effects.Count >= MonsterMoveDescriptor.MaximumEffects) return;
        _move.Effects.Add(new());
        RefreshList(_move.Effects.Count - 1);
    }

    private void RemoveEffect()
    {
        var index = _effects.SelectedIndex;
        if (_move == null || index < 0) return;
        _move.Effects.RemoveAt(index);
        RefreshList(index);
    }
}
