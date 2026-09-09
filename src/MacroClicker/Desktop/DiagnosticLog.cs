using System.Text;

namespace MacroClicker;

internal sealed class DiagnosticLog(string filePath)
{
    private readonly object _gate = new();
    private readonly StringBuilder _recent = new();
    private string? _writeError;
    public string FilePath { get; } = filePath;

    public void Write(string level, string message, Exception? error = null)
    {
        var details = error is null ? message : message + Environment.NewLine + error;
        if (details.Length > 16000) details = details[..16000] + " [truncated]";
        var line = $"{DateTimeOffset.Now:O} [{level}] {details}{Environment.NewLine}";
        lock (_gate)
        {
            _recent.Append(line);
            if (_recent.Length > 262144) _recent.Remove(0, _recent.Length - 262144);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
                _writeError = null;
            }
            catch (Exception failure) { _writeError = failure.Message; }
        }
    }

    public string Snapshot()
    {
        lock (_gate)
            return "Журнал: " + FilePath + Environment.NewLine +
                (_writeError is null ? "" : "Не удалось сохранить журнал на диск: " + _writeError + Environment.NewLine) +
                "Последние записи текущего запуска:" + Environment.NewLine + Environment.NewLine + _recent;
    }
}

internal static class AppLog
{
    public static DiagnosticLog Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacroClicker", "Logs",
        $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log"));
}
