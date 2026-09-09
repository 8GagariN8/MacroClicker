namespace MacroClicker;

internal sealed class HotkeyDialog : Form
{
    private readonly CheckBox _startCtrl = Box("Ctrl"), _startAlt = Box("Alt"), _startShift = Box("Shift"), _startWin = Box("Win");
    private readonly CheckBox _stopCtrl = Box("Ctrl"), _stopAlt = Box("Alt"), _stopShift = Box("Shift"), _stopWin = Box("Win");
    private readonly ComboBox _startKey = KeyPicker(), _stopKey = KeyPicker();
    private readonly CheckBox _separate = new() { Text = "Использовать разные сочетания для старта и остановки", AutoSize = true };
    private readonly Label _startLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _stopLabel = new() { Text = "Остановка", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel _stopPanel;

    public bool SeparateStopHotkey { get; private set; }
    public HotkeyDefinition StartHotkey { get; private set; }
    public HotkeyDefinition StopHotkey { get; private set; }

    public HotkeyDialog(bool separateStopHotkey, HotkeyDefinition start, HotkeyDefinition stop)
    {
        SeparateStopHotkey = separateStopHotkey;
        StartHotkey = start.Copy();
        StopHotkey = stop.Copy();
        Text = "Горячие клавиши";
        ClientSize = new Size(560, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        SetControls(start, _startCtrl, _startAlt, _startShift, _startWin, _startKey);
        SetControls(stop, _stopCtrl, _stopAlt, _stopShift, _stopWin, _stopKey);
        _stopPanel = HotkeyRow(_stopCtrl, _stopAlt, _stopShift, _stopWin, _stopKey);
        _separate.Checked = separateStopHotkey;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(_separate, 0, 0);
        layout.SetColumnSpan(_separate, 2);
        layout.Controls.Add(_startLabel, 0, 1);
        layout.Controls.Add(HotkeyRow(_startCtrl, _startAlt, _startShift, _startWin, _startKey), 1, 1);
        layout.Controls.Add(_stopLabel, 0, 2);
        layout.Controls.Add(_stopPanel, 1, 2);
        var note = new Label { Text = "Выберите сочетание, которое не используется в макросе. F10 останавливает запись действий.", AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(480, 0) };
        layout.Controls.Add(note, 0, 3);
        layout.SetColumnSpan(note, 2);

        var save = new Button { Text = "Применить", AutoSize = true };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        CancelButton = cancel;
        _separate.CheckedChanged += (_, _) => UpdateMode();
        UpdateMode();

        save.Click += (_, _) =>
        {
            var newStart = ReadControls(_startCtrl, _startAlt, _startShift, _startWin, _startKey);
            var newStop = ReadControls(_stopCtrl, _stopAlt, _stopShift, _stopWin, _stopKey);
            if (_separate.Checked && newStart.SameAs(newStop))
            {
                MessageBox.Show(this, "Для старта и остановки нужны разные сочетания.", "Одинаковые сочетания", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SeparateStopHotkey = _separate.Checked;
            StartHotkey = newStart;
            StopHotkey = newStop;
            DialogResult = DialogResult.OK;
        };
    }

    private void UpdateMode()
    {
        _startLabel.Text = _separate.Checked ? "Запуск" : "Старт / стоп";
        _stopLabel.Visible = _separate.Checked;
        _stopPanel.Visible = _separate.Checked;
    }

    private static CheckBox Box(string text) => new() { Text = text, AutoSize = true };

    private static ComboBox KeyPicker()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        combo.Items.AddRange(Enumerable.Range(1, 12).Select(i => $"F{i}")
            .Concat(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(i => i.ToString())).Cast<object>().ToArray());
        return combo;
    }

    private static FlowLayoutPanel HotkeyRow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static void SetControls(HotkeyDefinition value, CheckBox ctrl, CheckBox alt, CheckBox shift, CheckBox win, ComboBox key)
    {
        ctrl.Checked = value.Control; alt.Checked = value.Alt; shift.Checked = value.Shift; win.Checked = value.Windows;
        key.SelectedItem = (int)value.Key >= (int)Keys.D0 && (int)value.Key <= (int)Keys.D9
            ? ((int)value.Key - (int)Keys.D0).ToString()
            : value.Key.ToString();
        if (key.SelectedIndex < 0) key.SelectedIndex = 0;
    }

    private static HotkeyDefinition ReadControls(CheckBox ctrl, CheckBox alt, CheckBox shift, CheckBox win, ComboBox key) => new()
    {
        Control = ctrl.Checked, Alt = alt.Checked, Shift = shift.Checked, Windows = win.Checked,
        Key = ParseKey(key.SelectedItem?.ToString() ?? "F8")
    };

    private static Keys ParseKey(string value) => value.Length == 1 && char.IsDigit(value[0])
        ? (Keys)((int)Keys.D0 + value[0] - '0')
        : Enum.Parse<Keys>(value, true);
}
