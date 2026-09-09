namespace MacroClicker;

internal static class Ui
{
    private static readonly Icon AppIcon = LoadIcon();
    private static Icon LoadIcon()
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("MacroClicker.AppIcon")
            ?? throw new InvalidOperationException("Не найдена иконка приложения.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
    public static bool Dark { get; set; }
    public static Color Background => SystemInformation.HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(246, 246, 245);
    public static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Dark ? Color.FromArgb(42, 42, 42) : Color.White;
    public static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(35, 35, 35);
    public static Color Muted => Dark ? Color.FromArgb(185, 185, 185) : Color.FromArgb(96, 96, 96);
    public static Color Border => Dark ? Color.FromArgb(75, 75, 75) : Color.FromArgb(218, 218, 218);
    public static Color Accent => Dark ? Color.FromArgb(140, 210, 186) : Color.FromArgb(26, 107, 85);
    public static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(72, 36), Margin = new Padding(4), Padding = new Padding(12, 2, 12, 2) };
        b.Click += (_, _) => action();
        return b;
    }
    public static Label Label(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 8) };
    public static FlowLayoutPanel Row() => new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
    public static NumericUpDown Number(int value, int min = 0, int max = 600000) => new()
    { Value = Math.Clamp(value, min, max), Minimum = min, Maximum = max, Width = 130, ThousandsSeparator = true };
    public static void Theme(Control root)
    {
        var inCard = false;
        for (Control? parent = root; parent is not null; parent = parent.Parent)
            if (Equals(parent.Tag, "card")) { inCard = true; break; }
        root.BackColor = root is Label ? Color.Transparent
            : root is TextBoxBase or ComboBox or NumericUpDown or ListBox || inCard ? Surface : Background;
        root.ForeColor = Equals(root.Tag, "muted") && !SystemInformation.HighContrast ? Muted : Ink;
        if (root is Form form) { form.Font = new Font("Segoe UI", 10); form.Icon = AppIcon; }
        if (root is Button b)
        {
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.MouseOverBackColor = Dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(235, 235, 235);
            b.BackColor = Surface;
            b.UseVisualStyleBackColor = false;
        }
        if (root is ComboBox combo)
        {
            combo.FormattingEnabled = true;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem -= DrawComboItem;
            combo.DrawItem += DrawComboItem;
        }
        root.FontChanged -= ResizeInput;
        root.FontChanged += ResizeInput;
        root.DpiChangedAfterParent -= ResizeInput;
        root.DpiChangedAfterParent += ResizeInput;
        root.HandleCreated -= ResizeInput;
        root.HandleCreated += ResizeInput;
        ResizeInput(root, EventArgs.Empty);
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Surface; grid.GridColor = Border; grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Dark ? Color.FromArgb(50, 87, 76) : Color.FromArgb(215, 235, 226);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        }
        foreach (Control child in root.Controls) Theme(child);
        if (root is ToolStrip strip)
            foreach (ToolStripItem item in strip.Items) ThemeItem(item);
    }
    private static void ResizeInput(object? sender, EventArgs e)
    {
        if (sender is not Control control) return;
        var height = Math.Max((int)Math.Round(36 * control.DeviceDpi / 96d), control.Font.Height + 12);
        if (control is ComboBox combo && combo.DrawMode == DrawMode.OwnerDrawFixed)
        {
            // WinForms uses ItemHeight + two native fixed-frame borders.
            combo.ItemHeight = Math.Max(control.Font.Height, height - 2 * SystemInformation.FixedFrameBorderSize.Height);
            combo.Height = combo.PreferredHeight;
            combo.Margin = new Padding(4);
        }
        if (control is Button { AutoSize: true } button)
        {
            button.MaximumSize = new Size(0, height);
            button.MinimumSize = new Size(button.MinimumSize.Width, height);
            button.Height = height;
        }
        if (control is TextBox { Multiline: false } text)
        {
            text.AutoSize = false; text.Height = height; text.Margin = new Padding(4);
        }
        if (control is CheckBox check)
        {
            check.MinimumSize = new Size(0, height); check.Margin = new Padding(4);
        }
    }
    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected ? SystemColors.Highlight : Surface;
        var foreground = !combo.Enabled ? SystemColors.GrayText : selected ? SystemColors.HighlightText : Ink;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -4, 0);
        var text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
        TextRenderer.DrawText(e.Graphics, text, combo.Font, bounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
    private static void ThemeItem(ToolStripItem item)
    {
        item.BackColor = Background; item.ForeColor = Ink;
        if (item is ToolStripDropDownItem drop) foreach (ToolStripItem child in drop.DropDownItems) ThemeItem(child);
    }
    public static void Error(IWin32Window owner, Exception error) => MessageBox.Show(owner, error.Message, "MacroClicker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
