using System.Diagnostics;

namespace TidalRpc;

public sealed class ReportForm : Form
{
    public ReportForm(AppSettings settings, PlaybackSnapshot? track, Uri? repository = null,
        Action<Uri>? openBrowser = null, Action<string>? copyText = null)
    {
        repository ??= ReleaseConfiguration.Repository;
        openBrowser ??= uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        copyText ??= Clipboard.SetText;
        Text = "Report a problem"; Icon = AppBrand.Icon; Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(590, 530); MinimumSize = new Size(550, 490); StartPosition = FormStartPosition.CenterParent;
        var history = DebugConsole.Snapshot().Text;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7, Tag = "background" };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "What went wrong?", AutoSize = true, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Tag = "background", Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        var description = new TextBox { Name = "Description", Multiline = true, Height = 85, Dock = DockStyle.Top, MaxLength = 1000, AccessibleName = "Describe the problem", PlaceholderText = "What happened, and what did you expect?" };
        root.Controls.Add(description, 0, 1);
        var include = new CheckBox { Name = "IncludeSong", Text = "Include the current song", AutoSize = true, Margin = new Padding(0, 12, 0, 10), Tag = "background" };
        root.Controls.Add(include, 0, 2);
        root.Controls.Add(new Label { Text = "Report preview", AutoSize = true, Tag = "muted", Margin = new Padding(0, 0, 0, 6) }, 0, 3);
        var preview = new TextBox { Name = "ReportPreview", ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
        root.Controls.Add(preview, 0, 4);
        var feedback = new Label { Name = "ReportFeedback", AutoSize = true, MaximumSize = new Size(525, 0), Tag = "muted", Margin = new Padding(0, 10, 0, 10),
            Text = repository is null ? "GitHub reporting isn't set up yet. You can still copy this report." : "You'll review and submit this on GitHub." };
        root.Controls.Add(feedback, 0, 5);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Tag = "background", Margin = Padding.Empty };
        var open = new Button { Name = "OpenGitHub", Text = "Open GitHub", AutoSize = true, Enabled = repository is not null, Padding = new Padding(8, 3, 8, 3) };
        var copy = new Button { Name = "CopyReport", Text = "Copy report", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(8, 3, 8, 3) };
        buttons.Controls.AddRange([open, copy, cancel]); root.Controls.Add(buttons, 0, 6); CancelButton = cancel;
        void RefreshReport() => preview.Text = IssueReport.Create(settings, track, history, description.Text, include.Checked);
        description.TextChanged += (_, _) => RefreshReport(); include.CheckedChanged += (_, _) => RefreshReport(); RefreshReport();
        copy.Click += (_, _) =>
        {
            try { copyText(preview.Text); feedback.Text = "Report copied."; }
            catch { feedback.Text = "Couldn't copy. Select the report text and press Ctrl+C."; }
        };
        open.Click += (_, _) =>
        {
            if (repository is null) return;
            try { copyText(preview.Text); }
            catch { feedback.Text = "Couldn't copy the report. Try again."; return; }
            try { openBrowser(IssueReport.CreateIssueUri(repository, preview.Text)); feedback.Text = "Opened on GitHub. The full report is copied if you need it."; }
            catch { feedback.Text = "Couldn't open GitHub. Your report has been copied."; }
        };
        UiStyle.Apply(this, UiColors.For(settings.Theme));
    }
}
