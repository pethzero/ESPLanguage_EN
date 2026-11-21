using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ESPLanguageEN;

// dark theme: colors, fonts, icon glyphs, toolbar renderer and a few painted controls
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(24, 25, 30);
    public static readonly Color Surface = Color.FromArgb(32, 34, 40);
    public static readonly Color Surface2 = Color.FromArgb(42, 44, 52);
    public static readonly Color Hover = Color.FromArgb(52, 55, 64);
    public static readonly Color Border = Color.FromArgb(58, 61, 72);
    public static readonly Color Text = Color.FromArgb(230, 231, 236);
    public static readonly Color TextDim = Color.FromArgb(150, 153, 166);
    public static readonly Color TextFaint = Color.FromArgb(100, 103, 116);
    public static readonly Color Accent = Color.FromArgb(124, 108, 242);
    public static readonly Color AccentHover = Color.FromArgb(142, 128, 248);
    public static readonly Color AccentDown = Color.FromArgb(104, 90, 220);
    public static readonly Color AccentText = Color.FromArgb(190, 182, 255);
    public static readonly Color Foreign = Color.FromArgb(232, 172, 64);
    public static readonly Color Changed = Color.FromArgb(76, 195, 138);
    public static readonly Color Invalid = Color.FromArgb(229, 83, 75);

    public static readonly Font Ui = new("Segoe UI", 9.5f);
    public static readonly Font UiBold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Title = new("Segoe UI Semibold", 14f);
    public static readonly Font Big = new("Segoe UI Semibold", 17f);
    public static readonly Font Mono = new("Consolas", 9.5f);

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using var p = Round(r, rad);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, Color c, RectangleF r, float rad, float width = 1f, DashStyle dash = DashStyle.Solid)
    {
        using var p = Round(r, rad);
        using var pen = new Pen(c, width) { DashStyle = dash };
        g.DrawPath(pen, p);
    }

    // ------------------------------------------------------------ icons (Windows icon font)
    static readonly string IconFamily =
        new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static Bitmap Glyph(char ch, Color c, int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var f = new Font(IconFamily, size * 0.72f, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, ch.ToString(), f, new Rectangle(0, 0, size, size), c,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bmp;
    }

    public static Icon AppIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            FillRound(g, Accent, new RectangleF(1, 1, 30, 30), 7);
            using var f = new Font("Segoe UI Black", 12f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "EN", f, new Rectangle(0, 0, 32, 32), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ------------------------------------------------------------ native dark title bar / scrollbars
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

    public static void DarkTitleBar(Form f)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(f.Handle, 20, ref on, 4);                          // immersive dark mode
            int cap = Bg.R | Bg.G << 8 | Bg.B << 16;
            DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);                         // caption color (Win11)
        }
        catch { }
    }

    public static void DarkScroll(Control c)
    {
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
        foreach (Control k in c.Controls) DarkScroll(k);
    }

    public static void DarkCombo(ToolStripComboBox c)
    {
        c.ComboBox.FlatStyle = FlatStyle.Standard;
        c.ComboBox.BackColor = Surface2;
        c.ComboBox.ForeColor = Text;
        c.ComboBox.HandleCreated += (_, _) => { try { SetWindowTheme(c.ComboBox.Handle, "DarkMode_CFD", null); } catch { } };
    }

    public static void DarkSearch(ToolStripTextBox t, int width)
    {
        t.AutoSize = false;
        t.Size = new Size(width, 24);
        t.BackColor = Surface2;
        t.ForeColor = Text;
        t.Margin = new Padding(4, 1, 4, 1);
    }

    // plain controls of a small dialog
    public static void Apply(Control root)
    {
        root.BackColor = root is TextBox ? Surface2 : root is Button ? Surface2 : Bg;
        root.ForeColor = Text;
        if (root is Button b) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = Hover; b.Height = 30; }
        if (root is TextBox t) t.BorderStyle = BorderStyle.FixedSingle;
        if (root is Label) root.ForeColor = TextDim;
        foreach (Control c in root.Controls) Apply(c);
    }
}

sealed class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surface2;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Theme.Hover;
    public override Color MenuItemSelected => Theme.Hover;
    public override Color ImageMarginGradientBegin => Theme.Surface2;
    public override Color ImageMarginGradientMiddle => Theme.Surface2;
    public override Color ImageMarginGradientEnd => Theme.Surface2;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Theme.Border;
}

// flat toolbar: rounded hover, accent buttons (Tag = "accent"), toggle buttons with an accent outline
sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    static bool IsAccent(ToolStripItem i) => i.Tag as string == "accent";

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(e.ToolStrip is ToolStripDropDown ? Theme.Surface2 : e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var p = new Pen(Theme.Border);
        var r = e.ToolStrip.ClientRectangle;
        if (e.ToolStrip is ToolStripDropDown) e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        else if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(p, 0, 0, r.Width, 0);
        else e.Graphics.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        bool chk = item is ToolStripButton tb && tb.Checked;
        Color? fill = null;
        if (IsAccent(item))
            fill = !item.Enabled ? Theme.Surface2 : item.Pressed ? Theme.AccentDown : item.Selected ? Theme.AccentHover : Theme.Accent;
        else if (chk)
            fill = Theme.Mix(Theme.Surface, Theme.Accent, item.Selected ? 0.32f : 0.22f);
        else if (item.Pressed) fill = Theme.Border;
        else if (item.Selected && item.Enabled) fill = Theme.Hover;
        if (fill == null) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 1.5f, item.Width - 1.5f, item.Height - 3f);
        Theme.FillRound(g, fill.Value, r, 6);
        if (chk && !IsAccent(item)) Theme.DrawRound(g, Theme.Mix(Theme.Surface, Theme.Accent, 0.7f), r, 6);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.FillRound(e.Graphics, Theme.Hover, new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2), 5);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var i = e.Item;
        e.TextColor = !i.Enabled ? Theme.TextFaint
            : IsAccent(i) ? Color.White
            : i is ToolStripButton { Checked: true } ? Theme.AccentText
            : i is ToolStripLabel && i is not ToolStripStatusLabel ? Theme.TextDim
            : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Border);
        var r = e.Item.ContentRectangle;
        if (e.Vertical) { int x = r.Width / 2; e.Graphics.DrawLine(p, x, 5, x, e.Item.Height - 5); }
        else e.Graphics.DrawLine(p, 8, r.Height / 2, e.Item.Width - 8, r.Height / 2);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.TextDim; base.OnRenderArrow(e); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
}

// "1,234 / Not English" card in the header; click = action
sealed class StatCard : Control
{
    public Color Dot = Theme.Accent;
    public string Caption = "";
    public bool Active;
    bool hot;

    public StatCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(128, 50);
        Margin = new Padding(8, 0, 0, 0);
        Text = "–";
    }

    public void Set(string value, bool active) { if (Text != value || Active != active) { Text = value; Active = active; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Active ? Theme.Mix(Theme.Surface2, Dot, 0.16f) : hot && Cursor == Cursors.Hand ? Theme.Hover : Theme.Surface2, r, 8);
        Theme.DrawRound(g, Active ? Theme.Mix(Theme.Surface2, Dot, 0.75f) : Theme.Border, r, 8);
        TextRenderer.DrawText(g, Text, Theme.Big, new Rectangle(12, 3, Width - 16, 30), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 13, 35, 7, 7);
        TextRenderer.DrawText(g, Caption, Theme.Small, new Rectangle(24, 30, Width - 28, 16), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

// empty state: big dashed drop target
sealed class DropZone : Control
{
    public bool Hot;

    public DropZone()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        AllowDrop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);
        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        Theme.FillRound(g, Hot ? Theme.Mix(Theme.Bg, Theme.Accent, 0.08f) : Theme.Surface, r, 14);
        Theme.DrawRound(g, Hot ? Theme.Accent : Theme.Border, r, 14, 2f, DashStyle.Dash);

        int cx = Width / 2, cy = Height / 2;
        var circle = new RectangleF(cx - 36, cy - 92, 72, 72);
        using (var b = new SolidBrush(Theme.Mix(Theme.Surface, Theme.Accent, Hot ? 0.35f : 0.2f))) g.FillEllipse(b, circle);
        using (var ico = Theme.Glyph('', Theme.AccentText, 36)) g.DrawImage(ico, cx - 18, cy - 74);

        TextRenderer.DrawText(g, "Drop a plugin here", Theme.Title, new Rectangle(0, cy - 8, Width, 32), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, ".esp  ·  .esm  ·  .esl      or click to browse", Theme.Ui, new Rectangle(0, cy + 24, Width, 22), Theme.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Names that are not English (Korean, Chinese, Japanese, broken UTF-8) are listed in amber.",
            Theme.Small, new Rectangle(0, cy + 60, Width, 20), Theme.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// thin animated line under the toolbar while translating
sealed class BusyBar : Control
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    float pos;
    bool running;

    public BusyBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Height = 3;
        Dock = DockStyle.Top;
        timer.Tick += (_, _) => { pos = (pos + 0.012f) % 1.4f; Invalidate(); };
    }

    public bool Running
    {
        get => running;
        set { running = value; timer.Enabled = value; pos = 0; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
        if (!running) return;
        float w = Width * 0.3f, x = (pos - 0.3f) * Width;
        using var b = new LinearGradientBrush(new RectangleF(x - 1, 0, w + 2, Height), Color.FromArgb(0, Theme.Accent), Theme.AccentHover, 0f);
        b.SetBlendTriangularShape(0.5f);
        e.Graphics.FillRectangle(b, x, 0, w, Height);
    }

    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
