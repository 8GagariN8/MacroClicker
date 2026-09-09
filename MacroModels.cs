using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroClicker;

public enum InputKind { Key, Mouse, Pause, Text, Move, Wheel, DoubleClick }
public enum CoordinateSpace { Window, Screen }
public enum ButtonAction { Tap, Down, Up }

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
    [JsonIgnore] internal WindowTarget? SessionTarget { get; set; }
    public MacroNode Copy() => new()
    {
        Name = Name, ProcessName = ProcessName, WindowTitle = WindowTitle, UseActiveWindow = UseActiveWindow,
        SessionTarget = SessionTarget, Steps = Steps.Select(s => s.Copy()).ToList()
    };
}

public sealed class MacroDocument
{
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Новый макрос";
    public int RepeatCount { get; set; } = 1;
    public bool RepeatForever { get; set; }
    public bool SafeMousePauses { get; set; } = true;
    public List<MacroNode> Nodes { get; set; } = [];
    public List<MacroStep> Steps { get; set; } = []; // Импорт JSON первой версии.
    public MacroDocument Copy() => new()
    {
        Name = Name, RepeatCount = RepeatCount, RepeatForever = RepeatForever,
        SafeMousePauses = SafeMousePauses, Nodes = Nodes.Select(n => n.Copy()).ToList()
    };
}

internal static class MacroStorage
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
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
        if (doc.Version > 2) throw new InvalidDataException("Этот макрос создан более новой версией MacroClicker.");
        if (doc.Nodes is null || doc.Steps is null) throw new InvalidDataException("Некорректный список узлов или действий.");
        if (doc.Nodes.Count == 0 && doc.Steps.Count > 0)
        {
            doc.Nodes.Add(new MacroNode { Name = "Импортированный макрос — выберите окно", Steps = doc.Steps });
            doc.Steps = [];
        }
        Validate(doc);
        return doc;
    }
    public static void Write(string path, MacroDocument doc)
    {
        Validate(doc);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(doc, Options));
        File.Move(temporary, path, true);
    }
    public static void Validate(MacroDocument doc)
    {
        if (doc.Name is null || doc.Nodes is null || doc.Steps is null) throw new InvalidDataException("Название и списки не могут быть null.");
        if (doc.RepeatCount is < 1 or > 100000 || doc.Nodes.Count > 1000)
            throw new InvalidDataException("Недопустимое число повторов или узлов.");
        foreach (var node in doc.Nodes)
        {
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
