using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroClicker;

internal sealed class AppSettings
{
    public bool SeparateStopHotkey { get; set; }
    public bool DarkTheme { get; set; } = true;
    public HotkeyDefinition StartHotkey { get; set; } = new() { Control = true, Alt = true, Key = Keys.F8 };
    public HotkeyDefinition StopHotkey { get; set; } = new() { Control = true, Alt = true, Key = Keys.F9 };

    public IEnumerable<ushort[]> RecordingControlChords()
    {
        yield return InputSender.ParseChord(StartHotkey.DisplayText);
        if (SeparateStopHotkey) yield return InputSender.ParseChord(StopHotkey.DisplayText);
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MacroClicker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameMacroClicker", "settings.json");
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions()) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

internal sealed class HotkeyDefinition
{
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Windows { get; set; }
    public Keys Key { get; set; } = Keys.F8;

    [JsonIgnore]
    public uint Modifiers => (Control ? 0x0002u : 0) | (Alt ? 0x0001u : 0) |
        (Shift ? 0x0004u : 0) | (Windows ? 0x0008u : 0) | 0x4000u;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");
            parts.Add((int)Key >= (int)Keys.D0 && (int)Key <= (int)Keys.D9
                ? ((int)Key - (int)Keys.D0).ToString()
                : Key.ToString());
            return string.Join("+", parts);
        }
    }

    public HotkeyDefinition Copy() => new() { Control = Control, Alt = Alt, Shift = Shift, Windows = Windows, Key = Key };
    public bool SameAs(HotkeyDefinition other) => Modifiers == other.Modifiers && Key == other.Key;
}
