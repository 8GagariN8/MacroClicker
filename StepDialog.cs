namespace MacroClicker;

internal sealed class StepDialog : Form
{
    public MacroStep Step { get; private set; }
    public StepDialog(MacroStep? source = null, bool createFromTemplate = false)
    {
        Step = source?.Copy() ?? new MacroStep();
        Text = source is null || createFromTemplate ? "Добавить действие" : "Редактировать действие";
        ClientSize = new Size(720, 720); MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(16) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        kind.Items.AddRange(new object[] { "Клавиша / комбинация", "Мышь", "Пауза", "Текст", "Перемещение курсора", "Колесо мыши", "Двойной клик мышью" });
        kind.SelectedIndex = (int)Step.Kind;
        var input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Text = Step.Input };
        input.Items.AddRange(InputSender.SupportedKeys.Cast<object>().ToArray());
        var action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        action.Items.AddRange(new object[] { "Нажать и отпустить", "Нажать (удерживать до шага отпускания)", "Отпустить" }); action.SelectedIndex = (int)Step.Action;
        var coords = new CheckBox { Text = "Переместить перед действием", Checked = Step.UseCoordinates, AutoSize = true };
        var space = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        space.Items.AddRange(new object[] { "Окно приложения", "Весь экран" }); space.SelectedIndex = (int)Step.CoordinateSpace;
        var x = Ui.Number(Step.X, -100000, 100000); var y = Ui.Number(Step.Y, -100000, 100000);
        var move = Ui.Number(Step.MoveDelayMs); var before = Ui.Number(Step.DelayBeforeMs);
        var hold = Ui.Number(Step.HoldMs, 1); var after = Ui.Number(Step.DelayAfterMs);
        var text = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, Height = 130, MaxLength = 1000000, Text = Step.Text };
        var charDelay = Ui.Number(Step.CharacterDelayMs, 0, 10000);
        var wheel = Ui.Number(Step.WheelDelta, -12000, 12000);
        var horizontal = new CheckBox { Text = "Горизонтальное колесо", Checked = Step.HorizontalWheel, AutoSize = true };
        var fieldRows = new List<(Control Label, Control Input)>();
        int row = 0;
        void Add(string label, Control control)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = Ui.Label(label);
            fieldRows.Add((caption, control));
            fields.Controls.Add(caption, 0, row); control.Dock = DockStyle.Fill; control.Margin = new Padding(4);
            fields.Controls.Add(control, 1, row++);
        }
        Add("Тип", kind); Add("Клавиша / комбинация / кнопка", input);
        Add("Режим кнопки", action);
        Add("Примеры комбинаций", Ui.Label("Ctrl+C · Ctrl+Shift+S · Alt+Shift · Win+Space\nКлавиша «+»: Oemplus или Add. Fn зависит от устройства."));
        Add("Координаты", coords); Add("Система координат", space); Add("X", x); Add("Y", y);
        Add("Пауза после перемещения, мс", move); Add("Текст (до 1 млн символов)", text);
        Add("Интервал символов, мс", charDelay); Add("Прокрутка (120 = деление)", wheel); Add("Направление", horizontal);
        Add("Задержка до, мс", before); Add("Удержание / пауза, мс", hold); Add("Пауза после, мс", after);
        void UpdateFields()
        {
            var k = (InputKind)kind.SelectedIndex;
            input.Enabled = k is InputKind.Key or InputKind.Mouse or InputKind.DoubleClick;
            action.Enabled = k is InputKind.Key or InputKind.Mouse;
            coords.Enabled = k is InputKind.Mouse or InputKind.DoubleClick or InputKind.Wheel;
            space.Enabled = x.Enabled = y.Enabled = move.Enabled = k == InputKind.Move || (coords.Checked && coords.Enabled);
            text.Enabled = charDelay.Enabled = k == InputKind.Text;
            wheel.Enabled = horizontal.Enabled = k == InputKind.Wheel;
            hold.Enabled = k == InputKind.Pause || (k is InputKind.Key or InputKind.Mouse && action.SelectedIndex == 0);
            var showCoordinates = k == InputKind.Move || (coords.Checked && coords.Enabled);
            var visibleRows = new HashSet<int> { 0, 13, 15 };
            if (k is InputKind.Key or InputKind.Mouse) { visibleRows.Add(1); visibleRows.Add(2); }
            if (k == InputKind.DoubleClick) { visibleRows.Add(1); visibleRows.Add(3); }
            fieldRows[1].Label.Text = k is InputKind.Mouse or InputKind.DoubleClick ? "Кнопка мыши" : "Клавиша / комбинация";
            fieldRows[3].Label.Text = k == InputKind.DoubleClick ? "Интервалы внутри пары" : "Примеры комбинаций";
            fieldRows[3].Input.Text = k == InputKind.DoubleClick
                ? "Автоматически по настройке Windows. Системная пауза между двумя кликами не добавляется."
                : "Ctrl+C · Ctrl+Shift+S · Alt+Shift · Win+Space\nКлавиша «+»: Oemplus или Add. Fn зависит от устройства.";
            if (k == InputKind.Key) visibleRows.Add(3);
            if (coords.Enabled) visibleRows.Add(4);
            if (showCoordinates) foreach (var index in new[] { 5, 6, 7, 8 }) visibleRows.Add(index);
            if (k == InputKind.Text) { visibleRows.Add(9); visibleRows.Add(10); }
            if (k == InputKind.Wheel) { visibleRows.Add(11); visibleRows.Add(12); }
            if (hold.Enabled) visibleRows.Add(14);
            for (var index = 0; index < fieldRows.Count; index++)
                fieldRows[index].Label.Visible = fieldRows[index].Input.Visible = visibleRows.Contains(index);
        }
        kind.SelectedIndexChanged += (_, _) =>
        {
            input.Items.Clear();
            var isMouse = (InputKind)kind.SelectedIndex is InputKind.Mouse or InputKind.DoubleClick;
            input.Items.AddRange((isMouse ? InputSender.SupportedMouseButtons : InputSender.SupportedKeys).Cast<object>().ToArray());
            if (isMouse) input.Text = "Левая";
            UpdateFields();
        };
        coords.CheckedChanged += (_, _) => UpdateFields(); action.SelectedIndexChanged += (_, _) => UpdateFields();
        if (Step.Kind is InputKind.Mouse or InputKind.DoubleClick) { input.Items.Clear(); input.Items.AddRange(InputSender.SupportedMouseButtons.Cast<object>().ToArray()); }
        UpdateFields();
        var buttons = Ui.Row(); buttons.FlowDirection = FlowDirection.RightToLeft;
        var save = Ui.Button("Сохранить", () =>
        {
            try
            {
                var result = new MacroStep
                {
                    Kind = (InputKind)kind.SelectedIndex, Input = input.Text,
                    Action = (InputKind)kind.SelectedIndex == InputKind.DoubleClick ? ButtonAction.Tap : (ButtonAction)action.SelectedIndex,
                    UseCoordinates = coords.Checked, CoordinateSpace = (CoordinateSpace)space.SelectedIndex,
                    X = (int)x.Value, Y = (int)y.Value, MoveDelayMs = (int)move.Value,
                    Text = text.Text, CharacterDelayMs = (int)charDelay.Value, WheelDelta = (int)wheel.Value,
                    HorizontalWheel = horizontal.Checked, DelayBeforeMs = (int)before.Value, HoldMs = (int)hold.Value, DelayAfterMs = (int)after.Value
                };
                MacroStorage.Validate(new MacroDocument { Nodes = [new MacroNode { Steps = [result] }] });
                Step = result; DialogResult = DialogResult.OK;
            }
            catch (Exception e) { Ui.Error(this, e); }
        });
        buttons.Controls.Add(save);
        var cancel = Ui.Button("Отмена", () => DialogResult = DialogResult.Cancel); buttons.Controls.Add(cancel);
        buttons.Dock = DockStyle.Bottom; Controls.Add(fields); Controls.Add(buttons); CancelButton = cancel;
        Ui.Theme(this);
    }
}
