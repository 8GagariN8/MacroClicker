using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroClicker;

internal sealed class InputRecorder : IDisposable
{
    private readonly RecordingSession _session;
    private readonly HookProc _keyboardCallback, _mouseCallback;
    private IntPtr _keyboard, _mouse;
    private long _lastMove;
    public int Count => _session.Count;
    public bool Full => _session.Full;

    public InputRecorder(IEnumerable<WindowTarget> targets, IEnumerable<ushort[]>? controlChords = null)
    {
        _session = new RecordingSession(targets, controlChords);
        _keyboardCallback = KeyboardHook; _mouseCallback = MouseHook;
    }
    public void Start()
    {
        // Ignore buttons already held while the user started recording. Only
        // fresh presses after their releases can become recorded actions.
        var heldKeys = Enumerable.Range(8, 247).Where(k => (GetAsyncKeyState(k) & 0x8000) != 0).ToArray();
        var heldButtons = new (int Key, string Name)[] { (1, "Левая"), (2, "Правая"), (4, "Средняя"), (5, "Боковая 1"), (6, "Боковая 2") }
            .Where(b => (GetAsyncKeyState(b.Key) & 0x8000) != 0).Select(b => b.Name).ToArray();
        // Modifier aliases are reported together by GetAsyncKeyState. Keep only
        // left/right variants, matching the keys delivered by the low-level hook.
        _session.Initialize(GetForegroundWindow(), heldKeys.Where(k => k is not (0x10 or 0x11 or 0x12)), heldButtons);
        var module = GetModuleHandle(null);
        _keyboard = SetWindowsHookEx(13, _keyboardCallback, module, 0);
        _mouse = SetWindowsHookEx(14, _mouseCallback, module, 0);
        if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error(); Dispose();
            throw new Win32Exception(error, "Не удалось включить запись ввода.");
        }
    }
    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardData>(pointer);
            if ((data.flags & 0x10) == 0 && data.key is >= 8 and <= 254)
            {
                var up = message.ToInt32() is 0x101 or 0x105;
                _session.Keyboard(GetForegroundWindow(), (int)data.key, up, Stopwatch.GetTimestamp());
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, pointer);
    }
    private IntPtr MouseHook(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<MouseData>(pointer);
            if ((data.flags & 1) == 0)
            {
                var msg = message.ToInt32();
                var now = Stopwatch.GetTimestamp();
                if (msg != 0x200 || Stopwatch.GetElapsedTime(_lastMove, now).TotalMilliseconds >= 50)
                {
                    if (msg == 0x200) _lastMove = now;
                    var step = new MacroStep { CoordinateSpace = CoordinateSpace.Screen, UseCoordinates = true, X = data.point.X, Y = data.point.Y };
                    var valid = true;
                    switch (msg)
                    {
                        case 0x200: step.Kind = InputKind.Move; break;
                        case 0x20A: case 0x20E:
                            step.Kind = InputKind.Wheel; step.WheelDelta = unchecked((short)(data.mouseData >> 16)); step.HorizontalWheel = msg == 0x20E; break;
                        case 0x201: case 0x202: case 0x204: case 0x205: case 0x207: case 0x208: case 0x20B: case 0x20C:
                            step.Kind = InputKind.Mouse;
                            step.Input = msg switch { 0x201 or 0x202 => "Левая", 0x204 or 0x205 => "Правая", 0x207 or 0x208 => "Средняя", _ => (data.mouseData >> 16) == 1 ? "Боковая 1" : "Боковая 2" };
                            step.Action = msg is 0x202 or 0x205 or 0x208 or 0x20C ? ButtonAction.Up : ButtonAction.Down;
                            break;
                        default: valid = false; break;
                    }
                    if (valid)
                    {
                        var root = GetAncestor(WindowFromPoint(data.point), 2);
                        _session.Mouse(GetForegroundWindow(), root, step, now);
                    }
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, pointer);
    }
    public List<MacroNode> Finish()
    {
        Dispose();
        return _session.Finish(Stopwatch.GetTimestamp());
    }
    public void Dispose()
    {
        if (_keyboard != IntPtr.Zero) { UnhookWindowsHookEx(_keyboard); _keyboard = IntPtr.Zero; }
        if (_mouse != IntPtr.Zero) { UnhookWindowsHookEx(_mouse); _mouse = IntPtr.Zero; }
    }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint key, scan, flags, time; public nuint extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public NativePoint point; public uint mouseData, flags, time; public nuint extra; }
}
