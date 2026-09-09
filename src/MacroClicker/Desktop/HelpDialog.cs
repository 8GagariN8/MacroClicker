using System.Reflection;

namespace MacroClicker;

internal sealed class HelpDialog : Form
{
    private sealed record Topic(string Title, string Resource)
    {
        public override string ToString() => Title;
    }

    public HelpDialog()
    {
        Text = "Справка · MacroClicker";
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(740, 480);
        StartPosition = FormStartPosition.CenterParent;
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Size = new Size(1000, 650), FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 230, Panel1MinSize = 180, Panel2MinSize = 400
        };
        var topics = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
        var text = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
            DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
            AccessibleName = "Содержимое справки"
        };
        split.Panel1.Padding = new Padding(16);
        split.Panel2.Padding = new Padding(16);
        split.Panel1.Controls.Add(topics); split.Panel2.Controls.Add(text);
        topics.Items.AddRange(new object[]
        {
            new Topic("Быстрый старт", "QuickStart"),
            new Topic("Подробная инструкция", "Manual"),
            new Topic("Проверка на Windows", "WindowsChecks"),
            new Topic("Отчёт о проверках", "TestResults"),
            new Topic("Диагностика NuGet", "NuGet")
        });
        topics.SelectedIndexChanged += (_, _) =>
        {
            if (topics.SelectedItem is not Topic topic) return;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MacroClicker.Help." + topic.Resource)
                    ?? throw new InvalidOperationException("Раздел справки отсутствует в сборке.");
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                text.Text = reader.ReadToEnd().Replace("\r\n", "\n");
                text.SelectAll();
                using var bodyFont = new Font("Segoe UI", 10);
                text.SelectionFont = bodyFont;
                text.SelectionColor = Ui.Ink;
                // Выделяем заголовки встроенных Markdown-документов без браузера и внешних файлов.
                var offset = 0;
                foreach (var line in text.Lines)
                {
                    if (line.StartsWith("#"))
                    {
                        text.Select(offset, line.Length);
                        using var heading = new Font("Segoe UI", line.StartsWith("# ") ? 16 : 12, FontStyle.Bold);
                        text.SelectionFont = heading;
                    }
                    offset += line.Length + 1;
                }
                text.Select(0, 0); text.ScrollToCaret();
            }
            catch (Exception e) { text.Text = e.Message; }
        };
        var footer = Ui.Row(); footer.Dock = DockStyle.Bottom;
        footer.FlowDirection = FlowDirection.RightToLeft;
        var close = Ui.Button("Закрыть", Close); footer.Controls.Add(close);
        Controls.Add(split); Controls.Add(footer); CancelButton = close;
        Ui.Theme(this); topics.SelectedIndex = 0;
    }
}
