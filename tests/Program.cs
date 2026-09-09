using System.Diagnostics;
using System.Text.Json;
using MacroClicker;

static MacroStep Key(string value = "A", ButtonAction action = ButtonAction.Tap, int hold = 1) =>
    new() { Kind = InputKind.Key, Input = value, Action = action, HoldMs = hold, DelayAfterMs = 0 };
static MacroStep Mouse(int delay = 0) =>
    new() { Kind = InputKind.Mouse, Input = "Правая", HoldMs = 1, DelayAfterMs = delay };
static MacroStep DoubleClick() =>
    new() { Kind = InputKind.DoubleClick, Input = "Левая", DelayAfterMs = 0 };
static MacroDocument Doc(params MacroStep[] steps) =>
    new() { Nodes = [new MacroNode { Name = "A", Steps = steps.ToList() }], SafeMousePauses = false };
static void Assert(bool condition, string description)
{ if (!condition) throw new Exception(description); }
static Task Run(MacroDocument doc, CancellationToken token = default) =>
    new MacroRunner().RunAsync(doc, _ => { }, token);
static async Task MustFail<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Корзина: срок ровно 30 дней считается от удаления, не от изменения JSON", () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-trash-check-");
        try
        {
            var deleted = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
            var source = Path.Combine(directory.FullName, "old.json");
            MacroStorage.Write(source, new() { Name = "Старый файл" });
            File.SetLastWriteTimeUtc(source, deleted.AddYears(-2).UtcDateTime);
            var archived = MacroStorage.Archive(directory.FullName, "old.json", deleted);
            var item = MacroTrash.List(directory.FullName).Single();
            Assert(item.DeletedAt == deleted && item.ExpiresAt == deleted.AddDays(30), "Wrong retention timestamp");
            Assert(MacroTrash.PurgeExpired(directory.FullName, deleted.AddDays(30).AddTicks(-1)) == 0 && File.Exists(archived), "Premature expiry");
            Assert(MacroTrash.PurgeExpired(directory.FullName, deleted.AddDays(30)) == 1 && !File.Exists(archived), "Boundary expiry failed");
        }
        finally { Directory.Delete(directory.FullName, true); }
        return Task.CompletedTask;
    }),
    ("Корзина: восстановление не перезаписывает одноимённые макросы", () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-trash-check-");
        try
        {
            var first = Path.Combine(directory.FullName, "one.json");
            MacroStorage.Write(first, new() { Name = "Имя" }); var content = File.ReadAllText(first);
            var archived = MacroStorage.Archive(directory.FullName, "one.json");
            MacroStorage.Write(first, new() { Name = "Имя" });
            var restored = MacroTrash.Restore(directory.FullName, Path.GetFileName(archived));
            Assert(restored != first && File.Exists(first) && File.ReadAllText(restored) == content, "Restore replaced another macro");
            Assert(MacroTrash.List(directory.FullName).Count == 0, "Restored macro remains in trash");
        }
        finally { Directory.Delete(directory.FullName, true); }
        return Task.CompletedTask;
    }),
    ("Корзина: очистка ограничена JSON корзины, неизвестная дата не удаляется автоматически", async () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-trash-check-");
        try
        {
            MacroStorage.Write(Path.Combine(directory.FullName, "keep.json"), new());
            Directory.CreateDirectory(Path.Combine(directory.FullName, "Deleted"));
            var unknown = Path.Combine(directory.FullName, "Deleted", "legacy.json");
            File.WriteAllText(unknown, "invalid JSON");
            var note = Path.Combine(directory.FullName, "Deleted", "keep.txt"); File.WriteAllText(note, "note");
            Assert(MacroTrash.List(directory.FullName).Single().ExpiresAt is null, "Guessed deletion date");
            Assert(MacroTrash.PurgeExpired(directory.FullName, DateTimeOffset.UtcNow.AddYears(5)) == 0, "Unknown age removed automatically");
            await MustFail<System.Text.Json.JsonException>(() => { MacroTrash.Restore(directory.FullName, "legacy.json"); return Task.CompletedTask; });
            foreach (var id in new[] { "../keep.json", "..\\keep.json", "C:keep.json", "keep.txt", "" })
                await MustFail<InvalidDataException>(() => { MacroTrash.DeletePermanently(directory.FullName, id); return Task.CompletedTask; });
            MacroTrash.DeletePermanently(directory.FullName, "legacy.json");
            Assert(!File.Exists(unknown) && File.Exists(note) && File.Exists(Path.Combine(directory.FullName, "keep.json")), "Clear escaped trash boundary");
        }
        finally { Directory.Delete(directory.FullName, true); }
    }),
    ("Библиотека: удаление сохраняет JSON и не затрагивает соседние макросы", () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-delete-check-");
        try
        {
            var first = Path.Combine(directory.FullName, "first.json");
            var other = Path.Combine(directory.FullName, "other.json");
            MacroStorage.Write(first, new() { Name = "Одинаковое имя" });
            MacroStorage.Write(other, new() { Name = "Одинаковое имя" });
            var content = File.ReadAllText(first);
            var archived = MacroStorage.Archive(directory.FullName, "first.json");
            Assert(!File.Exists(first) && File.Exists(other), "Wrong macro removed");
            Assert(File.ReadAllText(archived) == content, "Recovery copy changed");
            Assert(Directory.GetFiles(directory.FullName, "*.json").Length == 1, "Archive appears in library");
            MacroStorage.Write(first, new() { Name = "Новый" });
            var second = MacroStorage.Archive(directory.FullName, "first.json");
            Assert(second != archived && File.ReadAllText(archived) == content, "Previous recovery copy overwritten");
        }
        finally { Directory.Delete(directory.FullName, true); }
        return Task.CompletedTask;
    }),
    ("Библиотека: ошибочные пути и отсутствующий файл не удаляют данные", async () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-delete-check-");
        try
        {
            var source = Path.Combine(directory.FullName, "keep.json");
            MacroStorage.Write(source, new());
            foreach (var id in new[] { "../keep.json", "..\\keep.json", source, "C:keep.json", "keep.txt", "" })
                await MustFail<InvalidDataException>(() => { MacroStorage.Archive(directory.FullName, id); return Task.CompletedTask; });
            await MustFail<FileNotFoundException>(() => { MacroStorage.Archive(directory.FullName, "missing.json"); return Task.CompletedTask; });
            File.WriteAllText(Path.Combine(directory.FullName, "Deleted"), "blocked directory");
            await MustFail<IOException>(() => { MacroStorage.Archive(directory.FullName, "keep.json"); return Task.CompletedTask; });
            Assert(File.Exists(source), "Failure removed source");
        }
        finally { Directory.Delete(directory.FullName, true); }
    }),
    ("Журнал: сохраняет обычные события и ошибки с деталями", () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-log-check-");
        try
        {
            var path = Path.Combine(directory.FullName, "session.log"); var log = new DiagnosticLog(path);
            log.Write("INFO", "Started"); log.Write("ERROR", "Failed", new InvalidOperationException("test failure"));
            var text = File.ReadAllText(path);
            Assert(text.Contains("[INFO] Started") && text.Contains("[ERROR] Failed") && text.Contains("InvalidOperationException: test failure"), "Log lost details");
            Assert(log.Snapshot().Contains(text), "Viewer snapshot differs from log");
        }
        finally { Directory.Delete(directory.FullName, true); }
        return Task.CompletedTask;
    }),
    ("Журнал: ошибка записи не роняет приложение и видна в снимке", () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-log-check-");
        try
        {
            // Opening a directory as a file fails on every supported platform.
            var log = new DiagnosticLog(directory.FullName); log.Write("ERROR", "Important failure");
            Assert(log.Snapshot().Contains("Не удалось сохранить журнал") && log.Snapshot().Contains("Important failure"), "Diagnostic fallback lost");
        }
        finally { Directory.Delete(directory.FullName); }
        return Task.CompletedTask;
    }),
    ("Журнал: параллельные ошибки не теряют записи", async () =>
    {
        var directory = Directory.CreateTempSubdirectory("macro-log-check-");
        try
        {
            var path = Path.Combine(directory.FullName, "session.log"); var log = new DiagnosticLog(path);
            await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() => log.Write("ERROR", "event-" + i))));
            Assert(File.ReadAllLines(path).Length == 30, "Concurrent log entries lost");
        }
        finally { Directory.Delete(directory.FullName, true); }
    }),
    ("Web: встроенная страница разрешена при первом запуске", () =>
    {
        var policy = new WebContentPolicy();
        Assert(policy.TryStart(WebContentPolicy.PageUrl, 1, false), "Own page blocked");
        Assert(policy.IsPageNavigation(1), "Navigation identity lost");
        Assert(WebContentPolicy.IsPage(WebContentPolicy.PageUrl), "Native bridge rejects own page");
        return Task.CompletedTask;
    }),
    ("Web: внешние, файловые и похожие адреса блокируются", () =>
    {
        foreach (var uri in new string?[] { null, "", "about:blank", "data:text/html,test", "file:///C:/test.html", "https://example.com/",
            "https://macroclicker.invalid.evil/index.html", "https://macroclicker.invalid@evil/index.html",
            "http://macroclicker.invalid/index.html", WebContentPolicy.PageUrl + "?x=1", WebContentPolicy.PageUrl + "#x" })
        {
            var policy = new WebContentPolicy();
            Assert(!policy.TryStart(uri, 1, false), "Foreign navigation allowed");
            Assert(!WebContentPolicy.IsPage(uri) && !WebContentPolicy.CanServe(uri, "GET"), "Foreign bridge/request allowed");
            Assert(policy.TryStart(WebContentPolicy.PageUrl, 2, false), "Rejected request poisoned startup");
        }
        return Task.CompletedTask;
    }),
    ("Web: редирект не допускается даже на адрес приложения", () =>
    {
        var policy = new WebContentPolicy();
        Assert(!policy.TryStart(WebContentPolicy.PageUrl, 1, true), "Redirect allowed");
        Assert(!policy.IsPageNavigation(1), "Blocked navigation tracked");
        Assert(policy.TryStart(WebContentPolicy.PageUrl, 2, false), "Own startup blocked");
        return Task.CompletedTask;
    }),
    ("Web: событие текущей загрузки разрешено, другая загрузка запрещена", () =>
    {
        var policy = new WebContentPolicy();
        Assert(policy.TryStart(WebContentPolicy.PageUrl, 1, false), "Initial navigation blocked");
        Assert(policy.TryStart(WebContentPolicy.PageUrl, 1, false), "Same navigation blocked");
        Assert(!policy.TryStart(WebContentPolicy.PageUrl, 2, false), "Second navigation allowed");
        return Task.CompletedTask;
    }),
    ("Web: завершение заблокированной загрузки не относится к странице", () =>
    {
        var policy = new WebContentPolicy();
        Assert(!policy.IsPageNavigation(0), "Unstarted page considered complete");
        Assert(policy.TryStart(WebContentPolicy.PageUrl, 10, false), "Initial navigation blocked");
        Assert(!policy.TryStart("https://example.com/", 11, false), "External navigation allowed");
        Assert(!policy.IsPageNavigation(11) && policy.IsPageNavigation(10), "Completion attributed to wrong page");
        return Task.CompletedTask;
    }),
    ("Web: ответ из ресурса разрешён только для GET страницы", () =>
    {
        Assert(WebContentPolicy.CanServe(WebContentPolicy.PageUrl, "GET"), "Embedded GET blocked");
        foreach (var method in new string?[] { null, "POST", "PUT", "DELETE", "HEAD" })
            Assert(!WebContentPolicy.CanServe(WebContentPolicy.PageUrl, method), "Unexpected method served");
        return Task.CompletedTask;
    }),
    ("Активное окно: ввод без поиска назначения и переключения", async () =>
    {
        var doc = Doc(Key()); doc.Nodes[0].UseActiveWindow = true; doc.Nodes[0].ProcessName = "missing";
        await Run(doc);
        Assert(Native.Events.SequenceEqual(new[] { "key:65:down", "key:65:up" }), "Active window unexpectedly activated or binding required");
    }),
    ("Активное окно: недоступный foreground блокирует ввод", async () =>
    {
        var doc = Doc(Key()); doc.Nodes[0].UseActiveWindow = true; Native.Focus = false;
        await MustFail<TargetWindowException>(() => Run(doc));
        Assert(Native.Events.Count == 0, "Input escaped");
    }),
    ("Активное окно: потеря фокуса отпускает кнопку и останавливает", async () =>
    {
        var doc = Doc(Mouse()); doc.Nodes[0].UseActiveWindow = true;
        Native.OnEvent = s => { if (s == "mouse:down") Native.Focus = false; };
        await MustFail<TargetWindowException>(() => Run(doc));
        Assert(Native.Events.SequenceEqual(new[] { "mouse:down", "mouse:up" }), "Focus guard lost");
    }),
    ("Опциональный режим не ослабляет проверку назначенного окна", async () =>
    {
        var doc = Doc(Key()); doc.Nodes[0].UseActiveWindow = true;
        doc.Nodes.Add(new MacroNode { ProcessName = "missing" });
        await MustFail<TargetWindowException>(() => Run(doc));
        Assert(Native.Events.Count == 0, "Configured missing target executed partially");
    }),
    ("Режим активного окна сохраняется в JSON и копии", () =>
    {
        var doc = Doc(Key()); doc.Nodes[0].UseActiveWindow = true;
        var copy = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(doc, MacroStorage.Options), MacroStorage.Options)!;
        Assert(copy.Nodes[0].UseActiveWindow && doc.Copy().Nodes[0].UseActiveWindow, "Active mode lost"); return Task.CompletedTask;
    }),
    ("Двойной клик: движение один раз до двух пар Down/Up", async () =>
    {
        var step = DoubleClick(); step.UseCoordinates = true; step.MoveDelayMs = 0;
        await Run(Doc(step));
        Assert(Native.Events.SequenceEqual(new[] { "window:A", "move", "mouse:down", "mouse:up", "mouse:down", "mouse:up" }), "Double click order");
    }),
    ("Системные паузы разделяют действия, но не разрывают двойной клик", async () =>
    {
        InputSender.SystemDoubleClickMs = 200;
        var doc = Doc(Mouse(), DoubleClick(), Mouse()); doc.SafeMousePauses = true;
        await Run(doc);
        var times = Native.MouseTimes;
        Assert(times.Count == 8, "Wrong count");
        Assert(Stopwatch.GetElapsedTime(times[1], times[2]).TotalMilliseconds >= 200, "No guard before pair");
        Assert(Stopwatch.GetElapsedTime(times[5], times[6]).TotalMilliseconds >= 200, "No guard after pair");
        Assert(Stopwatch.GetElapsedTime(times[2], times[4]).TotalMilliseconds < 200, "Pair split by system pause");
    }),
    ("Отмена между кликами не отправляет второй", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        Native.OnEvent = s => { if (s == "mouse:up") cancellation.Cancel(); };
        await MustFail<OperationCanceledException>(() => Run(Doc(DoubleClick()), cancellation.Token));
        Assert(Native.Events.Count(e => e == "mouse:down") == 1 && Native.Events.Last() == "mouse:up", "Unexpected second click");
    }),
    ("Отмена во время второго клика отпускает кнопку", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        Native.OnEvent = s => { if (s == "mouse:down" && Native.MouseTimes.Count == 3) cancellation.Cancel(); };
        await MustFail<OperationCanceledException>(() => Run(Doc(DoubleClick()), cancellation.Token));
        Assert(Native.Events.TakeLast(4).SequenceEqual(new[] { "mouse:down", "mouse:up", "mouse:down", "mouse:up" }), "Stuck second click");
    }),
    ("Потеря фокуса между кликами останавливает пару", async () =>
    {
        Native.OnEvent = s => { if (s == "mouse:up") Native.Focus = false; };
        await MustFail<TargetWindowException>(() => Run(Doc(DoubleClick())));
        Assert(Native.MouseTimes.Count == 2, "Second click after focus loss");
    }),
    ("Курсор вне приложения между кликами блокирует второй", async () =>
    {
        Native.OnEvent = s => { if (s == "mouse:up") Native.CursorInside = false; };
        await MustFail<TargetWindowException>(() => Run(Doc(DoubleClick())));
        Assert(Native.MouseTimes.Count == 2, "Second click outside application");
    }),
    ("Двойной клик не продолжает ранее зажатую кнопку", async () =>
    {
        var held = Mouse(); held.Action = ButtonAction.Down;
        var pair = DoubleClick(); pair.UseCoordinates = true;
        await MustFail<InvalidDataException>(() => Run(Doc(held, pair)));
        Assert(Native.MouseTimes.Count == 2 && Native.Events.Last() == "mouse:up", "Held button leaked");
        Assert(!Native.Events.Contains("move"), "Dragged held button before rejection");
    }),
    ("Двойной клик сохраняется в JSON; неверная кнопка и режим отклоняются", () =>
    {
        var doc = Doc(DoubleClick());
        var copy = JsonSerializer.Deserialize<MacroDocument>(JsonSerializer.Serialize(doc, MacroStorage.Options), MacroStorage.Options)!;
        MacroStorage.Validate(copy);
        Assert(copy.Nodes[0].Steps[0].Kind == InputKind.DoubleClick, "Kind lost");
        copy.Nodes[0].Steps[0].Action = ButtonAction.Down;
        try { MacroStorage.Validate(copy); throw new Exception("Down accepted"); } catch (InvalidDataException) { }
        copy.Nodes[0].Steps[0].Action = ButtonAction.Tap; copy.Nodes[0].Steps[0].Input = "bad";
        try { MacroStorage.Validate(copy); throw new Exception("Button accepted"); } catch (InvalidDataException) { }
        return Task.CompletedTask;
    }),
    ("Комбинация: модификатор до клавиши, отпускание в обратном порядке", async () =>
    {
        await Run(Doc(Key("Ctrl+C")));
        Assert(Native.Events.SequenceEqual(new[] { "window:A", "key:17:down", "key:67:down", "key:67:up", "key:17:up" }), string.Join(", ", Native.Events));
    }),
    ("Остановка во время удержания отпускает обе клавиши", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        Native.OnEvent = s => { if (s == "key:67:down") cancellation.Cancel(); };
        await MustFail<OperationCanceledException>(() => Run(Doc(Key("Ctrl+C", hold: 2000)), cancellation.Token));
        Assert(Native.Events.TakeLast(2).SequenceEqual(new[] { "key:67:up", "key:17:up" }), "Stuck chord");
    }),
    ("Потеря фокуса после перемещения не отправляет клик", async () =>
    {
        var mouse = Mouse(); mouse.UseCoordinates = true; mouse.MoveDelayMs = 100;
        Native.OnEvent = s => { if (s == "move") Native.Focus = false; };
        await MustFail<TargetWindowException>(() => Run(Doc(mouse)));
        Assert(!Native.Events.Any(e => e.StartsWith("mouse:")), "Click escaped");
    }),
    ("Потеря фокуса при удержании отпускает кнопку", async () =>
    {
        Native.OnEvent = s => { if (s == "mouse:down") Native.Focus = false; };
        await MustFail<TargetWindowException>(() => Run(Doc(Mouse())));
        Assert(Native.Events.Last() == "mouse:up", "Stuck mouse");
    }),
    ("Узлы активируются по порядку при каждом повторе", async () =>
    {
        var doc = Doc(Key()); doc.Nodes.Add(new MacroNode { Name = "B", Steps = [Key()] }); doc.RepeatCount = 2;
        await Run(doc);
        Assert(Native.Events.Where(e => e.StartsWith("window:")).SequenceEqual(new[] { "window:A", "window:B", "window:A", "window:B" }), "Order");
    }),
    ("Недоступный второй узел блокирует ввод даже в первый", async () =>
    {
        var doc = Doc(Key()); doc.Nodes.Add(new MacroNode { ProcessName = "missing" });
        await MustFail<TargetWindowException>(() => Run(doc));
        Assert(Native.Events.Count == 0, "Partial execution");
    }),
    ("Unicode сохраняет кириллицу, emoji и перевод строки", async () =>
    {
        await Run(Doc(new MacroStep { Kind = InputKind.Text, Text = "Я😀\r\n", CharacterDelayMs = 0, DelayAfterMs = 0 }));
        Assert(Native.Events.Contains("unicode:1071:down") && Native.Events.Contains("unicode:55357:down") &&
            Native.Events.Contains("unicode:56832:down") && Native.Events.Contains("key:13:down"), "Text corruption");
        Assert(Native.Events.Count(e => e == "key:13:down") == 1, "CRLF doubled");
    }),
    ("Отмена длинного текста останавливает дальнейший ввод", async () =>
    {
        using var cancellation = new CancellationTokenSource();
        Native.OnEvent = s => { if (s == "unicode:65:up") cancellation.Cancel(); };
        await MustFail<OperationCanceledException>(() => Run(Doc(new MacroStep { Kind = InputKind.Text, Text = "ABC", CharacterDelayMs = 10 }), cancellation.Token));
        Assert(!Native.Events.Contains("unicode:66:down"), "Text continued");
    }),
    ("Системная пауза использует заданный порог, без 600 мс", async () =>
    {
        var doc = Doc(Mouse(), Mouse()); doc.SafeMousePauses = true;
        await Run(doc);
        var gap = Stopwatch.GetElapsedTime(Native.MouseTimes[1], Native.MouseTimes[2]).TotalMilliseconds;
        Assert(gap >= 30 && gap < 500, $"Unexpected interval {gap}");
    }),
    ("Ручная пауза засчитывается в системный интервал", async () =>
    {
        var doc = Doc(Mouse(50), Mouse()); doc.SafeMousePauses = true;
        await Run(doc);
        var gap = Stopwatch.GetElapsedTime(Native.MouseTimes[1], Native.MouseTimes[2]).TotalMilliseconds;
        Assert(gap >= 49 && gap < 80, $"Double delay {gap}");
    }),
    ("Записанные Down и Up удерживают кнопку через перемещение", async () =>
    {
        var down = Mouse(); down.Action = ButtonAction.Down;
        var up = Mouse(); up.Action = ButtonAction.Up;
        await Run(Doc(down, new MacroStep { Kind = InputKind.Move, DelayAfterMs = 0, MoveDelayMs = 0 }, up));
        Assert(Native.Events.SequenceEqual(new[] { "window:A", "mouse:down", "move", "mouse:up" }), "Drag order");
    }),
    ("Импорт старого JSON не теряет шаги и добавляет один узел", () =>
    {
        var folder = Path.Combine(Path.GetTempPath(), "MacroClicker-check-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "legacy.json");
        File.WriteAllText(file, """{"RepeatCount":3,"SafeMousePauses":false,"Steps":[{"Kind":"Key","Input":"1","HoldMs":50,"X":-120,"CoordinateSpace":"Screen"}]}""");
        var doc = MacroStorage.Read(file);
        Assert(doc.Nodes.Count == 1 && doc.Nodes[0].Steps[0].X == -120 && doc.RepeatCount == 3 && !doc.SafeMousePauses, "Migration");
        MacroStorage.Write(file, doc);
        Assert(MacroStorage.Read(file).Nodes[0].Steps.Count == 1, "Roundtrip");
        File.Delete(file); Directory.Delete(folder);
        return Task.CompletedTask;
    }),
    ("JSON отклоняет отрицательное время и неизвестный тип", () =>
    {
        var doc = Doc(Key()); doc.Nodes[0].Steps[0].HoldMs = -1;
        try { MacroStorage.Validate(doc); throw new Exception("Invalid timing accepted"); } catch (InvalidDataException) { }
        doc.Nodes[0].Steps[0].HoldMs = 1; doc.Nodes[0].Steps[0].Kind = (InputKind)100;
        try { MacroStorage.Validate(doc); throw new Exception("Invalid kind accepted"); } catch (InvalidDataException) { }
        return Task.CompletedTask;
    }),
    ("Копия сценария отделяет узлы, текст и координаты", () =>
    {
        var doc = Doc(Key()); var copy = doc.Copy(); copy.Nodes[0].Steps[0].X = 10;
        Assert(doc.Nodes[0].Steps[0].X == 0, "Snapshot mutated"); return Task.CompletedTask;
    })
};
foreach (var test in tests)
{
    Native.Reset();
    await test.Run();
    Console.WriteLine("PASS " + test.Name);
}
Console.WriteLine($"PASS {tests.Length}/{tests.Length}. Windows APIs replaced with test doubles; no OS/UI claims.");
RecordingChecks.Run();
