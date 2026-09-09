using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MacroClicker;

internal static class InputSender
{
    public static IReadOnlyList<string> SupportedMouseButtons { get; } = ["Левая", "Правая", "Средняя", "Боковая 1", "Боковая 2"];
    public static IReadOnlyList<string> SupportedKeys { get; } = Enum.GetValues<Keys>()
        .Where(k => (int)k >= 8 && (int)k <= 254).Distinct().Select(k => KeyName((int)k)).ToArray();
    public static int SystemDoubleClickMs => (int)GetDoubleClickTime();
    public static int SafeMousePauseMs => SystemDoubleClickMs + 1;
    public static string KeyName(int key) => key is >= 0x30 and <= 0x39 ? ((char)key).ToString() : ((Keys)key).ToString();
    public static ushort[] ParseChord(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidDataException("Укажите клавишу или комбинацию, например Ctrl+Shift+S.");
        var keys = input.Split('+', StringSplitOptions.TrimEntries).Select(ParseKey).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new InvalidDataException("В комбинации есть повторяющиеся клавиши.");
        return keys;
    }
    private static ushort ParseKey(string value)
    {
        var name = value.ToUpperInvariant() switch
        {
            "CTRL" => "ControlKey", "ALT" => "Menu", "SHIFT" => "ShiftKey", "WIN" => "LWin",
            "ПРОБЕЛ" => "Space", "ESC" => "Escape", "DEL" => "Delete", _ => value
        };
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) return name[0];
        if (Enum.TryParse<Keys>(name, true, out var key) && (int)key is >= 8 and <= 254) return (ushort)key;
        if (name.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(name[3..], System.Globalization.NumberStyles.HexNumber, null, out var vk) && vk is >= 8 and <= 254) return vk;
        throw new InvalidDataException($"Неизвестная клавиша: {value}. Используйте список или код VK_XX.");
    }
    public static void KeyEvent(ushort key, bool up)
    {
        var extended = key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E
            or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;
        Send(new INPUT { type = 1, union = new InputUnion { keyboard = new KEYBDINPUT { virtualKey = key, flags = (up ? 2u : 0) | (extended ? 1u : 0) } } });
    }
    public static void UnicodeEvent(char value, bool up) => Send(new INPUT
    {
        type = 1, union = new InputUnion { keyboard = new KEYBDINPUT { scanCode = value, flags = 4u | (up ? 2u : 0) } }
    });
    public static void MouseEvent(string button, bool up)
    {
        var (down, release, data) = button switch
        {
            "Левая" => (2u, 4u, 0u), "Правая" => (8u, 16u, 0u), "Средняя" => (32u, 64u, 0u),
            "Боковая 1" => (128u, 256u, 1u), "Боковая 2" => (128u, 256u, 2u),
            _ => throw new InvalidDataException("Неизвестная кнопка мыши")
        };
        SendMouse(up ? release : down, data);
    }
    public static void Wheel(int delta, bool horizontal) => SendMouse(horizontal ? 0x1000u : 0x800u, unchecked((uint)delta));
    private static void SendMouse(uint flags, uint data) => Send(new INPUT
    {
        type = 0, union = new InputUnion { mouse = new MOUSEINPUT { flags = flags, mouseData = data } }
    });
    private static void Send(INPUT input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows не приняла событие ввода. Проверьте права приложения.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int dx, dy; public uint mouseData, flags, time; public nuint extraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    {
        public ushort virtualKey, scanCode; public uint flags, time; public nuint extraInfo;
    }
}
