using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MacroClicker;

internal sealed class JsonEditor : UserControl
{
    private readonly RichTextBox _text = new()
    {
        Dock = DockStyle.Fill, Font = new Font("Consolas", 11), AcceptsTab = true,
        WordWrap = false, DetectUrls = false, BorderStyle = BorderStyle.None
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 400 };
    private bool _coloring;
    public bool Dirty { get; private set; }
    public string Json => _text.Text;
    private static readonly Regex Tokens = new(
        "(?<key>\"(?:\\\\.|[^\"\\\\])*\"\\s*(?=:))|(?<str>\"(?:\\\\.|[^\"\\\\])*\")|(?<lit>\\b(?:true|false|null)\\b)|(?<num>-?\\b\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
    public JsonEditor()
    {
        Dock = DockStyle.Fill;
        Controls.Add(_text);
        _text.TextChanged += (_, _) =>
        {
            if (_coloring) return;
            Dirty = true; _timer.Stop(); _timer.Start();
        };
        _text.VScroll += (_, _) => { _timer.Stop(); _timer.Start(); };
        _timer.Tick += (_, _) => { _timer.Stop(); Highlight(); };
        Disposed += (_, _) => _timer.Dispose();
    }
    public void SetJson(string json)
    {
        _text.Text = json; Dirty = false; _text.ClearUndo(); Highlight();
    }
    public void RefreshColors() => Highlight();
    private void Highlight()
    {
        if (_coloring || !_text.IsHandleCreated) return;
        _coloring = true;
        var start = _text.SelectionStart; var length = _text.SelectionLength;
        var scroll = new NativePoint();
        SendMessage(_text.Handle, 0x4DD, IntPtr.Zero, ref scroll); // EM_GETSCROLLPOS
        SendMessage(_text.Handle, 0xB, IntPtr.Zero, IntPtr.Zero);
        try
        {
            // Для больших макросов подсвечиваем видимую область, чтобы редактор оставался отзывчивым.
            var from = _text.TextLength > 200000 ? _text.GetCharIndexFromPosition(Point.Empty) : 0;
            var to = _text.TextLength > 200000
                ? Math.Min(_text.TextLength, _text.GetCharIndexFromPosition(new Point(_text.Width, _text.Height)) + 2000)
                : _text.TextLength;
            var fragment = _text.Text.Substring(from, to - from);
            _text.Select(from, fragment.Length); _text.SelectionColor = Ui.Ink;
            foreach (Match match in Tokens.Matches(fragment))
            {
                _text.Select(from + match.Index, match.Length);
                _text.SelectionColor = SystemInformation.HighContrast ? Ui.Ink :
                    match.Groups["key"].Success ? (Ui.Dark ? Color.FromArgb(145, 195, 242) : Color.FromArgb(24, 86, 155)) :
                    match.Groups["str"].Success ? (Ui.Dark ? Color.FromArgb(164, 209, 173) : Color.FromArgb(28, 113, 57)) :
                    match.Groups["lit"].Success ? (Ui.Dark ? Color.FromArgb(205, 164, 235) : Color.FromArgb(126, 51, 155)) :
                    (Ui.Dark ? Color.FromArgb(234, 190, 140) : Color.FromArgb(157, 79, 20));
            }
        }
        catch (RegexMatchTimeoutException) { }
        finally
        {
            _text.Select(start, length);
            SendMessage(_text.Handle, 0x4DE, IntPtr.Zero, ref scroll);
            SendMessage(_text.Handle, 0xB, new IntPtr(1), IntPtr.Zero);
            _text.Invalidate(); _coloring = false;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref NativePoint lParam);
}
