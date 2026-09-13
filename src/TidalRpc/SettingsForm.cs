namespace TidalRpc;

public sealed class SettingsForm : Form
{
    private readonly TextBox country = new() { Name = "Country", Width = 80, MaxLength = 2, CharacterCasing = CharacterCasing.Upper, AccessibleName = "Country" };
    private readonly Switch enabled = new() { Name = "Sharing", AccessibleName = "Share on Discord" };
    private readonly Switch startup = new() { Name = "Startup", AccessibleName = "Start with Windows" };
    private readonly Switch artwork = new() { Name = "Artwork", AccessibleName = "Album artwork" };
    private readonly Switch timer = new() { Name = "PlaybackTime", AccessibleName = "Playback time" };
    private readonly Switch link = new() { Name = "TidalButton", AccessibleName = "Play on TIDAL button" };
    private readonly Switch openOnLaunch = new() { Name = "OpenOnLaunch", AccessibleName = "Open settings on launch" };
    private readonly ComboBox statusText = new StyledComboBox { Name = "StatusText", Width = 165, AccessibleName = "Status text" };
    private readonly ComboBox theme = new StyledComboBox { Name = "Theme", Width = 165, AccessibleName = "Theme" };
    private readonly Label status = new() { Name = "Status", AutoSize = true, Tag = "muted", Text = "Waiting for TIDAL", MaximumSize = new Size(480, 0) };
    private readonly Label saveError = new() { Name = "SaveError", AutoSize = true, Tag = "error", Visible = false, Dock = DockStyle.Top, MaximumSize = new Size(550, 0) };
    private AppSettings lastSaved;
    private bool syncing;
    public event Action<AppSettings>? Saved;
    public event Action? ReportRequested;
    public event Action? UpdateRequested;
    public SettingsForm(AppSettings settings)
    {
        lastSaved = settings; Text = "TIDAL RPC";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10);
        ClientSize = new Size(630, 690); MinimumSize = new Size(570, 640); StartPosition = FormStartPosition.CenterScreen; Icon = AppBrand.Icon;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, Tag = "background" };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); Controls.Add(root);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Tag = "background", Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var mark = new BrandMark { Tag = "background", Margin = new Padding(0, 5, 0, 0) }; header.Controls.Add(mark, 0, 0); header.SetRowSpan(mark, 2);
        header.Controls.Add(new Label { Text = "TIDAL RPC", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Tag = "background", Margin = Padding.Empty }, 1, 0);
        header.Controls.Add(status, 1, 1); root.Controls.Add(header, 0, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Tag = "background", Margin = Padding.Empty };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Tag = "background", Margin = Padding.Empty, Padding = new Padding(0, 0, 4, 0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroll.Controls.Add(content); root.Controls.Add(scroll, 0, 1); content.Controls.Add(saveError);
        var discord = Section("DISCORD"); content.Controls.Add(discord); AddRow(discord, "Share on Discord", enabled);
        statusText.Items.AddRange(["Artist", "Song title", "TIDAL"]); AddRow(discord, "Status text", statusText);
        AddRow(discord, "Album artwork", artwork); AddRow(discord, "Playback time", timer); AddRow(discord, "Play on TIDAL button", link);
        var app = Section("PREFERENCES"); content.Controls.Add(app);
        theme.Items.AddRange(["Dark", "Light", "Follow Windows"]); AddRow(app, "Appearance", theme); AddRow(app, "Country", country);
        AddRow(app, "Start with Windows", startup); AddRow(app, "Open settings on launch", openOnLaunch);
        var updates = new Button { Text = "Check for updates", AutoSize = true, Cursor = Cursors.Hand };
        updates.Click += (_, _) => UpdateRequested?.Invoke(); AddRow(app, "Updates", updates);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Tag = "background", Padding = new Padding(0, 12, 0, 0), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var report = new Button { Name = "Report", Text = "Report a problem", AutoSize = true, Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand, Margin = Padding.Empty };
        report.Click += (_, _) => ReportRequested?.Invoke(); footer.Controls.Add(report, 0, 0);
        footer.Controls.Add(new Label { Text = "v" + ReleaseConfiguration.Version, Tag = "muted", AutoSize = true, Anchor = AnchorStyles.Right, Margin = Padding.Empty }, 1, 0); root.Controls.Add(footer, 0, 2);
        country.Text = settings.Country; SetSettings(settings);
        country.Leave += (_, _) => SaveChanges(true);
        country.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { SaveChanges(true); e.SuppressKeyPress = true; } };
        foreach (var toggle in new[] { enabled, startup, artwork, timer, link, openOnLaunch }) toggle.CheckedChanged += (_, _) => SaveChanges(false);
        statusText.SelectedIndexChanged += (_, _) => SaveChanges(false); theme.SelectedIndexChanged += (_, _) => SaveChanges(false);
    }
    private Card Section(string title)
    {
        var card = new Card(); card.Controls.Add(new Label { Text = title, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), AutoSize = true, Tag = "muted", Margin = new Padding(2, 1, 0, 8) }); return card;
    }
    private static void AddRow(Card card, string title, Control input)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 39), Margin = Padding.Empty, Padding = new Padding(2, 4, 2, 4) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 14, 0) }, 0, 0);
        input.Anchor = AnchorStyles.Right; input.Margin = Padding.Empty; row.Controls.Add(input, 1, 0); card.Controls.Add(row);
    }
    private bool SaveChanges(bool editCountry)
    {
        if (syncing) return true;
        try
        {
            var value = editCountry ? lastSaved with { Country = country.Text.Trim().ToUpperInvariant() }
                : lastSaved with { Enabled = enabled.Checked, StartWithWindows = startup.Checked, ShowArtwork = artwork.Checked,
                    ShowPlaybackTime = timer.Checked, ShowTidalButton = link.Checked, StatusText = (StatusTextMode)statusText.SelectedIndex,
                    Theme = (AppTheme)theme.SelectedIndex, OpenSettingsOnLaunch = openOnLaunch.Checked };
            SettingsStore.Validate(value); if (value != lastSaved) Saved?.Invoke(value); lastSaved = value;
            UiStyle.Apply(this, UiColors.For(value.Theme));
            if (editCountry || country.Text.Trim().Equals(lastSaved.Country, StringComparison.OrdinalIgnoreCase)) saveError.Visible = false;
            return true;
        }
        catch (Exception ex)
        {
            DebugConsole.Write("Settings", $"Save failed: {ex.GetType().Name}");
            saveError.Text = ex is InvalidOperationException ? ex.Message : "Couldn't save changes. Try again.";
            saveError.Visible = true; SetSettings(lastSaved); return false;
        }
    }
    public void SetSettings(AppSettings settings)
    {
        syncing = true;
        try
        {
            if (settings.Country != lastSaved.Country) country.Text = settings.Country;
            enabled.Checked = settings.Enabled; startup.Checked = settings.StartWithWindows; artwork.Checked = settings.ShowArtwork;
            timer.Checked = settings.ShowPlaybackTime; link.Checked = settings.ShowTidalButton; statusText.SelectedIndex = (int)settings.StatusText;
            theme.SelectedIndex = (int)settings.Theme; openOnLaunch.Checked = settings.OpenSettingsOnLaunch; lastSaved = settings;
            UiStyle.Apply(this, UiColors.For(settings.Theme));
        }
        finally { syncing = false; }
    }
    public void SetStatus(string text) { if (!IsDisposed && status.Text != text) status.Text = text; }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; if (SaveChanges(true)) Hide(); }
        base.OnFormClosing(e);
    }
}
