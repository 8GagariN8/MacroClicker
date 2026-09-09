using System.Diagnostics;

namespace MacroClicker;

internal sealed record WindowTarget(string Name, IntPtr Handle = default, int ProcessId = 1)
{
    public string ProcessName => Name;
    public string Title => Name;
}
internal sealed class TargetWindowException(string message) : Exception(message);
internal static class Native
{
    public static readonly List<string> Events = [];
    public static readonly List<long> MouseTimes = [];
    public static bool Focus = true;
    public static bool CursorInside = true;
    public static Action<string>? OnEvent;
    public static void Emit(string text) { Events.Add(text); OnEvent?.Invoke(text); }
    public static void Reset() { Events.Clear(); MouseTimes.Clear(); Focus = true; CursorInside = true; OnEvent = null; InputSender.SystemDoubleClickMs = 30; }
}
internal static class WindowTargeting
{
    public static WindowTarget GetActiveApplication()
    { if (!Native.Focus) throw new TargetWindowException("no active application"); return new("active"); }
    public static WindowTarget Resolve(MacroNode node) => node.ProcessName == "missing"
        ? throw new TargetWindowException("missing") : new(node.Name);
    public static Task ActivateAsync(WindowTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Native.Emit("window:" + target.Name); return Task.CompletedTask;
    }
    public static void EnsureForeground(WindowTarget target)
    { if (!Native.Focus) throw new TargetWindowException("focus lost"); }
    public static bool IsCursorOverApplication(WindowTarget target) => Native.Focus && Native.CursorInside;
    public static bool TryMoveCursorToClientPosition(WindowTarget target, int x, int y, out string error)
    { error = ""; Native.Emit("move"); return true; }
    public static bool TryMoveCursorToScreenPosition(int x, int y, out string error)
    { error = ""; Native.Emit("move"); return true; }
}
internal static class InputSender
{
    public static string KeyName(int key) => key.ToString();
    public static IReadOnlyList<string> SupportedMouseButtons => ["Левая", "Правая"];
    public static int SystemDoubleClickMs { get; set; } = 30;
    public static int SafeMousePauseMs => SystemDoubleClickMs + 1;
    public static ushort[] ParseChord(string input) => input.Split('+').Select(s => s switch
    {
        "Ctrl" => (ushort)17, "C" => (ushort)67, "A" => (ushort)65, "1" => (ushort)49,
        _ => throw new InvalidDataException("Invalid fake key")
    }).ToArray();
    public static void KeyEvent(ushort key, bool up) => Native.Emit($"key:{key}:{(up ? "up" : "down")}");
    public static void MouseEvent(string button, bool up)
    {
        Native.MouseTimes.Add(Stopwatch.GetTimestamp()); Native.Emit("mouse:" + (up ? "up" : "down"));
    }
    public static void UnicodeEvent(char value, bool up) => Native.Emit($"unicode:{(int)value}:{(up ? "up" : "down")}");
    public static void Wheel(int delta, bool horizontal) => Native.Emit($"wheel:{delta}:{horizontal}");
}
