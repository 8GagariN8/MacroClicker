using System.ComponentModel;

namespace MacroClicker;

internal sealed class NodeDialog : Form
{
    public MacroNode Node { get; private set; }
    private readonly BindingList<MacroStep> _steps;
    private readonly DataGridView _grid = new();
    private readonly ToolStripStatusLabel _cursor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    public NodeDialog(MacroNode? source = null)
    {
        Node = source?.Copy() ?? new MacroNode();
        _steps = new BindingList<MacroStep>(Node.Steps);
        Text = "Редактор узла · MacroClicker"; ClientSize = new Size(1080, 650); MinimumSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterParent;
        var name = new TextBox { Text = Node.Name, Dock = DockStyle.Fill, AccessibleName = "Название узла" };
        var window = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 700, AccessibleName = "Окно приложения" };
        void RefreshWindows()
        {
            var selected = window.SelectedItem as WindowTarget ?? Node.SessionTarget;
            window.Items.Clear();
            var windows = WindowTargeting.GetVisibleWindows();
            window.Items.AddRange(windows.Cast<object>().ToArray());
            var match = windows.FirstOrDefault(w => w.Handle == selected?.Handle) ??
                windows.FirstOrDefault(w => w.ProcessName == Node.ProcessName && w.Title == Node.WindowTitle);
            if (match is not null) window.SelectedItem = match;
        }
        RefreshWindows();
        var properties = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2, Padding = new Padding(16, 12, 16, 8) };
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        properties.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        properties.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        properties.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        properties.Controls.Add(Ui.Label("Название узла"), 0, 0);
        properties.Controls.Add(name, 1, 0); properties.SetColumnSpan(name, 2);
        properties.Controls.Add(Ui.Label("Окно приложения"), 0, 1);
        properties.Controls.Add(window, 1, 1);
        properties.Controls.Add(Ui.Button("Обновить список", RefreshWindows), 2, 1);
        _grid.Dock = DockStyle.Fill; _grid.DataSource = _steps; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false; _grid.ReadOnly = true;
        _grid.MultiSelect = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowTemplate.Height = 32;
        foreach (var (title, property, width) in new[]
        {
            ("Тип", nameof(MacroStep.DisplayKind), 100), ("Действие", nameof(MacroStep.DisplayInput), 150),
            ("Координаты", nameof(MacroStep.DisplayCoordinates), 100), ("До, мс", nameof(MacroStep.DelayBeforeMs), 65),
            ("Удержание / пауза", nameof(MacroStep.DisplayHold), 100), ("После, мс", nameof(MacroStep.DelayAfterMs), 65)
        }) _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, DataPropertyName = property, FillWeight = width });
        int Index() => _grid.CurrentRow?.Index ?? -1;
        void Select(int i) { if (i >= 0 && i < _steps.Count) _grid.CurrentCell = _grid.Rows[i].Cells[0]; }
        void Add(MacroStep? template = null)
        {
            using var dialog = new StepDialog(template, true);
            if (dialog.ShowDialog(this) == DialogResult.OK) { var i = Index() + 1; _steps.Insert(i, dialog.Step); Select(i); }
        }
        void Edit()
        {
            var i = Index(); if (i < 0) return;
            using var dialog = new StepDialog(_steps[i]);
            if (dialog.ShowDialog(this) == DialogResult.OK) { _steps[i] = dialog.Step; Select(i); }
        }
        void Move(int direction)
        {
            var i = Index(); var next = i + direction;
            if (i < 0 || next < 0 || next >= _steps.Count) return;
            var step = _steps[i]; _steps.RemoveAt(i); _steps.Insert(next, step); Select(next);
        }
        var toolbar = Ui.Row();
        toolbar.Controls.Add(Ui.Button("+ Действие", () => Add()));
        toolbar.Controls.Add(Ui.Button("+ Двойной клик", () => Add(new MacroStep { Kind = InputKind.DoubleClick, Input = "Левая" })));
        toolbar.Controls.Add(Ui.Button("+ Пауза", () => Add(new MacroStep { Kind = InputKind.Pause, HoldMs = 500, DelayAfterMs = 0 })));
        toolbar.Controls.Add(Ui.Button("Изменить", Edit));
        toolbar.Controls.Add(Ui.Button("Дублировать", () => { var i = Index(); if (i >= 0) { _steps.Insert(i + 1, _steps[i].Copy()); Select(i + 1); } }));
        toolbar.Controls.Add(Ui.Button("Удалить", () => { var i = Index(); if (i >= 0) _steps.RemoveAt(i); }));
        toolbar.Controls.Add(Ui.Button("↑", () => Move(-1))); toolbar.Controls.Add(Ui.Button("↓", () => Move(1)));
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Edit(); };
        var footer = Ui.Row(); footer.Dock = DockStyle.Bottom; footer.FlowDirection = FlowDirection.RightToLeft;
        footer.Controls.Add(Ui.Button("Сохранить узел", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            { Ui.Error(this, new InvalidOperationException("Укажите название узла.")); name.Focus(); return; }
            Node.Name = name.Text.Trim();
            if (window.SelectedItem is WindowTarget selected)
            {
                Node.SessionTarget = selected; Node.ProcessName = selected.ProcessName; Node.WindowTitle = selected.Title;
            }
            Node.Steps = _steps.ToList(); DialogResult = DialogResult.OK;
        }));
        var cancel = Ui.Button("Отмена", () => DialogResult = DialogResult.Cancel); footer.Controls.Add(cancel); CancelButton = cancel;
        var status = new StatusStrip(); status.Items.Add(_cursor); status.Dock = DockStyle.Bottom;
        Controls.Add(_grid); Controls.Add(toolbar); Controls.Add(properties); Controls.Add(footer); Controls.Add(status);
        _timer.Tick += (_, _) =>
        {
            if (!WindowTargeting.TryGetCursorScreenPosition(out var p)) return;
            var prefix = window.SelectedItem is WindowTarget t && WindowTargeting.TryGetCursorClientPosition(t, out var c, out var inside)
                ? $"Окно: {c.X}, {c.Y} ({(inside ? "внутри" : "снаружи")})" : "Окно: недоступно";
            _cursor.Text = $"{prefix}    |    Экран: {p.X}, {p.Y}";
        };
        _timer.Start(); FormClosed += (_, _) => _timer.Dispose(); Ui.Theme(this);
    }
}
