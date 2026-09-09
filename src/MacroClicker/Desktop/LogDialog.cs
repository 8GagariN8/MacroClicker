namespace MacroClicker;

// Native fallback: available even if the web renderer itself has failed.
internal sealed class LogDialog : Form
{
    public LogDialog()
    {
        Text = "Журнал MacroClicker"; ClientSize = new Size(900, 600); MinimumSize = new Size(620, 400);
        StartPosition = FormStartPosition.CenterParent; Padding = new Padding(16);
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52 };
        void RefreshLog() { text.Text = AppLog.Current.Snapshot(); text.SelectionStart = text.TextLength; text.ScrollToCaret(); }
        void Guard(Action action) { try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, "Журнал"); } }
        buttons.Controls.Add(Ui.Button("Обновить", RefreshLog));
        buttons.Controls.Add(Ui.Button("Копировать", () => Guard(() => Clipboard.SetText(text.Text))));
        buttons.Controls.Add(Ui.Button("Папка журналов", () => Guard(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(AppLog.Current.FilePath)!) { UseShellExecute = true }))));
        Controls.Add(text); Controls.Add(buttons); Ui.Theme(this); text.Font = new Font("Consolas", 10); RefreshLog();
    }
}
