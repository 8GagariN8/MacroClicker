using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MacroClicker;

internal sealed class TargetWindowException(string message) : Exception(message);

internal sealed record WindowTarget(IntPtr Handle, int ProcessId, string ProcessName, string Title)
{
    public override string ToString() => $"{Title}  —  {ProcessName}";
}

internal static class WindowTargeting
{
    public static IReadOnlyList<WindowTarget> GetVisibleWindows()
    {
        var result = new List<WindowTarget>();
        var ownProcessId = Environment.ProcessId;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var length = GetWindowTextLength(handle);
            if (length <= 0) return true;
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == ownProcessId) return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;
            try
            {
                var processName = Process.GetProcessById((int)processId).ProcessName;
                result.Add(new WindowTarget(handle, (int)processId, processName, title.ToString()));
            }
            catch
            {
                // Процесс мог завершиться между перечислением окон и чтением имени.
            }
            return true;
        }, IntPtr.Zero);

        return result.OrderBy(item => item.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool Exists(WindowTarget target)
    {
        if (!IsWindow(target.Handle)) return false;
        GetWindowThreadProcessId(target.Handle, out var process);
        return process == target.ProcessId;
    }

    public static WindowTarget Resolve(MacroNode node)
    {
        if (node.SessionTarget is { } selected && Exists(selected)) return selected;
        var candidates = GetVisibleWindows().Where(w =>
            w.ProcessName.Equals(node.ProcessName, StringComparison.OrdinalIgnoreCase) && w.Title == node.WindowTitle).ToArray();
        if (candidates.Length != 1)
            throw new TargetWindowException($"Узел «{node.Name}»: выберите окно приложения заново (не найдено однозначное совпадение).");
        return candidates[0];
    }

    public static WindowTarget GetActiveApplication()
    {
        var handle = GetForegroundWindow();
        var target = GetVisibleWindows().FirstOrDefault(w => w.Handle == handle);
        // The clicker itself is excluded by GetVisibleWindows.
        return target ?? throw new TargetWindowException("Перейдите в нужное приложение перед запуском. Ввод в сам MacroClicker не отправляется.");
    }

    public static bool IsCursorAt(int x, int y) => GetCursorPos(out var p) && p.X == x && p.Y == y;

    public static async Task ActivateAsync(WindowTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Exists(target)) throw new TargetWindowException("Выбранное окно закрыто.");
        if (IsIconic(target.Handle))
        {
            ShowWindowAsync(target.Handle, ShowRestore);
            for (var i = 0; i < 40 && IsIconic(target.Handle); i++) await Task.Delay(50, token);
        }
        SetForegroundWindow(target.Handle);
        for (var i = 0; i < 40; i++)
        {
            token.ThrowIfCancellationRequested();
            if (IsSelectedWindowForeground(target))
            {
                await Task.Delay(200, token);
                EnsureForeground(target);
                return;
            }
            await Task.Delay(50, token);
        }
        throw new TargetWindowException("Windows не разрешила активировать окно. Активируйте его вручную и повторите запуск.");
    }

    public static void EnsureForeground(WindowTarget target)
    {
        if (!Exists(target) || !IsForegroundApplication(target))
            throw new TargetWindowException("Окно закрыто или потеряло фокус. Выполнение остановлено.");
    }

    public static bool TryActivate(WindowTarget target, out string error)
    {
        error = "";
        if (!IsWindow(target.Handle))
        {
            error = "выбранное окно закрыто";
            return false;
        }
        if (IsSelectedWindowForeground(target)) return true;

        if (IsIconic(target.Handle))
            ShowWindowAsync(target.Handle, ShowRestore);
        SetForegroundWindow(target.Handle);
        return true;
    }

    public static bool IsSelectedWindowForeground(WindowTarget target) =>
        IsWindow(target.Handle) && GetForegroundWindow() == target.Handle;

    public static bool IsForegroundApplication(WindowTarget target)
    {
        if (!IsWindow(target.Handle)) return false;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        return foregroundProcessId == target.ProcessId;
    }

    public static bool IsCursorOverApplication(WindowTarget target)
    {
        if (!GetCursorPos(out var point)) return false;
        var windowUnderCursor = WindowFromPoint(point);
        if (windowUnderCursor == IntPtr.Zero) return false;
        GetWindowThreadProcessId(windowUnderCursor, out var processId);
        return processId == target.ProcessId;
    }

    public static bool TryGetCursorScreenPosition(out Point position)
    {
        position = Point.Empty;
        if (!GetCursorPos(out var nativePoint)) return false;
        position = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool TryGetCursorClientPosition(WindowTarget target, out Point position, out bool insideClientArea)
    {
        position = Point.Empty;
        insideClientArea = false;
        if (!IsWindow(target.Handle) || !GetCursorPos(out var nativePoint)) return false;
        if (!ScreenToClient(target.Handle, ref nativePoint)) return false;
        if (!GetClientRect(target.Handle, out var clientRect)) return false;
        position = new Point(nativePoint.X, nativePoint.Y);
        insideClientArea = nativePoint.X >= clientRect.Left && nativePoint.X < clientRect.Right &&
            nativePoint.Y >= clientRect.Top && nativePoint.Y < clientRect.Bottom;
        return true;
    }

    public static bool TryMoveCursorToClientPosition(WindowTarget target, int x, int y, out string error)
    {
        error = "";
        if (!IsWindow(target.Handle) || !GetClientRect(target.Handle, out var clientRect))
        {
            error = "выбранное окно недоступно";
            return false;
        }
        if (x < clientRect.Left || x >= clientRect.Right || y < clientRect.Top || y >= clientRect.Bottom)
        {
            error = $"координаты X={x}, Y={y} находятся вне клиентской области {clientRect.Right}×{clientRect.Bottom}";
            return false;
        }

        var point = new NativePoint { X = x, Y = y };
        if (!ClientToScreen(target.Handle, ref point) || !SetCursorPos(point.X, point.Y) || !IsCursorAt(point.X, point.Y))
        {
            error = "Windows не смогла переместить курсор в заданную точку";
            return false;
        }
        return true;
    }

    public static bool TryMoveCursorToScreenPosition(int x, int y, out string error)
    {
        error = "";
        if (!SystemInformation.VirtualScreen.Contains(x, y))
        {
            var area = SystemInformation.VirtualScreen;
            error = $"координаты X={x}, Y={y} находятся вне виртуального экрана " +
                $"({area.Left}…{area.Right - 1}, {area.Top}…{area.Bottom - 1})";
            return false;
        }
        if (!SetCursorPos(x, y) || !IsCursorAt(x, y))
        {
            error = "Windows не смогла переместить курсор в заданную точку экрана";
            return false;
        }
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out RECT rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int ShowRestore = 9;
}
