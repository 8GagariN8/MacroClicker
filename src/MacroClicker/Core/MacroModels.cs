using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroClicker;

public enum InputKind { Key, Mouse, Pause, Text, Move, Wheel, DoubleClick }
public enum CoordinateSpace { Window, Screen }
public enum ButtonAction { Tap, Down, Up }
public enum PeriodicTrigger { Cycles, Time }

public sealed class PeriodicAction
{
    [JsonRequired] public bool Enabled { get; set; } = true;
    [JsonRequired] public PeriodicTrigger Trigger { get; set; }
    [JsonRequired] public int EveryCycles { get; set; } = 6;
    [JsonRequired] public int EveryMs { get; set; } = 30000;
    [JsonRequired] public MacroNode Node { get; set; } = new() { Name = "Дополнительные действия", UseActiveWindow = true };
    public PeriodicAction Copy() => new()
    {
        Enabled = Enabled, Trigger = Trigger, EveryCycles = EveryCycles, EveryMs = EveryMs, Node = Node.Copy()
    };
}

public sealed class MacroStep
{
    public InputKind Kind { get; set; }
    public string Input { get; set; } = "1";
    public int DelayBeforeMs { get; set; }
    public int HoldMs { get; set; } = 50;
    public int DelayAfterMs { get; set; } = 100;
    public bool UseCoordinates { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public CoordinateSpace CoordinateSpace { get; set; }
    public int MoveDelayMs { get; set; } = 100;
    public ButtonAction Action { get; set; }
    public string Text { get; set; } = "";
    public int CharacterDelayMs { get; set; } = 10;
    public int WheelDelta { get; set; } = 120;
    public bool HorizontalWheel { get; set; }
    [JsonIgnore] public string DisplayKind => Kind switch
    {
        InputKind.Key => "Клавиша / комбинация", InputKind.Mouse => "Мышь", InputKind.Pause => "Пауза",
        InputKind.Text => "Текст", InputKind.Move => "Перемещение", InputKind.Wheel => "Колесо",
        InputKind.DoubleClick => "Двойной клик мышью", _ => "?"
    };
    [JsonIgnore] public string DisplayInput => Kind switch
    {
        InputKind.Text => Text.Replace("\r", "").Replace("\n", " ↵ ") is var s && s.Length > 70 ? s[..70] + "…" : s,
        InputKind.Pause or InputKind.Move => "—",
        InputKind.Wheel => $"{(HorizontalWheel ? "Горизонтально" : "Вертикально")}: {WheelDelta}",
        _ => Input + (Action == ButtonAction.Tap ? "" : Action == ButtonAction.Down ? " ↓" : " ↑")
    };
    [JsonIgnore] public string DisplayCoordinates => UseCoordinates || Kind == InputKind.Move
        ? $"{(CoordinateSpace == CoordinateSpace.Window ? "Окно" : "Экран")}: {X}, {Y}" : "—";
    [JsonIgnore] public string DisplayHold => Kind == InputKind.DoubleClick ? "Авто · двойной клик" : HoldMs.ToString();
    public MacroStep Copy() => (MacroStep)MemberwiseClone();
}

public sealed class MacroNode
{
    public bool UseActiveWindow { get; set; }
    public string Name { get; set; } = "Окно приложения";
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = [];
    public int TransitionDelayMs { get; set; }
    public List<PeriodicAction> PeriodicActions { get; set; } = [];
    [JsonIgnore] internal WindowTarget? SessionTarget { get; set; }
    public MacroNode Copy() => new()
    {
        Name = Name, ProcessName = ProcessName, WindowTitle = WindowTitle, UseActiveWindow = UseActiveWindow,
        SessionTarget = SessionTarget, Steps = Steps.Select(s => s.Copy()).ToList(),
        TransitionDelayMs = TransitionDelayMs, PeriodicActions = PeriodicActions.Select(r => r.Copy()).ToList()
    };
}

public sealed class MacroDocument
{
    public int Version { get; set; } = 3;
    public string Name { get; set; } = "Новый макрос";
    public int RepeatCount { get; set; } = 1;
    public bool RepeatForever { get; set; }
    public bool SafeMousePauses { get; set; } = true;
    public List<MacroNode> Nodes { get; set; } = [];
    public List<MacroStep> Steps { get; set; } = []; // Импорт JSON первой версии.
    // Exactly one attachment level; Validate rejects nested periodic actions.
    public IEnumerable<MacroNode> AllNodes(bool enabledOnly = false) => Nodes.SelectMany(n =>
        new[] { n }.Concat(n.PeriodicActions.Where(r => !enabledOnly || r.Enabled).Select(r => r.Node)));
    public MacroDocument Copy() => new()
    {
        Name = Name, RepeatCount = RepeatCount, RepeatForever = RepeatForever,
        SafeMousePauses = SafeMousePauses, Nodes = Nodes.Select(n => n.Copy()).ToList(), Steps = Steps.Select(s => s.Copy()).ToList()
    };
}

internal static class MacroStorage
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter<PeriodicTrigger>(allowIntegerValues: false), new JsonStringEnumConverter() }
    };
    public static string LibraryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroClicker", "Macros");
    public static string Archive(string libraryPath, string id, DateTimeOffset? deletedAt = null)
    {
        // Accept a library filename only, never a path supplied by the web interface.
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['/', '\\', ':']) >= 0 ||
            Path.GetFileName(id) != id || !id.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Некорректный идентификатор макроса.");
        var source = Path.Combine(libraryPath, id);
        if (!File.Exists(source)) throw new FileNotFoundException("Макрос уже удалён или перемещён.");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Нельзя удалить макрос по символической ссылке.");
        var archive = Path.Combine(libraryPath, "Deleted");
        Directory.CreateDirectory(archive);
        if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Папка удалённых макросов не должна быть ссылкой.");
        var stamp = (deletedAt ?? DateTimeOffset.UtcNow).UtcDateTime.ToString(MacroTrash.TimestampFormat, System.Globalization.CultureInfo.InvariantCulture);
        var destination = Path.Combine(archive, stamp + "_" + Guid.NewGuid() + ".json");
        File.Move(source, destination); // No overwrite: an error must leave the editor unchanged.
        return destination;
    }
    public static MacroDocument Read(string path)
    {
        var doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Пустой файл макроса.");
        if (doc.Version > 3) throw new InvalidDataException("Этот макрос создан более новой версией MacroClicker.");
        if (doc.Version < 1) throw new InvalidDataException("Неизвестная версия макроса.");
        if (doc.Nodes is null || doc.Steps is null) throw new InvalidDataException("Некорректный список узлов или действий.");
        if (doc.Nodes.Count == 0 && doc.Steps.Count > 0)
        {
            doc.Nodes.Add(new MacroNode { Name = "Импортированный макрос — выберите окно", Steps = doc.Steps });
            doc.Steps = [];
        }
        Validate(doc);
        doc.Version = 3;
        return doc;
    }
    public static void Write(string path, MacroDocument doc)
    {
        Validate(doc);
        doc = doc.Copy(); // Always write v3; never mutate the caller during save.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(doc, Options));
        File.Move(temporary, path, true);
    }
    public static void Validate(MacroDocument doc)
    {
        if (doc.Name is null || doc.Nodes is null || doc.Steps is null) throw new InvalidDataException("Название и списки не могут быть null.");
        if (doc.Version is < 1 or > 3) throw new InvalidDataException("Неизвестная версия макроса.");
        if (doc.RepeatCount is < 1 or > 100000 || doc.Nodes.Count > 1000)
            throw new InvalidDataException("Недопустимое число повторов или узлов.");
        var nodes = new List<MacroNode>();
        var rules = 0;
        foreach (var parent in doc.Nodes)
        {
            if (parent is null || parent.PeriodicActions is null) throw new InvalidDataException("Некорректный узел или список условий.");
            nodes.Add(parent);
            rules += parent.PeriodicActions.Count;
            if (rules > 100) throw new InvalidDataException("Допустимо не более 100 условий на макрос.");
            if (parent.PeriodicActions.Count > 0 && (parent.Steps is null || parent.Steps.Count == 0))
                throw new InvalidDataException("Основной узел с условиями должен содержать действия или паузу.");
            foreach (var rule in parent.PeriodicActions)
            {
                if (rule is null || !Enum.IsDefined(rule.Trigger) || rule.EveryCycles is < 1 or > 100000 || rule.EveryMs is < 1000 or > 86400000)
                    throw new InvalidDataException("Условие: 1–100000 повторений или 1 секунда–24 часа; оба сохранённых интервала должны быть корректны.");
                if (rule.Node is null || rule.Node.PeriodicActions is null || rule.Node.PeriodicActions.Count != 0)
                    throw new InvalidDataException("Вложенные дополнительные узлы не поддерживаются.");
                if (rule.Enabled && (rule.Node.Steps is null || rule.Node.Steps.Count == 0))
                    throw new InvalidDataException("Включённый дополнительный узел должен содержать действия.");
                nodes.Add(rule.Node);
            }
        }
        if (nodes.Count > 1000) throw new InvalidDataException("Допустимо не более 1000 узлов, включая дополнительные.");
        foreach (var node in nodes)
        {
            if (node.TransitionDelayMs is < 0 or > 600000)
                throw new InvalidDataException("Пауза перед следующим узлом: 0–600000 мс.");
            if (node is null || node.Name is null || node.ProcessName is null || node.WindowTitle is null || node.Steps is null || node.Steps.Count > 50000)
                throw new InvalidDataException("Некорректный узел или слишком много действий.");
            foreach (var s in node.Steps)
            {
                if (s is null || !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Action) || !Enum.IsDefined(s.CoordinateSpace))
                    throw new InvalidDataException("Неизвестный тип действия.");
                if (s.DelayBeforeMs is < 0 or > 600000 || s.DelayAfterMs is < 0 or > 600000 ||
                    s.HoldMs is < 1 or > 600000 || s.MoveDelayMs is < 0 or > 600000 || s.CharacterDelayMs is < 0 or > 10000)
                    throw new InvalidDataException("Время шага должно быть в пределах 0–600000 мс, удержание от 1 мс.");
                if (s.Kind == InputKind.Key) InputSender.ParseChord(s.Input);
                if (s.Kind is InputKind.Mouse or InputKind.DoubleClick && !InputSender.SupportedMouseButtons.Contains(s.Input))
                    throw new InvalidDataException("Неизвестная кнопка мыши.");
                if (s.Kind == InputKind.DoubleClick && s.Action != ButtonAction.Tap)
                    throw new InvalidDataException("Двойной клик всегда нажимает и отпускает кнопку дважды.");
                if (s.Text is null || s.Text.Length > 1000000) throw new InvalidDataException("Текст должен быть не длиннее миллиона символов.");
                if (s.X is < -100000 or > 100000 || s.Y is < -100000 or > 100000)
                    throw new InvalidDataException("Координаты должны быть в пределах -100000…100000.");
            }
        }
    }
}
