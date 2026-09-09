using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MacroClicker;

internal sealed class WebMainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly WebContentPolicy _webContent = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _startupTimer = new() { Interval = 25000 };
    private readonly Panel _startupPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(32) };
    private readonly Label _startupMessage = new() { Dock = DockStyle.Top, AutoSize = false, Height = 180 };
    private readonly TextBox _startupDetails = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both };
    private readonly ProgressBar _startupProgress = new() { Dock = DockStyle.Top, Height = 6, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25 };
    private readonly FlowLayoutPanel _startupButtons = new() { Dock = DockStyle.Bottom, Height = 56, Visible = false };
    private bool _startupFailed, _interfaceRendered;
    private string _startupStage = "Создание окна";
    private MacroDocument _document = new();
    private readonly Dictionary<string, string> _library = [];
    private readonly HashSet<string> _trashIds = [];
    private DateTimeOffset _nextTrashCleanup = DateTimeOffset.MinValue;
    private readonly Dictionary<string, WindowTarget> _windows = [];
    private string? _file;
    private string _status = "Готово к запуску";
    private bool _ready, _dirty, _dialog, _command, _allowClose, _closePending;
    private CancellationTokenSource? _run, _recordStart;
    private InputRecorder? _recorder;
    private WindowTarget? _coordinateTarget;
    private bool Busy => _run is not null || _recorder is not null || _recordStart is not null;

    public WebMainForm()
    {
        Text = "MacroClicker"; ClientSize = new Size(1240, 820); MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Ui.Dark = _settings.DarkTheme; Ui.Theme(this);
        BackColor = _settings.DarkTheme ? Color.FromArgb(33, 33, 33) : Color.FromArgb(250, 249, 247);
        _web.DefaultBackgroundColor = BackColor; Controls.Add(_web);
        _startupButtons.Controls.Add(Ui.Button("Открыть журнал", () => { using var viewer = new LogDialog(); viewer.ShowDialog(this); }));
        _startupDetails.Visible = false;
        _startupPanel.Controls.Add(_startupDetails); _startupPanel.Controls.Add(_startupProgress);
        _startupPanel.Controls.Add(_startupMessage); _startupPanel.Controls.Add(_startupButtons);
        Controls.Add(_startupPanel); Ui.Theme(_startupPanel); _startupPanel.BringToFront();
        _startupMessage.TextAlign = ContentAlignment.MiddleCenter;
        Resize += (_, _) => LayoutLoader(); LayoutLoader();
        LogStartup($"MacroClicker {Application.ProductVersion}; OS {Environment.OSVersion}; process {RuntimeInformation.ProcessArchitecture}");
        StartupStage("Подготовка WebView2");
        _startupTimer.Tick += (_, _) => FailStartup("За 25 секунд интерфейс не подтвердил готовность. Последний этап: " + _startupStage);
        Shown += async (_, _) => await InitializeAsync();
        Application.ThreadException += OnUiException;
        _timer.Tick += (_, _) => Tick(); FormClosing += ClosingAsync;
        FormClosed += (_, _) => { Application.ThreadException -= OnUiException; AppLog.Current.Write("INFO", "Application closed"); _startupTimer.Dispose(); _timer.Dispose(); _recorder?.Dispose(); _web.Dispose(); };
    }
    private void LayoutLoader()
    {
        if (_startupFailed) return;
        _startupPanel.Padding = new Padding(Math.Max(32, ClientSize.Width / 4), Math.Max(32, ClientSize.Height / 3 - 90), Math.Max(32, ClientSize.Width / 4), 32);
    }
    private void OnUiException(object sender, ThreadExceptionEventArgs e)
    {
        AppLog.Current.Write("ERROR", "Unhandled UI exception", e.Exception);
        FailStartup("Необработанная ошибка приложения: " + e.Exception.Message);
    }
    private void LogStartup(string message)
    {
        AppLog.Current.Write("INFO", message);
        if (!IsDisposed) _startupDetails.Text = AppLog.Current.Snapshot();
    }
    private void StartupStage(string stage)
    {
        _startupStage = stage; LogStartup(stage);
        _startupMessage.Text = "MacroClicker\n\n" + stage + "…";
    }
    private void FailStartup(string message)
    {
        if (IsDisposed || Disposing || _startupFailed) return;
        _startupFailed = true; _ready = false; _startupTimer.Stop(); _timer.Stop();
        _run?.Cancel(); _recordStart?.Cancel();
        if (_recorder is not null) FinishRecording();
        AppLog.Current.Write("ERROR", message); _startupDetails.Text = AppLog.Current.Snapshot();
        _startupPanel.Padding = new Padding(32); _startupProgress.Visible = false; _startupProgress.MarqueeAnimationSpeed = 0;
        _startupMessage.TextAlign = ContentAlignment.TopLeft; _startupDetails.Visible = true; _startupButtons.Visible = true;
        _startupMessage.Text = (_interfaceRendered ? "Работа интерфейса остановлена из-за ошибки." : "Не удалось загрузить интерфейс MacroClicker.") + "\n\n" + message +
            "\n\nСкопируйте сведения ниже или отправьте файл журнала. Переустанавливать WebView2 без диагностики не нужно.";
        _web.Visible = false; _startupPanel.Visible = true; _startupPanel.BringToFront();
    }
    private static string Resource(string name)
    {
        using var stream = typeof(WebMainForm).Assembly.GetManifestResourceStream("MacroClicker." + name)
            ?? throw new InvalidOperationException("Отсутствует ресурс " + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private async Task InitializeAsync()
    {
        try
        {
            _startupTimer.Start();
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroClicker", "WebView2");
            StartupStage("Создание среды WebView2");
            LogStartup("Runtime: " + CoreWebView2Environment.GetAvailableBrowserVersionString());
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (_startupFailed || IsDisposed) return;
            StartupStage("Создание контроллера WebView2");
            await _web.EnsureCoreWebView2Async(environment);
            if (_startupFailed || IsDisposed) return;
            StartupStage("Настройка WebView2; Runtime " + environment.BrowserVersionString);
            var core = _web.CoreWebView2;
            ApplyWebTheme(_settings.DarkTheme);
            core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                e.Cancel = _startupFailed || !_webContent.TryStart(e.Uri, e.NavigationId, e.IsRedirected);
                LogStartup($"NavigationStarting: {(e.Cancel ? "blocked" : "allowed")}; id={e.NavigationId}; appPage={WebContentPolicy.IsPage(e.Uri)}; redirected={e.IsRedirected}");
            };
            core.NavigationCompleted += (_, e) =>
            {
                LogStartup($"NavigationCompleted: id={e.NavigationId}; success={e.IsSuccess}; status={e.WebErrorStatus}");
                // A rejected external navigation is not a failure of our own page.
                if (_webContent.IsPageNavigation(e.NavigationId) && !e.IsSuccess)
                    BeginInvoke((Action)(() => FailStartup("Ошибка загрузки страницы: " + e.WebErrorStatus)));
            };
            core.DOMContentLoaded += (_, _) => LogStartup("DOMContentLoaded");
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, e) => BeginInvoke((Action)(() => FailStartup("Процесс WebView2 остановился: " + e.ProcessFailedKind)));
            // Native file/confirmation dialogs must run after the WebView2 event
            // callback returns, without a nested message loop inside that callback.
            core.WebMessageReceived += (_, e) =>
            {
                if (!WebContentPolicy.IsPage(e.Source)) return;
                var json = e.WebMessageAsJson;
                BeginInvoke((Action)(() => Receive(json)));
            };
            StartupStage("Загрузка встроенной страницы");
            var html = Resource("Web.index.html"); LogStartup("HTML characters: " + html.Length);
            var page = System.Text.Encoding.UTF8.GetBytes(html);
            // Intercept all page requests: only the embedded HTML is served.
            // No local server, extracted HTML file, external site or DNS is needed.
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                var allowed = !_startupFailed && WebContentPolicy.CanServe(e.Request.Uri, e.Request.Method);
                e.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(allowed ? page : Array.Empty<byte>(), writable: false),
                    allowed ? 200 : 403, allowed ? "OK" : "Forbidden",
                    "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
                if (allowed) LogStartup("Embedded page response: 200; bytes=" + page.Length);
            };
            core.Navigate(WebContentPolicy.PageUrl);
        }
        catch (Exception error)
        {
            if (!IsDisposed) FailStartup(error.ToString());
        }
    }
    private void Send(object value)
    { if (_ready && !IsDisposed) _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, MacroStorage.Options)); }
    private void Error(Exception error)
    { AppLog.Current.Write("ERROR", "Operation failed", error); _status = error.Message; Send(new { type = "error", message = error.Message }); }
    private void State(bool replaceDocument = true)
    {
        _library.Clear(); var library = new List<object>();
        if (Directory.Exists(MacroStorage.LibraryPath))
            foreach (var path in Directory.GetFiles(MacroStorage.LibraryPath, "*.json").Order())
                try
                {
                    var doc = MacroStorage.Read(path); var id = Path.GetFileName(path); _library[id] = path;
                    library.Add(new { id, name = doc.Name, nodes = doc.Nodes.Count, active = path == _file });
                }
                catch (Exception error)
                {
                    _status = "В библиотеке есть повреждённый JSON; файл сохранён на диске.";
                    AppLog.Current.Write("WARN", "Could not read macro library entry", error);
                }
        var json = MacroEditorDocument.Export(_document);
        Send(new { type = "state", document = replaceDocument ? json : null, library, dirty = _dirty,
            settings = _settings, startHotkey = _settings.StartHotkey.DisplayText, stopHotkey = _settings.StopHotkey.DisplayText,
            doubleClickMs = InputSender.SystemDoubleClickMs, keys = InputSender.SupportedKeys, highContrast = SystemInformation.HighContrast });
        Tick();
    }
    private void SetDocument(JsonElement json)
    {
        _document = MacroEditorDocument.Import(json, _document, _windows);
    }
    private async Task ReadEditorAsync()
    {
        var result = await _web.CoreWebView2.ExecuteScriptAsync("window.exportDocument()");
        using var parsed = JsonDocument.Parse(result);
        if (parsed.RootElement.TryGetProperty("error", out var error)) throw new InvalidDataException(error.GetString());
        SetDocument(parsed.RootElement.GetProperty("document"));
    }
    private void Save(bool copy)
    {
        var path = copy || _file is null ? Path.Combine(MacroStorage.LibraryPath, Guid.NewGuid() + ".json") : _file;
        MacroStorage.Write(path, _document); _file = path; _dirty = false; _status = "Макрос сохранён";
    }
    private async Task<bool> CanReplaceAsync()
    {
        if (!_dirty) return true;
        _dialog = true;
        try
        {
            var answer = MessageBox.Show(this, "Сохранить изменения текущего макроса?", "MacroClicker", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.Yes) { await ReadEditorAsync(); Save(false); }
            return true;
        }
        finally { _dialog = false; }
    }
    private void DeleteMacro(string id)
    {
        if (!_library.TryGetValue(id, out var path))
            throw new InvalidDataException("Макрос не найден в библиотеке. Откройте список заново.");
        var active = path == _file;
        // The themed web dialog sends this command only after explicit confirmation.
        MacroStorage.Archive(MacroStorage.LibraryPath, id);
        _library.Remove(id);
        _status = "Макрос помещён в корзину";
        if (active) { _document = new(); _file = null; _dirty = false; State(); }
        // Do not re-render another open document: it may contain incomplete JSON edits.
        Send(new { type = "libraryDeleted", id });
    }
    private void CleanupTrash()
    {
        _nextTrashCleanup = DateTimeOffset.UtcNow.AddHours(1);
        try
        {
            var count = MacroTrash.PurgeExpired(MacroStorage.LibraryPath, DateTimeOffset.UtcNow,
                error => AppLog.Current.Write("WARN", "Could not expire trash entry", error));
            if (count > 0) AppLog.Current.Write("INFO", $"Expired trash entries: {count}");
        }
        catch (Exception error) { AppLog.Current.Write("WARN", "Could not clean macro trash", error); }
    }
    private void SendTrash()
    {
        var items = MacroTrash.List(MacroStorage.LibraryPath);
        _trashIds.Clear(); foreach (var item in items) _trashIds.Add(item.Id);
        Send(new { type = "trash", items });
    }
    private void RestoreMacro(string id)
    {
        if (!_trashIds.Contains(id)) throw new InvalidDataException("Откройте корзину заново.");
        var path = MacroTrash.Restore(MacroStorage.LibraryPath, id);
        var doc = MacroStorage.Read(path); var restoredId = Path.GetFileName(path);
        _library[restoredId] = path;
        Send(new { type = "libraryRestored", item = new { id = restoredId, name = doc.Name, nodes = doc.Nodes.Count, active = false } });
        SendTrash();
    }
    private void ClearTrash()
    {
        var ids = MacroTrash.List(MacroStorage.LibraryPath).Select(x => x.Id).ToArray();
        if (ids.Length == 0) { SendTrash(); return; }
        var wasDialog = _dialog; _dialog = true;
        try
        {
            if (MessageBox.Show(this, $"Навсегда удалить все макросы из корзины ({ids.Length})?\nВосстановить их будет невозможно. Макросы из основного списка останутся.",
                "Очистить корзину", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { foreach (var id in ids) MacroTrash.DeletePermanently(MacroStorage.LibraryPath, id); }
            finally { SendTrash(); } // Refresh even after a partial failure.
            Send(new { type = "trashCleared" });
        }
        finally { _dialog = wasDialog; }
    }
    private async void Receive(string json)
    {
        try
        {
            using var message = JsonDocument.Parse(json);
            var data = message.RootElement; var command = data.GetProperty("command").GetString();
            if (_startupFailed) return;
            if (command == "startupError")
            { FailStartup("JavaScript: " + data.GetProperty("message").GetString()); return; }
            if (command == "ready")
            {
                LogStartup("JavaScript ready; sending initial state");
                _ready = true;
                try { State(); } catch (Exception error) { FailStartup(error.ToString()); }
                return;
            }
            if (command == "rendered")
            {
                if (!_ready || _interfaceRendered) return;
                _interfaceRendered = true;
                LogStartup("Initial state rendered; startup complete");
                _startupTimer.Stop(); _startupProgress.MarqueeAnimationSpeed = 0; _startupPanel.Visible = false; _web.BringToFront(); _timer.Start(); return;
            }
            if (command == "dirty") { if (!Busy) _dirty = true; return; }
            if (command == "modal") { _dialog = data.GetProperty("open").GetBoolean(); return; }
            if (command == "themePreview") { ApplyWebTheme(data.GetProperty("dark").GetBoolean()); return; }
            if (command == "stop") { _run?.Cancel(); _recordStart?.Cancel(); if (_recorder is not null) FinishRecording(); return; }
            if (command == "logs") { Send(new { type = "logs", text = AppLog.Current.Snapshot() }); return; }
            if (command == "copyLogs") { Clipboard.SetText(AppLog.Current.Snapshot()); Send(new { type = "logsCopied" }); return; }
            if (command == "logsFolder")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(AppLog.Current.FilePath)!) { UseShellExecute = true }); return;
            }
            if (_command) return; _command = true;
            try
            {
                if (Busy) return;
                if (data.TryGetProperty("document", out var document)) SetDocument(document);
                switch (command)
                {
                    case "save": Save(data.TryGetProperty("copy", out var copy) && copy.GetBoolean()); State(); break;
                    case "deleteMacro": DeleteMacro(data.GetProperty("id").GetString() ?? ""); break;
                    case "trash": CleanupTrash(); SendTrash(); break;
                    case "restoreMacro": RestoreMacro(data.GetProperty("id").GetString() ?? ""); break;
                    case "clearTrash": ClearTrash(); break;
                    case "apply": _dirty = true; _status = "JSON применён"; State(); break;
                    case "new":
                        if (await CanReplaceAsync()) { _document = new(); _file = null; _dirty = false; State(); } break;
                    case "open":
                        if (_library.TryGetValue(data.GetProperty("id").GetString()!, out var path) && await CanReplaceAsync())
                        { _document = MacroStorage.Read(path); _file = path; _dirty = false; State(); }
                        break;
                    case "import":
                        if (!await CanReplaceAsync()) break;
                        _dialog = true;
                        try
                        {
                            using var picker = new OpenFileDialog { Filter = "Макрос JSON|*.json" };
                            if (picker.ShowDialog(this) == DialogResult.OK)
                            { _document = MacroStorage.Read(picker.FileName); _file = null; _dirty = true; State(); }
                        }
                        finally { _dialog = false; }
                        break;
                    case "export":
                        _dialog = true;
                        try
                        {
                            using var picker = new SaveFileDialog { Filter = "Макрос JSON|*.json", DefaultExt = "json", FileName = "macro.json" };
                            if (picker.ShowDialog(this) == DialogResult.OK) { MacroStorage.Write(picker.FileName, _document); _status = "JSON экспортирован"; }
                        }
                        finally { _dialog = false; }
                        break;
                    case "windows":
                        var targets = WindowTargeting.GetVisibleWindows(); _windows.Clear();
                        foreach (var target in targets) _windows[target.Handle.ToInt64().ToString()] = target;
                        Send(new { type = "windows", windows = targets.Select(w => new { id = w.Handle.ToInt64().ToString(), title = w.Title, process = w.ProcessName }) }); break;
                    case "coordinate": _coordinateTarget = _windows.GetValueOrDefault(data.GetProperty("id").GetString() ?? ""); break;
                    case "start": if (!_dialog) _ = RunAsync(); break;
                    case "record": if (!_dialog) _ = RecordAsync(); break;
                    case "settings": Configure(data); break;
                    case "help":
                        var topic = data.GetProperty("topic").GetString();
                        if (topic is "QuickStart" or "Manual" or "WindowsChecks" or "TestResults" or "NuGet")
                            Send(new { type = "help", text = Resource("Help." + topic) });
                        break;
                }
            }
            finally { _command = false; Tick(); }
        }
        catch (Exception error) { if (!_interfaceRendered) FailStartup(error.ToString()); else Error(error); }
    }
    private static HotkeyDefinition ParseHotkey(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Length)
            throw new InvalidDataException("Укажите корректное сочетание, например Ctrl+Alt+F8.");
        var definition = new HotkeyDefinition();
        foreach (var part in parts[..^1])
            switch (part.ToUpperInvariant())
            {
                case "CTRL": definition.Control = true; break;
                case "ALT": definition.Alt = true; break;
                case "SHIFT": definition.Shift = true; break;
                case "WIN": definition.Windows = true; break;
                default: throw new InvalidDataException("Перед основной клавишей допустимы Ctrl, Alt, Shift, Win.");
            }
        definition.Key = (Keys)InputSender.ParseChord(parts[^1]).Single();
        if ((int)definition.Key is 16 or 17 or 18 or 91 or 92 or >= 160 and <= 165)
            throw new InvalidDataException("Добавьте основную клавишу после модификаторов.");
        return definition;
    }
    private void Configure(JsonElement data)
    {
        var start = ParseHotkey(data.GetProperty("start").GetString()!);
        var separate = data.GetProperty("separate").GetBoolean();
        var stop = separate ? ParseHotkey(data.GetProperty("stop").GetString()!) : _settings.StopHotkey.Copy();
        if (separate && start.SameAs(stop)) throw new InvalidDataException("Для раздельного режима нужны разные сочетания.");
        var previousStart = _settings.StartHotkey; var previousStop = _settings.StopHotkey; var previousSeparate = _settings.SeparateStopHotkey;
        UnregisterPlayback(); _settings.StartHotkey = start; _settings.StopHotkey = stop; _settings.SeparateStopHotkey = separate;
        if (!RegisterPlayback())
        {
            UnregisterPlayback(); _settings.StartHotkey = previousStart; _settings.StopHotkey = previousStop; _settings.SeparateStopHotkey = previousSeparate;
            RegisterPlayback(); throw new InvalidOperationException("Сочетание занято другой программой.");
        }
        _settings.DarkTheme = data.GetProperty("dark").GetBoolean();
        _settings.Save(); Ui.Dark = _settings.DarkTheme; ApplyWebTheme(_settings.DarkTheme); ApplyTitleBar(); State(false); Send(new { type = "settingsSaved" });
    }
    private void ApplyWebTheme(bool dark)
    {
        // CSS colors the page; native browser popups otherwise follow the OS theme.
        _web.CoreWebView2.Profile.PreferredColorScheme = dark
            ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        _web.DefaultBackgroundColor = dark ? Color.FromArgb(33, 33, 33) : Color.FromArgb(250, 249, 247);
    }
    private async Task RunAsync()
    {
        if (Busy) return; _run = new CancellationTokenSource(); Tick();
        AppLog.Current.Write("INFO", $"Playback requested; nodes={_document.Nodes.Count}; steps={_document.Nodes.Sum(n => n.Steps.Count)}");
        try
        {
            if (_document.AllNodes(enabledOnly: true).Any(n => n.UseActiveWindow))
                for (var remaining = 5; remaining > 0; remaining--)
                {
                    _status = $"Перейдите в нужное приложение · старт через {remaining} с"; Tick();
                    await Task.Delay(1000, _run.Token);
                }
            await Task.Delay(250, _run.Token);
            await new MacroRunner().RunAsync(_document.Copy(), status => { _status = status; Tick(); }, _run.Token);
            AppLog.Current.Write("INFO", "Playback completed");
        }
        catch (OperationCanceledException) { _status = "Остановлено"; AppLog.Current.Write("INFO", "Playback cancelled"); }
        catch (Exception error) { Error(error); }
        finally { _run.Dispose(); _run = null; Tick(); }
    }
    private async Task RecordAsync()
    {
        if (Busy) return;
        if (_document.Nodes.Count == 0) { Error(new InvalidDataException("Добавьте хотя бы одно окно приложения.")); return; }
        if (_document.Nodes.Any(n => n.UseActiveWindow))
        { Error(new InvalidDataException("Для записи явно выберите окна приложений в узлах. Режим активного окна предназначен для воспроизведения.")); return; }
        _dialog = true;
        var answer = MessageBox.Show(this, "Записать действия в окнах текущих узлов?\nНабранный текст тоже попадёт в запись. F10 останавливает запись.\n\nКнопки MacroClicker, сочетания старта/остановки и действия переключения окон не записываются.\nОтпустите зажатые клавиши и кнопки перед рабочими действиями.\n\nРезультат добавится новыми узлами в конец сценария.", "Запись действий", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        _dialog = false; if (answer != DialogResult.OK) return;
        _recordStart = new CancellationTokenSource(); Tick(); UnregisterPlayback();
        try
        {
            var targets = _document.Nodes.Select(WindowTargeting.Resolve).ToArray();
            if (!RegisterHotKey(Handle, 103, 0x4000, 0x79)) throw new InvalidOperationException("F10 занята другой программой.");
            await WindowTargeting.ActivateAsync(targets[0], _recordStart.Token); _recordStart.Token.ThrowIfCancellationRequested();
            _recorder = new InputRecorder(targets, _settings.RecordingControlChords()); _recorder.Start();
            AppLog.Current.Write("INFO", "Recording started");
        }
        catch (OperationCanceledException) { _status = "Запись отменена"; }
        catch (Exception error) { _recorder?.Dispose(); _recorder = null; Error(error); }
        finally
        {
            _recordStart.Dispose(); _recordStart = null;
            if (_recorder is null) { UnregisterHotKey(Handle, 103); RegisterPlayback(); } Tick();
        }
    }
    private void FinishRecording()
    {
        if (_recorder is null) return; var recorder = _recorder; _recorder = null;
        try
        {
            var nodes = recorder.Finish();
            if (nodes.Count > 0) { _document.Nodes.AddRange(nodes); _document.SafeMousePauses = false; _dirty = true; }
            _status = $"Запись завершена · добавлено узлов: {nodes.Count}";
            AppLog.Current.Write("INFO", $"Recording completed; nodes={nodes.Count}");
        }
        catch (Exception error) { Error(error); }
        finally { recorder.Dispose(); UnregisterHotKey(Handle, 103); RegisterPlayback(); State(); }
    }
    private void Tick()
    {
        if (_ready && !Busy && !_dialog && !_command && DateTimeOffset.UtcNow >= _nextTrashCleanup) CleanupTrash();
        if (!_ready) return;
        if (_recorder?.Full == true) { FinishRecording(); return; }
        var status = _recorder is not null ? $"Идёт запись · событий: {_recorder.Count} · F10 — остановить" : _status;
        var coordinates = "";
        if (WindowTargeting.TryGetCursorScreenPosition(out var point))
        {
            var first = _document.Nodes.FirstOrDefault();
            var target = _coordinateTarget ?? (first?.UseActiveWindow == false ? first.SessionTarget : null);
            coordinates = target is not null && WindowTargeting.TryGetCursorClientPosition(target, out var client, out _)
                ? $"Окно: {client.X}, {client.Y}   ·   Экран: {point.X}, {point.Y}" : $"Экран: {point.X}, {point.Y}";
        }
        Send(new { type = "runtime", busy = Busy, running = _run is not null, recording = _recorder is not null || _recordStart is not null, status, coordinates });
    }
    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || (!_ready && !Busy)) return; e.Cancel = true;
        if (_closePending) return; _closePending = true;
        try
        {
            _run?.Cancel(); _recordStart?.Cancel();
            while (_run is not null || _recordStart is not null) await Task.Delay(50);
            if (_recorder is not null) FinishRecording();
            if (_dialog) { Send(new { type = "error", message = "Сначала сохраните или отмените изменения в открытом редакторе." }); return; }
            if (await CanReplaceAsync()) { _allowClose = true; Close(); }
        }
        catch (Exception error) { Error(error); }
        finally { _closePending = false; }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); ApplyTitleBar();
        if (!RegisterPlayback()) _status = "Горячая клавиша занята — измените её в настройках.";
    }
    private void ApplyTitleBar()
    { try { var dark = _settings.DarkTheme ? 1 : 0; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { } }
    protected override void OnHandleDestroyed(EventArgs e)
    { UnregisterPlayback(); UnregisterHotKey(Handle, 103); base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312)
        {
            var id = m.WParam.ToInt32();
            if (id == 103) { _recordStart?.Cancel(); if (_recorder is not null) FinishRecording(); }
            else if (id == 102) _run?.Cancel();
            else if (id == 101)
            {
                if (_run is not null) { if (!_settings.SeparateStopHotkey) _run.Cancel(); }
                else if (_ready && _interfaceRendered && !Busy && !_dialog && !_command) BeginInvoke((Action)(async () =>
                {
                    if (_command || Busy || _dialog) return; _command = true;
                    try { await ReadEditorAsync(); _ = RunAsync(); } catch (Exception error) { Error(error); }
                    finally { _command = false; }
                }));
            }
        }
        base.WndProc(ref m);
    }
    private bool RegisterPlayback()
    {
        if (!RegisterHotKey(Handle, 101, _settings.StartHotkey.Modifiers, (uint)_settings.StartHotkey.Key)) return false;
        if (!_settings.SeparateStopHotkey || RegisterHotKey(Handle, 102, _settings.StopHotkey.Modifiers, (uint)_settings.StopHotkey.Key)) return true;
        UnregisterHotKey(Handle, 101); return false;
    }
    private void UnregisterPlayback() { UnregisterHotKey(Handle, 101); UnregisterHotKey(Handle, 102); }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
