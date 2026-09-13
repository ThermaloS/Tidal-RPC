using System.Drawing.Drawing2D;
using System.ComponentModel;
using Microsoft.Win32;

namespace TidalRpc;

internal sealed record UiColors(Color Background, Color Surface, Color Text, Color Muted, Color Border, Color Accent, Color Error)
{
    public static UiColors For(AppTheme theme)
    {
        if (SystemInformation.HighContrast) return new(SystemColors.Window, SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Highlight, SystemColors.WindowText);
        bool dark = theme == AppTheme.Dark;
        if (theme == AppTheme.System)
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
            catch { dark = false; }
        }
        return dark
            ? new(Color.FromArgb(17, 20, 26), Color.FromArgb(26, 31, 39), Color.FromArgb(240, 243, 248), Color.FromArgb(158, 170, 188), Color.FromArgb(48, 58, 71), Color.FromArgb(77, 218, 188), Color.FromArgb(255, 147, 151))
            : new(Color.FromArgb(244, 246, 249), Color.White, Color.FromArgb(29, 38, 49), Color.FromArgb(86, 101, 118), Color.FromArgb(215, 224, 232), Color.FromArgb(0, 119, 104), Color.FromArgb(178, 42, 55));
    }
}
internal static class UiStyle
{
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    public static void Apply(Control root, UiColors colors, bool background = false)
    {
        bool isBackground = root is Form || Equals(root.Tag, "background") || (background && root is Label);
        root.BackColor = isBackground ? colors.Background : colors.Surface;
        root.ForeColor = Equals(root.Tag, "muted") ? colors.Muted : Equals(root.Tag, "error") ? colors.Error : colors.Text;
        if (root is Switch toggle) toggle.Colors = colors;
        if (root is BrandMark brand) brand.Colors = colors;
        if (root is Card card) card.Colors = colors;
        if (root is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = colors.Border; }
        if (root is TextBox box) box.BorderStyle = BorderStyle.FixedSingle;
        foreach (Control control in root.Controls) Apply(control, colors, isBackground);
        root.Invalidate();
    }
}
internal sealed class Card : TableLayoutPanel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal UiColors Colors { get; set; } = UiColors.For(AppTheme.Dark);
    public Card() { DoubleBuffered = true; AutoSize = true; ColumnCount = 1; Dock = DockStyle.Top; Padding = new Padding(16, 12, 16, 12); Margin = new Padding(0, 0, 0, 14); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Colors.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiStyle.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 12 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Colors.Surface); using var border = new Pen(Colors.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}
internal sealed class Switch : CheckBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal UiColors Colors { get; set; } = UiColors.For(AppTheme.Dark);
    public Switch() { AutoSize = false; Size = new Size(46, 26); Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(2, 3, Width - 4, Height - 6); using var path = UiStyle.Rounded(rect, rect.Height / 2);
        using var fill = new SolidBrush(Enabled && Checked ? Colors.Accent : Colors.Border); e.Graphics.FillPath(fill, path);
        float diameter = rect.Height - 6; using var thumb = new SolidBrush(Checked ? Colors.Background : Colors.Muted);
        e.Graphics.FillEllipse(thumb, Checked ? rect.Right - diameter - 3 : rect.Left + 3, rect.Top + 3, diameter, diameter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Colors.Text, BackColor);
    }
}
internal sealed class BrandMark : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal UiColors Colors { get; set; } = UiColors.For(AppTheme.Dark);
    public BrandMark() { Size = new Size(52, 52); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var background = new SolidBrush(Colors.Accent);
        using var path = UiStyle.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Width * .25f); e.Graphics.FillPath(background, path);
        using var bars = new SolidBrush(Colors.Background);
        for (int i = 0; i < 3; i++)
        {
            float height = Height * (i == 1 ? .58f : .34f);
            using var bar = UiStyle.Rounded(new RectangleF(Width * (.25f + i * .19f), (Height - height) / 2, Width * .11f, height), Width * .06f);
            e.Graphics.FillPath(bars, bar);
        }
    }
}
internal sealed class StyledComboBox : ComboBox
{
    public StyledComboBox() { DrawMode = DrawMode.OwnerDrawFixed; DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat; ItemHeight = Font.Height + 4; }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 4; }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0 && DroppedDown;
        using var background = new SolidBrush(selected ? SystemColors.Highlight : BackColor); e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -5, 0), selected ? SystemColors.HighlightText : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
