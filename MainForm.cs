using System.Runtime.InteropServices;
using System.Text.Json;

namespace MacroClicker;

internal sealed class MainForm : Form
{
    private MacroDocument _document = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ListBox _library = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _name = new() { Width = 270 };
    private readonly NumericUpDown _repeats = Ui.Number(1, 1, 100000);
    private readonly CheckBox _forever = new() { Text = "Бесконечно", AutoSize = true };
    private readonly FlowLayoutPanel _nodes = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16) };
    private readonly TabControl _views = new() { Dock = DockStyle.Fill };
    private readonly JsonEditor _json = new();
    private readonly ToolStripStatusLabel _status = new("Готово") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _cursor = new();
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _record;
    private readonly Label _hotkeyHint = Ui.Label("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly List<Control> _editing = [];
    private readonly MenuStrip _menu = new();
    private CancellationTokenSource? _run;
    private InputRecorder? _recorder;
    private bool _recordStarting;
    private bool _recordAbort;
    private bool _dirty;
    private bool _loading;
    private bool _closing;
    private string? _libraryFile;
    private WindowTarget? _coordinateTarget;
    private bool Busy => _run is not null || _recorder is not null || _recordStarting;
    private sealed record LibraryItem(string Name, string Path) { public override string ToString() => Name; }

    public MainForm()
    {
        Text = "MacroClicker"; ClientSize = new Size(1200, 780); MinimumSize = new Size(950, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Ui.Dark = _settings.DarkTheme;
        _hotkeyHint.Tag = "muted";
        _start = Ui.Button("▶\nСТАРТ", () => _ = ToggleRunAsync());
        _start.AutoSize = false; _start.Size = new Size(112, 112);
        _stop = Ui.Button("■ Стоп", () => _run?.Cancel());
        _record = Ui.Button("● Записать действия", () => _ = ToggleRecordAsync());
        BuildMenu();
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 1, Size = new Size(1200, 650), SplitterDistance = 240, Panel1MinSize = 210, Panel2MinSize = 620 };
        var libraryHeader = Ui.Row(); libraryHeader.Controls.Add(Ui.Label("Мои макросы"));
        var libraryButtons = Ui.Row(); libraryButtons.Controls.Add(Ui.Button("+ Новый", NewMacro));
        libraryButtons.Controls.Add(Ui.Button("Открыть", OpenSelected));
        split.Panel1.Padding = new Padding(12);
        split.Panel1.Controls.Add(_library); split.Panel1.Controls.Add(libraryButtons); split.Panel1.Controls.Add(libraryHeader);
        _library.DoubleClick += (_, _) => OpenSelected();
        var heading = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(12, 12, 12, 4) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _name.Dock = DockStyle.Fill; _name.AccessibleName = "Название макроса";
        heading.Controls.Add(Ui.Label("Название"), 0, 0); heading.Controls.Add(_name, 1, 0);
        heading.Controls.Add(Ui.Button("Сохранить", () => SaveLibrary(false)), 2, 0);
        heading.Controls.Add(Ui.Button("Сохранить копию", () => SaveLibrary(true)), 3, 0);
        var commands = Ui.Row();
        commands.Controls.Add(Ui.Button("+ Узел приложения", () => EditNode(-1)));
        commands.Controls.Add(_record);
        var visual = new TabPage("Визуально") { Padding = new Padding(0) };
        visual.Controls.Add(_nodes);
        var jsonPage = new TabPage("JSON") { Padding = new Padding(12) };
        var jsonCommands = Ui.Row();
        jsonCommands.Controls.Add(Ui.Button("Применить JSON", () => ApplyJson()));
        jsonCommands.Controls.Add(Ui.Button("Форматировать", () =>
        {
            try { using var parsed = JsonDocument.Parse(_json.Json); _json.SetJson(JsonSerializer.Serialize(parsed.RootElement, MacroStorage.Options)); _dirty = true; }
            catch (Exception e) { Ui.Error(this, e); }
        }));
        jsonCommands.Controls.Add(Ui.Label("Изменения проверяются перед переходом к узлам и запуском."));
        jsonPage.Controls.Add(_json); jsonPage.Controls.Add(jsonCommands);
        _views.TabPages.Add(visual); _views.TabPages.Add(jsonPage);
        _views.Selecting += (_, e) =>
        {
            if (e.TabPageIndex == 0 && _views.SelectedIndex == 1 && !ApplyJson()) e.Cancel = true;
            if (e.TabPageIndex == 1) { PullFields(); _json.SetJson(JsonSerializer.Serialize(_document, MacroStorage.Options)); }
        };
        _views.SelectedIndexChanged += (_, _) => UpdateState();
        split.Panel2.Controls.Add(_views); split.Panel2.Controls.Add(commands); split.Panel2.Controls.Add(heading);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(20, 12, 20, 12), ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var runLayout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0, 8, 12, 0) };
        runLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); runLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var runOptions = Ui.Row();
        runOptions.WrapContents = false;
        _repeats.Width = 100; _repeats.Anchor = AnchorStyles.Left; _repeats.Margin = new Padding(4);
        runOptions.Controls.Add(Ui.Label("Повторить")); runOptions.Controls.Add(_repeats); runOptions.Controls.Add(_forever); runOptions.Controls.Add(_stop);
        runLayout.Controls.Add(runOptions, 0, 0); runLayout.Controls.Add(_hotkeyHint, 0, 1);
        _start.Anchor = AnchorStyles.Right;
        bottom.Controls.Add(runLayout, 0, 0); bottom.Controls.Add(_start, 1, 0);
        var status = new StatusStrip(); status.Items.Add(_status); status.Items.Add(_cursor);
        Controls.Add(split); Controls.Add(bottom); Controls.Add(status); Controls.Add(_menu); MainMenuStrip = _menu;
        _editing.AddRange([_menu, _library, heading, libraryButtons, commands.Controls[0], _views, _repeats, _forever]);
        _name.TextChanged += (_, _) => { if (!_loading) _dirty = true; };
        _repeats.ValueChanged += (_, _) => { if (!_loading) _dirty = true; };
        _forever.CheckedChanged += (_, _) => { if (!_loading) _dirty = true; _repeats.Enabled = !_forever.Checked && !Busy; };
        _nodes.SizeChanged += (_, _) => SizeCards();
        _timer.Tick += (_, _) => TickStatus();
        _timer.Start();
        FormClosing += OnClosing;
        FormClosed += (_, _) => { _timer.Dispose(); _recorder?.Dispose(); };
        RefreshLibrary(); LoadDocument(new MacroDocument(), null); Ui.Theme(this); UpdateState();
    }
    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("Файл");
        file.DropDownItems.Add("Импорт JSON…", null, (_, _) => Import());
        file.DropDownItems.Add("Экспорт JSON…", null, (_, _) => Export());
        var settings = new ToolStripMenuItem("Настройки");
        settings.DropDownItems.Add("Параметры ввода…", null, (_, _) => ConfigureInput());
        settings.DropDownItems.Add("Горячие клавиши…", null, (_, _) => ConfigureHotkeys());
        var dark = new ToolStripMenuItem("Тёмная тема") { CheckOnClick = true, Checked = _settings.DarkTheme };
        dark.CheckedChanged += (_, _) =>
        {
            _settings.DarkTheme = dark.Checked; Ui.Dark = dark.Checked;
            try { _settings.Save(); } catch (Exception e) { Ui.Error(this, e); }
            Ui.Theme(this); RenderNodes(); _json.RefreshColors(); UpdateState();
        };
        settings.DropDownItems.Add(dark);
        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add("Как пользоваться", null, (_, _) =>
        {
            using var dialog = new HelpDialog();
            dialog.ShowDialog(this);
        });
        _menu.Items.AddRange([file, settings, help]);
    }
    private void PullFields()
    {
        _document.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Новый макрос" : _name.Text.Trim();
        _document.RepeatCount = (int)_repeats.Value; _document.RepeatForever = _forever.Checked;
    }
    private void LoadDocument(MacroDocument doc, string? file)
    {
        _document = doc; _libraryFile = file;
        _loading = true; _name.Text = doc.Name; _repeats.Value = doc.RepeatCount; _forever.Checked = doc.RepeatForever; _loading = false;
        _json.SetJson(JsonSerializer.Serialize(doc, MacroStorage.Options));
        _dirty = false; RenderNodes();
    }
    private bool ApplyJson()
    {
        if (_views.SelectedIndex != 1) return true;
        try
        {
            var parsed = JsonSerializer.Deserialize<MacroDocument>(_json.Json, MacroStorage.Options)
                ?? throw new InvalidDataException("JSON пуст.");
            if (parsed.Nodes is null || parsed.Steps is null) throw new InvalidDataException("Нужен список Nodes.");
            if (parsed.Version != 2) throw new InvalidDataException("Для редактора JSON нужна Version: 2. Старые файлы открываются через импорт.");
            MacroStorage.Validate(parsed);
            foreach (var node in parsed.Nodes)
                node.SessionTarget = _document.Nodes.FirstOrDefault(n => n.ProcessName == node.ProcessName && n.WindowTitle == node.WindowTitle)?.SessionTarget;
            var changed = _json.Json != JsonSerializer.Serialize(_document, MacroStorage.Options);
            _document = parsed;
            _loading = true; _name.Text = parsed.Name; _repeats.Value = parsed.RepeatCount; _forever.Checked = parsed.RepeatForever; _loading = false;
            _dirty |= changed; RenderNodes(); _status.Text = "JSON применён";
            return true;
        }
        catch (Exception e) { Ui.Error(this, e); return false; }
    }
    private bool CanReplace()
    {
        if (!_dirty && !_json.Dirty) return true;
        var result = MessageBox.Show(this, "Сохранить изменения текущего макроса?", "MacroClicker",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        return result == DialogResult.No || SaveLibrary(false);
    }
    private void NewMacro() { if (!Busy && CanReplace()) LoadDocument(new MacroDocument(), null); }
    private void OpenSelected()
    {
        if (Busy || _library.SelectedItem is not LibraryItem item || !CanReplace()) return;
        try { LoadDocument(MacroStorage.Read(item.Path), item.Path); } catch (Exception e) { Ui.Error(this, e); }
    }
    private bool SaveLibrary(bool copy)
    {
        if (Busy || !ApplyJson()) return false;
        try
        {
            PullFields();
            var path = copy || _libraryFile is null ? Path.Combine(MacroStorage.LibraryPath, Guid.NewGuid() + ".json") : _libraryFile;
            MacroStorage.Write(path, _document); _libraryFile = path; _dirty = false;
            _json.SetJson(JsonSerializer.Serialize(_document, MacroStorage.Options));
            RefreshLibrary(); _status.Text = "Макрос сохранён в библиотеку"; return true;
        }
        catch (Exception e) { Ui.Error(this, e); return false; }
    }
    private void RefreshLibrary()
    {
        _library.Items.Clear();
        if (!Directory.Exists(MacroStorage.LibraryPath)) return;
        foreach (var path in Directory.GetFiles(MacroStorage.LibraryPath, "*.json").Order())
        {
            try
            {
                var item = new LibraryItem(MacroStorage.Read(path).Name, path); _library.Items.Add(item);
                if (path == _libraryFile) _library.SelectedItem = item;
            }
            catch { _status.Text = "В библиотеке есть повреждённый JSON; файл сохранён на диске."; }
        }
    }
    private void Import()
    {
        if (Busy || !CanReplace()) return;
        using var dialog = new OpenFileDialog { Filter = "Макрос JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { LoadDocument(MacroStorage.Read(dialog.FileName), null); _dirty = true; }
        catch (Exception e) { Ui.Error(this, e); }
    }
    private void Export()
    {
        if (Busy || !ApplyJson()) return;
        using var dialog = new SaveFileDialog { Filter = "Макрос JSON|*.json", DefaultExt = "json", FileName = "macro.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { PullFields(); MacroStorage.Write(dialog.FileName, _document); _status.Text = "JSON экспортирован"; }
        catch (Exception e) { Ui.Error(this, e); }
    }
    private void EditNode(int index)
    {
        if (Busy || !ApplyJson()) return;
        using var dialog = new NodeDialog(index < 0 ? null : _document.Nodes[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (index < 0) _document.Nodes.Add(dialog.Node); else _document.Nodes[index] = dialog.Node;
        _coordinateTarget = dialog.Node.SessionTarget; Changed();
    }
    private void Changed()
    {
        _dirty = true; RenderNodes();
        if (_views.SelectedIndex == 1) { PullFields(); _json.SetJson(JsonSerializer.Serialize(_document, MacroStorage.Options)); }
    }
    private void RenameNode(int index)
    {
        if (Busy || !ApplyJson()) return;
        var node = _document.Nodes[index];
        using var dialog = new Form
        {
            Text = "Название узла", ClientSize = new Size(560, 190),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
        var name = new TextBox { Text = node.Name, Dock = DockStyle.Fill, AccessibleName = "Название узла" };
        layout.Controls.Add(Ui.Label("Название узла не меняет назначенное ему окно приложения."), 0, 0);
        layout.Controls.Add(name, 0, 1);
        var buttons = Ui.Row(); buttons.FlowDirection = FlowDirection.RightToLeft;
        var save = Ui.Button("Сохранить", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            node.Name = name.Text.Trim(); dialog.DialogResult = DialogResult.OK;
        });
        var cancel = Ui.Button("Отмена", () => dialog.DialogResult = DialogResult.Cancel);
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { name.Focus(); name.SelectAll(); };
        Ui.Theme(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK) Changed();
    }
    private void RenderNodes()
    {
        _nodes.SuspendLayout();
        foreach (Control old in _nodes.Controls.Cast<Control>().ToArray()) { _nodes.Controls.Remove(old); old.Dispose(); }
        if (_document.Nodes.Count == 0)
        {
            var empty = Ui.Label("Создайте первый узел приложения\n\nУзел выбирает окно и выполняет свои действия.\nДобавьте следующий узел, чтобы переключиться в другое окно.");
            empty.Padding = new Padding(24); _nodes.Controls.Add(empty);
        }
        for (var i = 0; i < _document.Nodes.Count; i++)
        {
            var index = i; var node = _document.Nodes[i];
            var card = new TableLayoutPanel { Width = 660, AutoSize = true, ColumnCount = 1, Padding = new Padding(16), Margin = new Padding(0), Tag = "card", BorderStyle = BorderStyle.FixedSingle };
            var title = Ui.Label($"{i + 1:00}    {node.Name}"); title.Font = new Font("Segoe UI", 12, FontStyle.Bold); card.Controls.Add(title);
            var subtitle = Ui.Label($"Окно приложения: {(string.IsNullOrEmpty(node.WindowTitle) ? "не выбрано" : node.WindowTitle)}");
            subtitle.Tag = "muted";
            subtitle.MaximumSize = new Size(650, 0); card.Controls.Add(subtitle);
            var summary = Ui.Label($"{node.Steps.Count} действий" + (string.IsNullOrEmpty(node.ProcessName) ? "" : $"    ·    {node.ProcessName}"));
            summary.Tag = "muted";
            card.Controls.Add(summary);
            var actions = Ui.Row();
            actions.Controls.Add(Ui.Button("Редактировать", () => EditNode(index)));
            actions.Controls.Add(Ui.Button("Переименовать", () => RenameNode(index)));
            var up = Ui.Button("↑", () => MoveNode(index, -1)); up.MinimumSize = new Size(36, 36);
            up.AccessibleName = "Переместить узел вверх"; up.Enabled = index > 0;
            var down = Ui.Button("↓", () => MoveNode(index, 1)); down.MinimumSize = new Size(36, 36);
            down.AccessibleName = "Переместить узел вниз"; down.Enabled = index < _document.Nodes.Count - 1;
            actions.Controls.Add(up); actions.Controls.Add(down);
            var moreMenu = new ContextMenuStrip();
            moreMenu.Items.Add("Дублировать узел", null, (_, _) => { if (!Busy) { _document.Nodes.Insert(index + 1, node.Copy()); Changed(); } });
            moreMenu.Items.Add("Удалить узел…", null, (_, _) =>
            {
                if (!Busy && MessageBox.Show(this, $"Удалить узел «{node.Name}» и его действия?", "MacroClicker", MessageBoxButtons.YesNo) == DialogResult.Yes)
                { _document.Nodes.RemoveAt(index); Changed(); }
            });
            Button? more = null;
            more = Ui.Button("Ещё ▾", () => moreMenu.Show(more!, new Point(0, more!.Height)));
            actions.Controls.Add(more); Ui.Theme(moreMenu);
            card.Disposed += (_, _) => moreMenu.Dispose();
            card.Controls.Add(actions); card.Click += (_, _) => _coordinateTarget = node.SessionTarget;
            _nodes.Controls.Add(card); Ui.Theme(card); card.BackColor = Ui.Surface;
            if (i < _document.Nodes.Count - 1)
            {
                var arrow = Ui.Label("↓    Переход к следующему окну"); arrow.Padding = new Padding(24, 4, 0, 4);
                arrow.Tag = "muted";
                arrow.ForeColor = Ui.Muted; _nodes.Controls.Add(arrow);
            }
        }
        SizeCards(); _nodes.ResumeLayout();
    }
    private void SizeCards()
    {
        foreach (Control card in _nodes.Controls)
            if (Equals(card.Tag, "card"))
            {
                var width = Math.Max(400, _nodes.ClientSize.Width - 56);
                card.Width = width;
                foreach (Control child in card.Controls)
                    if (child is Label) child.MaximumSize = new Size(width - 40, 0);
            }
    }
    private void MoveNode(int index, int delta)
    {
        var next = index + delta;
        if (Busy || next < 0 || next >= _document.Nodes.Count) return;
        var node = _document.Nodes[index]; _document.Nodes.RemoveAt(index); _document.Nodes.Insert(next, node); Changed();
    }
    private void ConfigureInput()
    {
        if (!ApplyJson()) return;
        using var dialog = new Form { Text = "Параметры ввода", ClientSize = new Size(660, 400), StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var check = new CheckBox { Text = "Системные паузы мыши", AutoSize = true, Checked = _document.SafeMousePauses };
        panel.Controls.Add(check);
        var info = Ui.Label($"Текущий порог двойного клика Windows: {InputSender.SystemDoubleClickMs} мс.\nМинимальный интервал при включении: {InputSender.SafeMousePauseMs} мс.\n\nWindows не требует паузы между обычными кликами. Эта настройка разделяет их, чтобы избежать двойного клика. Используется реальное значение ОС + 1 мс, без фиксированного минимума 600 мс.\n\nВаши паузы засчитываются в интервал. Внутри действия «Двойной клик мышью» эта пауза не применяется. Для точного воспроизведения записи отключите настройку.");
        info.MaximumSize = new Size(590, 0); panel.Controls.Add(info);
        panel.Controls.Add(Ui.Button("Применить", () => { _document.SafeMousePauses = check.Checked; Changed(); dialog.DialogResult = DialogResult.OK; }));
        dialog.Controls.Add(panel); Ui.Theme(dialog); dialog.ShowDialog(this);
    }
    private void ConfigureHotkeys()
    {
        using var dialog = new HotkeyDialog(_settings.SeparateStopHotkey, _settings.StartHotkey, _settings.StopHotkey);
        Ui.Theme(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var previousStart = _settings.StartHotkey; var previousStop = _settings.StopHotkey; var previousMode = _settings.SeparateStopHotkey;
        UnregisterPlayback();
        _settings.StartHotkey = dialog.StartHotkey; _settings.StopHotkey = dialog.StopHotkey; _settings.SeparateStopHotkey = dialog.SeparateStopHotkey;
        if (!RegisterPlayback())
        {
            UnregisterPlayback(); _settings.StartHotkey = previousStart; _settings.StopHotkey = previousStop; _settings.SeparateStopHotkey = previousMode; RegisterPlayback();
            Ui.Error(this, new InvalidOperationException("Сочетание занято другой программой.")); return;
        }
        try { _settings.Save(); } catch (Exception e) { Ui.Error(this, e); }
        UpdateState();
    }
    private async Task ToggleRunAsync()
    {
        if (_run is not null) { _run.Cancel(); return; }
        if (Busy || !ApplyJson()) return;
        PullFields();
        _run = new CancellationTokenSource(); UpdateState();
        try
        {
            await Task.Delay(250, _run.Token);
            await new MacroRunner().RunAsync(_document.Copy(), s => _status.Text = s, _run.Token);
        }
        catch (OperationCanceledException) { _status.Text = "Остановлено"; }
        catch (Exception e) { _status.Text = "Остановлено: " + e.Message; if (!_closing) Ui.Error(this, e); }
        finally { _run.Dispose(); _run = null; UpdateState(); if (_closing) Close(); }
    }
    private async Task ToggleRecordAsync()
    {
        if (_recorder is not null) { FinishRecording(); return; }
        if (_recordStarting) { _recordAbort = true; return; }
        if (Busy || !ApplyJson()) return;
        if (_document.Nodes.Count == 0) { Ui.Error(this, new InvalidOperationException("Сначала добавьте окна приложений через узлы.")); return; }
        if (MessageBox.Show(this, "Записать действия только в окнах текущих узлов?\n\nЗапись начнётся после переключения в первое окно. F10 останавливает запись. Текст, набранный в этих окнах, тоже попадёт в запись.\n\nРезультат будет добавлен новыми узлами в конец сценария; системные паузы будут отключены для сохранения интервалов.",
            "Начать запись", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        _recordStarting = true; _recordAbort = false; UpdateState(); UnregisterPlayback();
        try
        {
            var targets = _document.Nodes.Select(WindowTargeting.Resolve).ToArray();
            if (!RegisterHotKey(Handle, 103, 0x4000, 0x79)) throw new InvalidOperationException("F10 занята другой программой. Освободите её перед записью.");
            await WindowTargeting.ActivateAsync(targets[0], CancellationToken.None);
            if (_closing || _recordAbort) return;
            _recorder = new InputRecorder(targets, _settings.RecordingControlChords()); _recorder.Start();
            _status.Text = "● ИДЁТ ЗАПИСЬ · F10 — остановить";
        }
        catch (Exception e) { _recorder?.Dispose(); _recorder = null; if (!_closing) Ui.Error(this, e); }
        finally
        {
            _recordStarting = false;
            if (_recorder is null) { UnregisterHotKey(Handle, 103); RegisterPlayback(); }
            UpdateState(); if (_closing) Close();
        }
    }
    private void FinishRecording()
    {
        if (_recorder is null) return;
        var recorder = _recorder; _recorder = null;
        try
        {
            var result = recorder.Finish();
            if (result.Count > 0)
            {
                _document.Nodes.AddRange(result); _document.SafeMousePauses = false; Changed();
            }
            _status.Text = $"Запись завершена · добавлено узлов: {result.Count}, событий: {recorder.Count}";
        }
        finally { recorder.Dispose(); UnregisterHotKey(Handle, 103); RegisterPlayback(); UpdateState(); }
    }
    private void UpdateState()
    {
        foreach (var control in _editing) control.Enabled = !Busy;
        _name.Enabled = !Busy && _views.SelectedIndex == 0;
        _forever.Enabled = !Busy && _views.SelectedIndex == 0;
        _repeats.Enabled = !Busy && !_forever.Checked && _views.SelectedIndex == 0;
        _start.Enabled = _recorder is null && !_recordStarting;
        _start.Text = _run is null ? "▶\nСТАРТ" : "■\nСТОП";
        _start.BackColor = Ui.Ink; _start.ForeColor = Ui.Surface;
        _start.UseVisualStyleBackColor = false;
        _start.FlatAppearance.MouseOverBackColor = SystemInformation.HighContrast
            ? Ui.Ink : Ui.Dark ? Color.FromArgb(215, 215, 215) : Color.FromArgb(55, 55, 55);
        _start.FlatAppearance.MouseDownBackColor = SystemInformation.HighContrast
            ? Ui.Ink : Ui.Dark ? Color.FromArgb(195, 195, 195) : Color.FromArgb(72, 72, 72);
        _stop.Enabled = _run is not null; _stop.Visible = _settings.SeparateStopHotkey;
        _record.Enabled = _run is null;
        _record.Text = _recordStarting ? "Отменить начало записи" : _recorder is null ? "● Записать действия" : "■ Завершить запись (F10)";
        _start.AccessibleName = "Запуск и остановка макроса: " + _settings.StartHotkey.DisplayText;
        _stop.Text = "■ Стоп · " + _settings.StopHotkey.DisplayText;
        _hotkeyHint.Text = _settings.SeparateStopHotkey
            ? $"Старт: {_settings.StartHotkey.DisplayText}   ·   Стоп: {_settings.StopHotkey.DisplayText}"
            : $"Старт / стоп: {_settings.StartHotkey.DisplayText}";
    }
    private void TickStatus()
    {
        if (_recorder is not null)
        {
            _status.Text = $"● ИДЁТ ЗАПИСЬ · событий: {_recorder.Count} · F10 — остановить";
            if (_recorder.Full) { FinishRecording(); _status.Text += " · достигнут лимит 50 000 событий"; }
        }
        if (!WindowTargeting.TryGetCursorScreenPosition(out var p)) return;
        var target = _coordinateTarget ?? _document.Nodes.FirstOrDefault()?.SessionTarget;
        _cursor.Text = target is not null && WindowTargeting.TryGetCursorClientPosition(target, out var c, out _)
            ? $"Окно: {c.X}, {c.Y}   |   Экран: {p.X}, {p.Y}" : $"Экран: {p.X}, {p.Y}";
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_run is not null || _recordStarting)
        {
            _closing = true; _run?.Cancel(); e.Cancel = true; return;
        }
        if (_recorder is not null) FinishRecording();
        if (!CanReplace()) { e.Cancel = true; _closing = false; }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterPlayback()) _status.Text = "Горячая клавиша занята. Измените сочетание в настройках.";
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterPlayback(); UnregisterHotKey(Handle, 103); base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            if (m.WParam.ToInt32() == 103)
            {
                if (_recorder is not null) FinishRecording();
                else if (_recordStarting) _recordAbort = true;
            }
            else if (!ModalFormOpen())
            {
                if (m.WParam.ToInt32() == 101)
                {
                    if (_run is null || !_settings.SeparateStopHotkey) _ = ToggleRunAsync();
                }
                else if (m.WParam.ToInt32() == 102) _run?.Cancel();
            }
        }
        base.WndProc(ref m);
    }
    private bool ModalFormOpen() => OwnedForms.Any(f => f.Visible);
    private bool RegisterPlayback()
    {
        if (!RegisterHotKey(Handle, 101, _settings.StartHotkey.Modifiers, (uint)_settings.StartHotkey.Key)) return false;
        if (!_settings.SeparateStopHotkey || RegisterHotKey(Handle, 102, _settings.StopHotkey.Modifiers, (uint)_settings.StopHotkey.Key)) return true;
        UnregisterHotKey(Handle, 101); return false;
    }
    private void UnregisterPlayback() { UnregisterHotKey(Handle, 101); UnregisterHotKey(Handle, 102); }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
