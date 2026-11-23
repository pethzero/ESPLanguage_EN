using System.ComponentModel;
using System.Text;

namespace ESPLanguageEN;

public sealed class MainForm : Form
{
    static readonly string BaseDir = AppContext.BaseDirectory;

    Plugin plugin;
    readonly List<TextEntry> all = new();
    readonly BindingList<TextEntry> view = new();
    readonly BindingSource bs = new();
    Translator tr;
    bool busy;

    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, BorderStyle = BorderStyle.None,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, EnableHeadersVisualStyles = false,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, AllowDrop = true,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 34,
    };
    readonly ToolStrip bar1 = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(10, 6, 10, 6) };
    readonly ToolStrip bar2 = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(10, 4, 10, 6) };
    readonly ToolStripComboBox cboEngine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    AppSettings settings = AppSettings.Load();
    readonly ToolStripTextBox txtSearch = new() { Width = 240, BorderStyle = BorderStyle.FixedSingle };
    readonly ToolStripButton chkForeign = new("Non-English only") { CheckOnClick = true, Checked = true };
    readonly ToolStripButton chkChanged = new("Changed only") { CheckOnClick = true };
    readonly ToolStripButton chkDesc = new("Include DESC") { CheckOnClick = true, ToolTipText = "Also list descriptions (DESC), e.g. book text and item descriptions" };
    readonly List<ToolStripItem> busyItems = new();
    readonly ToolStripStatusLabel status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel engineLabel = new() { IsLink = false, Margin = new Padding(12, 3, 12, 2) };
    readonly ToolStripStatusLabel hint = new("F2 edit  ·  F5 Auto EN  ·  Ctrl+S save  ·  Ctrl+F search") { ForeColor = Theme.TextFaint };
    readonly System.Windows.Forms.Timer searchDelay = new() { Interval = 250 };
    readonly Dictionary<string, int> pillWidth = new();
    DateTime glossaryStamp;
    bool closeAfterSave;

    readonly Label lblFile = new() { AutoSize = true, ForeColor = Theme.TextDim, Font = Theme.Ui, Text = "No plugin loaded" };
    readonly StatCard cardAll = new() { Caption = "Texts", Dot = Theme.TextDim };
    readonly StatCard cardForeign = new() { Caption = "Not English", Dot = Theme.Foreign, Cursor = Cursors.Hand };
    readonly StatCard cardChanged = new() { Caption = "Changed", Dot = Theme.Changed, Cursor = Cursors.Hand };
    readonly StatCard cardBad = new() { Caption = "Invalid chars", Dot = Theme.Invalid };
    readonly DropZone drop = new() { Dock = DockStyle.Fill };
    readonly BusyBar busyBar = new();
    Panel frame;
    DataGridViewColumn colStatus, colType, colEnglish;


    public MainForm(string startPath)
    {
        Text = "ESPLanguage_EN";
        ClientSize = new Size(1380, 820);
        MinimumSize = new Size(980, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Ui;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Icon = Theme.AppIcon();
        AllowDrop = true;
        KeyPreview = true;

        foreach (var e in AppSettings.EngineList) cboEngine.Items.Add(e.Name);
        cboEngine.SelectedItem = AppSettings.NameOf(settings.Engine);
        cboEngine.ToolTipText = "Engine used by Auto EN. Set each one up in Settings (keys, servers, models) and test it there.";
        Theme.DarkCombo(cboEngine);
        Theme.DarkSearch(txtSearch, 260);
        txtSearch.TextBox.PlaceholderText = "Search name, EditorID, FormID...";
        chkDesc.Checked = settings.IncludeDesc;

        // ---------------------------------------------------------- toolbars
        // bar1 = file and translate actions (left), engine + Settings (right)
        // bar2 = what the list shows (search, filters) and row actions
        var renderer = new DarkRenderer();
        foreach (var b in new[] { bar1, bar2 }) { b.Renderer = renderer; b.BackColor = Theme.Bg; b.ForeColor = Theme.Text; b.ImageScalingSize = new Size(16, 16); }

        ToolStripButton Btn(ToolStrip bar, string text, char icon, string tip, Action a, bool accent = false)
        {
            var b = new ToolStripButton(text)
            {
                ToolTipText = tip, Padding = new Padding(6, 3, 6, 3), Margin = new Padding(2, 1, 2, 1),
                Image = Theme.Glyph(icon, accent ? Color.White : Theme.Text),
                Tag = accent ? "accent" : null, Font = accent ? Theme.UiBold : Theme.Ui,
            };
            b.Click += (_, _) => a();
            bar.Items.Add(b);
            busyItems.Add(b);
            return b;
        }
        void Toggle(ToolStrip bar, ToolStripButton b) { b.Padding = new Padding(8, 3, 8, 3); b.Margin = new Padding(2, 1, 2, 1); bar.Items.Add(b); busyItems.Add(b); }
        void Sep(ToolStrip bar) => bar.Items.Add(new ToolStripSeparator { Margin = new Padding(6, 0, 6, 0) });

        Btn(bar1, "Open", '', "Open a plugin (Ctrl+O). You can also drag a .esp onto the window.", OpenDialog);
        Btn(bar1, "Save", '', "Write the changes into the plugin (Ctrl+S). A backup goes to the backups folder first.", () => _ = SaveAsync(plugin?.Path));
        Btn(bar1, "Save As", '', "Write to another file", SaveAs);
        Sep(bar1);
        Btn(bar1, "Auto EN selected", '', "Translate the selected rows to English (F5)", () => _ = TranslateAsync(Selected()));
        Btn(bar1, "Auto EN all", '', "Translate every row that is not English yet and has no English text", () =>
            _ = TranslateAsync(all.Where(e => e.IsForeign && string.IsNullOrEmpty(e.English)).ToList()), accent: true);
        Sep(bar1);
        Btn(bar1, "Export", '', "Export TSV: save the list as a tab-separated file (Excel / any translator / AI). Edit the English column and import it back.", ExportTsv);
        Btn(bar1, "Import", '', "Import TSV: read the English column back from an exported file", ImportTsv);
        Btn(bar1, "Glossary", '', "Open glossary.txt (your own words, used before any engine). It is reloaded when you come back to this window.", OpenGlossary);

        var settingsBtn = Btn(bar1, "Settings", '', "Engines (keys, servers, models, Test), translation and reading options", () => OpenSettings());
        bar1.Items.Remove(settingsBtn);
        foreach (var it in new ToolStripItem[] { settingsBtn, cboEngine, new ToolStripLabel("Engine") { Margin = new Padding(8, 1, 2, 2) } })
        {
            it.Alignment = ToolStripItemAlignment.Right;
            bar1.Items.Add(it);
        }
        busyItems.Add(cboEngine);

        var searchIcon = new ToolStripLabel { Image = Theme.Glyph('', Theme.TextDim), Margin = new Padding(0, 1, 0, 2) };
        bar2.Items.Add(searchIcon);
        bar2.Items.Add(txtSearch);
        Sep(bar2);
        Toggle(bar2, chkForeign);
        Toggle(bar2, chkChanged);
        Toggle(bar2, chkDesc);
        Sep(bar2);
        Btn(bar2, "Original → English", '', "Copy the original text into English for the selected rows (then edit by hand)", () => ForSelected(e => e.English = e.Original));
        Btn(bar2, "Revert", '', "Undo the English text of the selected rows", () => ForSelected(e => e.English = e.IsForeign ? "" : e.Original));

        // ---------------------------------------------------------- header
        var header = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Theme.Bg };
        var logo = new PictureBox { Image = Theme.AppIcon().ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(40, 40), Location = new Point(20, 20) };
        var title = new Label { Text = "ESPLanguage_EN", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(68, 14) };
        var tag = new Label { Text = "Korean / Chinese / Japanese plugin names  →  English", Font = Theme.Small, ForeColor = Theme.AccentText, AutoSize = true };
        lblFile.Location = new Point(70, 44);
        lblFile.AutoEllipsis = true;
        var cards = new FlowLayoutPanel { Anchor = AnchorStyles.Top | AnchorStyles.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = Padding.Empty };
        cards.Controls.AddRange(new Control[] { cardAll, cardForeign, cardChanged, cardBad });
        header.Controls.AddRange(new Control[] { cards, logo, title, tag, lblFile });
        void LayoutHeader()
        {
            tag.Location = new Point(title.Right + 10, title.Top + 9);
            cards.Location = new Point(header.Width - cards.Width - 16, 15);
            lblFile.AutoSize = false;
            lblFile.Size = new Size(Math.Max(100, cards.Left - lblFile.Left - 16), 22);
        }
        header.Resize += (_, _) => LayoutHeader();
        header.HandleCreated += (_, _) => LayoutHeader();
        cardForeign.Click += (_, _) => { chkChanged.Checked = false; chkForeign.Checked = !chkForeign.Checked; };
        cardChanged.Click += (_, _) => chkChanged.Checked = !chkChanged.Checked;

        // ---------------------------------------------------------- grid
        var dark = Theme.Bg;
        grid.BackgroundColor = dark;
        grid.GridColor = Theme.Mix(Theme.Bg, Theme.Border, 0.6f);
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = dark, ForeColor = Theme.Text, Font = Theme.Ui, Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Theme.Mix(dark, Theme.Accent, 0.32f), SelectionForeColor = Color.White,
            WrapMode = DataGridViewTriState.False,
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface, ForeColor = Theme.TextDim, Font = Theme.Small, Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Theme.Surface, SelectionForeColor = Theme.TextDim, Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        grid.RowTemplate.Height = 32;

        colType = AddCol("Type", "TYPE", 70);
        var fid = AddCol("FormID", "FORM ID", 92);
        fid.DefaultCellStyle.Font = Theme.Mono;
        fid.DefaultCellStyle.ForeColor = Theme.TextDim;
        var edid = AddCol("EditorID", "EDITOR ID", 200);
        edid.DefaultCellStyle.ForeColor = Theme.TextDim;
        var fld = AddCol("Field", "FIELD", 64);
        fld.DefaultCellStyle.ForeColor = Theme.TextDim;
        var orig = AddCol("Original", "ORIGINAL (DECODED)", 300);
        orig.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        orig.FillWeight = 100;
        orig.MinimumWidth = 160;
        var en = AddCol("English", "ENGLISH   ·   double-click / F2 to edit", 340);
        en.ReadOnly = false;
        en.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        en.FillWeight = 115;
        en.MinimumWidth = 180;
        en.DefaultCellStyle.Font = Theme.UiBold;
        colEnglish = en;
        colStatus = AddCol("Status", "STATUS", 160);
        bs.DataSource = view;
        grid.DataSource = bs;
        grid.CellFormatting += Grid_CellFormatting;
        grid.CellPainting += Grid_CellPainting;
        grid.CellEndEdit += (_, _) => { grid.InvalidateRow(grid.CurrentCell?.RowIndex ?? 0); UpdateStatus(); };
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != en.Index) { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[en.Index]; grid.BeginEdit(true); }
        };
        grid.EditingControlShowing += (_, e) =>
        {
            e.Control.BackColor = Theme.Surface2;
            e.Control.ForeColor = Color.White;
        };
        grid.HandleCreated += (_, _) => Theme.DarkScroll(grid);
        grid.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && grid.CurrentCell?.ColumnIndex != colEnglish.Index && EditEnglish(e.KeyChar)) e.Handled = true;
        };

        var menu = new ContextMenuStrip { Renderer = renderer, ShowImageMargin = true };
        menu.Items.Add("Auto EN", Theme.Glyph('', Theme.Text), (_, _) => _ = TranslateAsync(Selected()));
        menu.Items.Add("Translate again (ignore cache)", Theme.Glyph('', Theme.Text), (_, _) => _ = TranslateAsync(Selected(), again: true))
            .ToolTipText = "Ask the engine again for the selected rows, even if a result is in translation_cache.txt (fixes wrong cached results). glossary.txt still wins.";
        menu.Items.Add("Name this set…", Theme.Glyph('', Theme.Text), (_, _) => NameSets())
            .ToolTipText = "Give the set name before \" - \" an English name (suggested: in English letters, 요대선몽 → Yodaeseonmong). " +
                           "Saved in glossary.txt; every row of the set is translated again.";
        menu.Items.Add("Original → English", Theme.Glyph('', Theme.Text), (_, _) => ForSelected(e => e.English = e.Original));
        menu.Items.Add("Revert", Theme.Glyph('', Theme.Text), (_, _) => ForSelected(e => e.English = e.IsForeign ? "" : e.Original));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy original", null, (_, _) => CopyText(e => e.Original));
        menu.Items.Add("Copy English", null, (_, _) => CopyText(e => e.English));
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Theme.Text;
        grid.ContextMenuStrip = menu;

        // the grid sits in a 1px-border frame with a margin around it
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(16, 10, 16, 14) };
        frame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1), Visible = false };
        frame.Controls.Add(grid);
        content.Controls.Add(frame);
        content.Controls.Add(drop);
        drop.Click += (_, _) => OpenDialog();

        var strip = new StatusStrip { Renderer = renderer, BackColor = Theme.Surface, SizingGrip = false, Padding = new Padding(12, 3, 12, 3) };
        status.ForeColor = Theme.TextDim;
        engineLabel.Click += (_, _) => OpenSettings(settings.Engine);
        engineLabel.ToolTipText = "Engine used by Auto EN. Click to set it up.";
        strip.Items.Add(status);
        strip.Items.Add(engineLabel);
        strip.Items.Add(hint);

        Controls.Add(content);
        Controls.Add(busyBar);
        Controls.Add(bar2);
        Controls.Add(bar1);
        Controls.Add(header);
        Controls.Add(strip);

        // smooth scrolling of long lists (DataGridView has no public switch for this)
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(grid, true);

        // search: filter once typing pauses, not on every key
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); ApplyFilter(); };
        txtSearch.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        chkForeign.CheckedChanged += (_, _) => ApplyFilter();
        chkChanged.CheckedChanged += (_, _) => ApplyFilter();
        chkDesc.CheckedChanged += (_, _) =>
        {
            if (settings.IncludeDesc == chkDesc.Checked) return;
            settings.IncludeDesc = chkDesc.Checked;
            settings.Save();
            if (plugin != null) _ = RebuildAsync();
        };
        cboEngine.SelectedIndexChanged += (_, _) =>
        {
            var k = AppSettings.KindByName(cboEngine.SelectedItem as string, settings.Engine);
            if (k == settings.Engine) return;
            settings.Engine = k;
            settings.Save();
            UpdateEngineLabel();
        };

        // drag & drop works on the window, the grid and the empty drop zone
        foreach (Control c in new Control[] { this, grid, drop })
        {
            c.AllowDrop = true;
            c.DragEnter += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; drop.Hot = true; drop.Invalidate(); }
            };
            c.DragLeave += (_, _) => { drop.Hot = false; drop.Invalidate(); };
            c.DragDrop += (_, e) =>
            {
                drop.Hot = false;
                drop.Invalidate();
                if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) BeginInvoke(() => _ = LoadFileAsync(f[0]));
            };
        }
        KeyDown += MainForm_KeyDown;
        FormClosing += MainForm_FormClosing;
        Activated += (_, _) => ReloadGlossaryIfChanged();

        try { tr = new Translator(BaseDir) { S = settings }; glossaryStamp = GlossaryTime(); }
        catch (Exception ex) { MessageBox.Show(this, "Could not read glossary / cache: " + ex.Message, Text); }
        status.Text = "Open a plugin (Open, or drag a .esp onto the window).";
        UpdateCards();
        UpdateEngineLabel();
        if (!string.IsNullOrEmpty(startPath)) Shown += (_, _) => _ = LoadFileAsync(startPath);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    DataGridViewTextBoxColumn AddCol(string prop, string header, int width)
    {
        var c = new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop, HeaderText = header, Width = width, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        grid.Columns.Add(c);
        return c;
    }

    // ---------------------------------------------------------------- load
    void OpenDialog()
    {
        if (busy) return;
        using var d = new OpenFileDialog
        {
            Filter = "Skyrim plugins (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(settings.LastDir) ? settings.LastDir : "",
        };
        if (d.ShowDialog(this) == DialogResult.OK) _ = LoadFileAsync(d.FileName);
    }

    // reading and parsing a big plugin takes seconds: it runs in the background, the window stays usable
    async Task LoadFileAsync(string path)
    {
        if (busy || !await ConfirmDiscardAsync()) return;
        SetBusy(true);
        status.Text = "Opening " + System.IO.Path.GetFileName(path) + " ...";
        try
        {
            string mode = settings.TextMode;
            bool desc = settings.IncludeDesc;
            var (p, entries) = await Task.Run(() =>
            {
                var pl = Plugin.Load(path);
                return (pl, pl.Localized ? null : ReadEntries(pl, mode, desc, null));
            });
            if (p.Localized)
            {
                status.Text = "Not opened: localized plugin.";
                MessageBox.Show(this, "This plugin is LOCALIZED: its names are in the Strings\\*.STRINGS files, not in the plugin.\n" +
                    "ESPLanguage_EN edits names that are stored inside the plugin.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            plugin = p;
            settings.LastDir = System.IO.Path.GetDirectoryName(path);
            settings.Save();
            all.Clear();
            all.AddRange(entries);
            drop.Visible = false;
            frame.Visible = true;
            ApplyFilter();
            grid.Focus();
        }
        catch (Exception ex)
        {
            status.Text = "Could not open the plugin.";
            MessageBox.Show(this, "Cannot read the plugin:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    // after Include DESC / Text in file changed; English typed so far is kept
    async Task RebuildAsync()
    {
        if (plugin == null || busy) return;
        grid.EndEdit();
        SetBusy(true);
        status.Text = "Reading the plugin again ...";
        try
        {
            var keep = all.Where(e => e.Changed).ToDictionary(e => e.Key, e => e.English);
            var p = plugin;
            string mode = settings.TextMode;
            bool desc = settings.IncludeDesc;
            var entries = await Task.Run(() => ReadEntries(p, mode, desc, keep));
            all.Clear();
            all.AddRange(entries);
            ApplyFilter();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    // every FULL (+ DESC) text of the plugin; runs on a worker thread (touches no control)
    static List<TextEntry> ReadEntries(Plugin plugin, string mode, bool desc, Dictionary<string, string> keep)
    {
        var list = new List<TextEntry>();
        foreach (var r in plugin.Records())
        {
            if (r.Type == "TES4") continue;
            List<Sub> subs;
            try { subs = r.Subs; }
            catch { continue; }
            string edid = "";
            var ed = subs.FirstOrDefault(s => s.Type == "EDID");
            if (ed != null) edid = TextCodec.Decode(TextCodec.Trim0(ed.Data), "Auto").Text;
            var count = new Dictionary<string, int>();
            foreach (var s in subs)
            {
                if (s.Type != "FULL" && !(desc && s.Type == "DESC")) continue;
                int n = count.GetValueOrDefault(s.Type) + 1;
                count[s.Type] = n;
                var raw = TextCodec.Trim0(s.Data);
                if (raw.Length == 0) continue;
                var (text, enc) = TextCodec.Decode(raw, mode);
                string field = n == 1 ? s.Type : s.Type + "#" + n;
                var e = new TextEntry
                {
                    Rec = r, Sub = s, Type = r.Type, FormID = r.FormId.ToString("X8"), EditorID = edid,
                    Field = field, Original = text, Enc = enc,
                };
                e.Key = e.FormID + "|" + field;
                e.English = keep != null && keep.TryGetValue(e.Key, out var k) ? k : (e.IsForeign ? "" : text);
                list.Add(e);
            }
        }
        return list;
    }

    void ApplyFilter()
    {
        string q = txtSearch.Text.Trim();
        view.RaiseListChangedEvents = false;
        view.Clear();
        foreach (var e in all)
        {
            if (chkForeign.Checked && !e.IsForeign && !e.Changed) continue;
            if (chkChanged.Checked && !e.Changed) continue;
            if (q.Length > 0 && !Has(e.Original, q) && !Has(e.English, q) && !Has(e.EditorID, q) && !Has(e.FormID, q) && !Has(e.Type, q))
                continue;
            view.Add(e);
        }
        view.RaiseListChangedEvents = true;
        bs.ResetBindings(false);
        UpdateStatus();
    }

    static bool Has(string s, string q) => s != null && s.Contains(q, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- grid
    void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
        var t = view[e.RowIndex];
        var c = StateColor(t);
        if (c == Color.Empty) return;
        e.CellStyle.BackColor = Theme.Mix(Theme.Bg, c, 0.07f);
        e.CellStyle.SelectionBackColor = Theme.Mix(Theme.Mix(Theme.Bg, Theme.Accent, 0.30f), c, 0.10f);
    }

    static Color StateColor(TextEntry t) =>
        !t.Valid ? Theme.Invalid : t.Changed ? Theme.Changed : t.IsForeign ? Theme.Foreign : Color.Empty;

    // colored stripe at the left of each row + a pill badge in the Status column
    void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
        var t = view[e.RowIndex];
        var c = StateColor(t);
        if (e.ColumnIndex == colType.Index && c != Color.Empty)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            using var b = new SolidBrush(c);
            e.Graphics.FillRectangle(b, e.CellBounds.X, e.CellBounds.Y + 1, 3, e.CellBounds.Height - 2);
            e.Handled = true;
        }
        else if (e.ColumnIndex == colEnglish.Index && string.IsNullOrEmpty(t.English)
                 && !(grid.IsCurrentCellInEditMode && grid.CurrentCell?.RowIndex == e.RowIndex && grid.CurrentCell.ColumnIndex == e.ColumnIndex))
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border);
            var r = e.CellBounds;
            TextRenderer.DrawText(e.Graphics, "not translated yet", Theme.Ui, new Rectangle(r.X + 9, r.Y, r.Width - 12, r.Height), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }
        else if (e.ColumnIndex == colStatus.Index)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border);
            string s = t.Status;
            if (s.Length > 0 && c != Color.Empty)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (!pillWidth.TryGetValue(s, out int tw))
                    pillWidth[s] = tw = TextRenderer.MeasureText(g, s, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
                int w = Math.Min(tw + 22, e.CellBounds.Width - 12), h = 20;
                var r = new RectangleF(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - h) / 2f, w, h);
                Theme.FillRound(g, Theme.Mix(Theme.Bg, c, 0.22f), r, h / 2f);
                using (var dot = new SolidBrush(c)) g.FillEllipse(dot, r.X + 8, r.Y + h / 2f - 3, 6, 6);
                TextRenderer.DrawText(g, s, Theme.Small, Rectangle.Round(new RectangleF(r.X + 17, r.Y, r.Width - 19, h)), Theme.Mix(c, Color.White, 0.35f),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            e.Handled = true;
        }
    }

    List<TextEntry> Selected()
    {
        var rows = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).ToList();
        if (rows.Count == 0 && grid.CurrentCell != null) rows.Add(grid.CurrentCell.RowIndex);
        return rows.Where(i => i >= 0 && i < view.Count).OrderBy(i => i).Select(i => view[i]).ToList();
    }

    void ForSelected(Action<TextEntry> a)
    {
        grid.EndEdit();
        foreach (var e in Selected()) a(e);
        grid.Refresh();
        UpdateStatus();
    }

    void CopyText(Func<TextEntry, string> f)
    {
        var s = string.Join("\r\n", Selected().Select(f));
        if (s.Length > 0) Clipboard.SetText(s);
    }

    int nForeign, nChanged, nBad;

    void Count()
    {
        nForeign = nChanged = nBad = 0;
        foreach (var e in all)
        {
            bool ch = e.Changed;
            if (ch) nChanged++;
            else if (e.IsForeign) nForeign++;
            if (!e.Valid) nBad++;
        }
    }

    void UpdateStatus()
    {
        if (plugin == null) return;
        Count();
        status.Text = $"Showing {view.Count:N0} of {all.Count:N0} texts" +
                      (nBad > 0 ? $"   ·   {nBad} with characters an English game cannot show (red)" : "");
        UpdateTitle();
        UpdateCards();
    }

    void UpdateCards()
    {
        bool p = plugin != null;
        cardAll.Set(p ? all.Count.ToString("N0") : "–", false);
        cardForeign.Set(p ? nForeign.ToString("N0") : "–", chkForeign.Checked && !chkChanged.Checked && p);
        cardChanged.Set(p ? nChanged.ToString("N0") : "–", chkChanged.Checked && p);
        cardBad.Set(p ? nBad.ToString("N0") : "–", p && nBad > 0);
    }

    void UpdateEngineLabel()
    {
        string text = settings.EngineSummary(out bool ready);
        engineLabel.Text = (ready ? "● " : "○ ") + text;
        engineLabel.ForeColor = ready ? Theme.Changed : Theme.Foreign;
        if (AppSettings.NameOf(settings.Engine) != cboEngine.SelectedItem as string) cboEngine.SelectedItem = AppSettings.NameOf(settings.Engine);
    }

    void UpdateTitle()
    {
        bool dirty = plugin != null && nChanged > 0;
        string name = plugin == null ? "" : " - " + System.IO.Path.GetFileName(plugin.Path) + (dirty ? " *" : "");
        Text = "ESPLanguage_EN" + name;
        lblFile.Text = plugin == null ? "No plugin loaded  —  drop a .esp, .esm or .esl below"
            : (dirty ? "●  " : "") + plugin.Path;
        lblFile.ForeColor = dirty ? Theme.Foreign : Theme.TextDim;
    }

    void MainForm_KeyDown(object sender, KeyEventArgs e)
    {
        if (grid.IsCurrentCellInEditMode) return;
        if (e.Control && e.KeyCode == Keys.S) { _ = SaveAsync(plugin?.Path); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.O) { OpenDialog(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.F) { txtSearch.Focus(); e.Handled = true; }
        else if (e.KeyCode == Keys.F5) { _ = TranslateAsync(Selected()); e.Handled = true; }
        else if (e.KeyCode == Keys.F2 && grid.Focused && EditEnglish()) e.Handled = true;
    }

    // F2 / typing on any cell of a row edits that row's English text
    bool EditEnglish(char? first = null)
    {
        if (grid.CurrentCell == null || grid.IsCurrentCellInEditMode) return false;
        int row = grid.CurrentCell.RowIndex;
        if (row < 0 || row >= grid.RowCount) return false;
        grid.CurrentCell = grid.Rows[row].Cells[colEnglish.Index];
        if (!grid.BeginEdit(first == null)) return false;
        if (first != null && grid.EditingControl is TextBox tb)
        {
            tb.Text = first.Value.ToString();
            tb.SelectionStart = tb.TextLength;
        }
        return true;
    }

    void SetBusy(bool b)
    {
        busy = b;
        foreach (var i in busyItems) i.Enabled = !b;
        busyBar.Running = b;
        UseWaitCursor = b;
    }

    // ---------------------------------------------------------------- translate
    async Task TranslateAsync(List<TextEntry> list, bool again = false)
    {
        if (busy || plugin == null || tr == null) return;
        grid.EndEdit();
        list = list.Where(e => e.IsForeign).ToList();
        if (list.Count == 0)
        {
            status.Text = "Nothing to translate: select rows that are not English (amber).";
            return;
        }
        settings.EngineSummary(out bool ready);
        if (!ready)
        {
            if (MessageBox.Show(this, AppSettings.NameOf(settings.Engine) + " is not set up yet (no API key).\n\nOpen its settings now?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenSettings(settings.Engine);
            return;
        }
        tr.S = settings;
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(s => status.Text = s);
            var (res, error) = await tr.TranslateAsync(list.Select(e => e.Original).ToList(), settings.Lang, settings.TitleCase, progress, ignoreCache: again);
            int done = 0;
            for (int i = 0; i < list.Count; i++)
                if (!string.IsNullOrEmpty(res[i])) { list[i].English = res[i]; done++; }
            grid.Refresh();
            UpdateStatus();
            status.Text = $"Auto EN ({AppSettings.NameOf(settings.Engine)}): {done} of {list.Count} translated. " + status.Text;
            if (error != null)
                MessageBox.Show(this, "Translation stopped: " + error + "\n\nTexts translated so far are kept.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Translation failed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------------------------------------------------------- set names
    // the set names of the selected rows -> one editable line each (suggestion: Latin letters, or the glossary entry)
    void NameSets()
    {
        if (busy || plugin == null || tr == null) return;
        grid.EndEdit();
        var sets = Selected().Select(e => Translator.SetNameOf(e.Original)).Where(x => x != null).Distinct().ToList();
        if (sets.Count == 0)
        {
            MessageBox.Show(this, "The selected rows have no set name: it is the part before \" - \", as in \"요대선몽 - 소매\".", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new Form
        {
            Text = "Name this set", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font, BackColor = Theme.Bg, ForeColor = Theme.Text,
            ClientSize = new Size(620, 150 + sets.Count * 40),
        };
        f.HandleCreated += (_, _) => Theme.DarkTitleBar(f);
        var intro = new Label
        {
            Left = 16, Top = 14, Width = 588, Height = 52, ForeColor = Theme.TextDim,
            Text = "An invented set name has no meaning to translate. Keep the suggestion (the name in English letters) or type your own English name. " +
                   "It goes into glossary.txt, so every piece of the set, now and in other plugins, gets the same name.",
        };
        f.Controls.Add(intro);
        var boxes = new List<(string Set, TextBox Box)>();
        int y = 74;
        foreach (var set in sets)
        {
            string suggestion = tr.Glossary.TryGetValue(set, out var g) ? g : Romanizer.HasHangul(set) ? Romanizer.Name(set) : "";
            f.Controls.Add(new Label { Text = set, Left = 16, Top = y + 4, Width = 190, AutoEllipsis = true, ForeColor = Theme.Text });
            var box = new TextBox { Text = suggestion, Left = 216, Top = y, Width = 388, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Text };
            f.Controls.Add(box);
            boxes.Add((set, box));
            y += 40;
        }
        var ok = new Button { Text = "Save and translate", Width = 160, Height = 32, Left = 336, Top = y + 14, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 32, Left = 504, Top = y + 14, DialogResult = DialogResult.Cancel };
        foreach (var b in new[] { ok, cancel })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = b == ok ? Theme.Accent : Theme.Surface2;
            b.ForeColor = b == ok ? Color.White : Theme.Text;
            b.FlatAppearance.BorderColor = b == ok ? Theme.Accent : Theme.Border;
        }
        f.Controls.AddRange(new Control[] { ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;

        var named = new List<string>();
        try
        {
            foreach (var (set, box) in boxes)
            {
                string en = box.Text.Trim();
                if (en.Length == 0) continue;
                if (!TextCodec.CanWrite(en))
                {
                    MessageBox.Show(this, $"\"{en}\" has characters an English game cannot show. Use English letters.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                tr.SetGlossary(set, en);
                named.Add(set);
            }
            glossaryStamp = GlossaryTime();
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not write glossary.txt: " + ex.Message, Text); return; }
        if (named.Count == 0) return;
        // every row of these sets, not only the selected ones
        var rows = all.Where(e => e.IsForeign && named.Contains(Translator.SetNameOf(e.Original))).ToList();
        status.Text = $"Named {named.Count} set(s) in glossary.txt; translating {rows.Count} row(s) ...";
        _ = TranslateAsync(rows);
    }

    // ---------------------------------------------------------------- glossary
    // Notepad opens on its own; the window is not blocked. The glossary is reloaded when you come back.
    void OpenGlossary()
    {
        if (tr == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "\"" + tr.GlossaryPath + "\"") { UseShellExecute = true });
            status.Text = "Glossary opened in Notepad. Save it there; it is reloaded when you come back to this window.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }

    DateTime GlossaryTime()
    {
        try { return tr == null ? default : File.GetLastWriteTimeUtc(tr.GlossaryPath); }
        catch { return default; }
    }

    void ReloadGlossaryIfChanged()
    {
        if (tr == null) return;
        var t = GlossaryTime();
        if (t == glossaryStamp) return;
        glossaryStamp = t;
        try
        {
            tr.ReloadGlossary();
            status.Text = $"Glossary reloaded: {tr.Glossary.Count} words.";
        }
        catch (Exception ex) { status.Text = "Glossary not reloaded: " + ex.Message; }
    }

    // ---------------------------------------------------------------- settings
    void OpenSettings(EngineKind? page = null)
    {
        if (busy) return;
        using var f = new SettingsForm(settings, page);
        if (f.ShowDialog(this) != DialogResult.OK || f.Result == null) return;
        var old = settings;
        settings = f.Result;
        settings.Save();
        if (tr != null) tr.S = settings;
        chkDesc.Checked = settings.IncludeDesc;
        UpdateEngineLabel();
        status.Text = "Settings saved.";
        if (plugin != null && (old.TextMode != settings.TextMode || old.IncludeDesc != settings.IncludeDesc)) _ = RebuildAsync();
    }

    // ---------------------------------------------------------------- save
    void SaveAs()
    {
        if (plugin == null || busy) return;
        using var d = new SaveFileDialog
        {
            Filter = "Skyrim plugins (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl|All files (*.*)|*.*",
            FileName = System.IO.Path.GetFileName(plugin.Path),
            InitialDirectory = System.IO.Path.GetDirectoryName(plugin.Path),
        };
        if (d.ShowDialog(this) == DialogResult.OK) _ = SaveAsync(d.FileName);
    }

    // building a 17 MB plugin and writing it (plus the backup) runs in the background
    async Task<bool> SaveAsync(string target)
    {
        if (plugin == null || busy || string.IsNullOrEmpty(target)) return false;
        grid.EndEdit();
        var changed = all.Where(e => e.Changed).ToList();
        var bad = changed.Where(e => !e.Valid).ToList();
        if (changed.Count == 0 && target == plugin.Path)
        {
            status.Text = "Nothing to save.";
            return true;
        }
        if (bad.Count > 0 && MessageBox.Show(this, $"{bad.Count} text(s) contain characters an English game cannot show (red rows).\n" +
                "They are NOT saved. Continue with the others?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return false;

        var good = changed.Where(e => e.Valid).ToList();
        var oldData = good.Select(e => e.Sub.Data).ToList();
        foreach (var e in good) { e.Sub.Data = TextCodec.Encode(e.English); e.Rec.Dirty = true; }
        SetBusy(true);
        status.Text = "Saving " + System.IO.Path.GetFileName(target) + " ...";
        string backup = null;
        try
        {
            var p = plugin;
            backup = await Task.Run(() =>
            {
                var bytes = p.ToBytes();
                string bk = null;
                if (File.Exists(target))
                {
                    string bdir = System.IO.Path.Combine(BaseDir, "backups");
                    Directory.CreateDirectory(bdir);
                    bk = System.IO.Path.Combine(bdir, System.IO.Path.GetFileNameWithoutExtension(target) + "_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") + System.IO.Path.GetExtension(target));
                    File.Copy(target, bk, true);
                }
                File.WriteAllBytes(target, bytes);       // in place: under MO2 the file stays in its mod folder
                return bk;
            });
        }
        catch (Exception ex)
        {
            for (int i = 0; i < good.Count; i++) { good[i].Sub.Data = oldData[i]; good[i].Rec.Dirty = true; }
            SetBusy(false);
            status.Text = "Save failed.";
            MessageBox.Show(this, "Save failed:\n" + ex.Message + "\n\nIs the plugin open in xEdit or the game?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        SetBusy(false);
        foreach (var e in good)
        {
            e.Original = e.English;
            e.Enc = e.English.All(c => c < 0x80) ? "ASCII" : "1252";
        }
        plugin.Path = target;
        settings.LastDir = System.IO.Path.GetDirectoryName(target);
        settings.Save();
        ApplyFilter();
        status.Text = $"Saved {good.Count} text(s) to {System.IO.Path.GetFileName(target)}" +
                      (backup != null ? $"  (backup: backups\\{System.IO.Path.GetFileName(backup)})" : "") + ".  " + status.Text;
        return true;
    }

    async Task<bool> ConfirmDiscardAsync()
    {
        if (plugin == null || !all.Any(e => e.Changed)) return true;
        var r = MessageBox.Show(this, "Save the changes to " + System.IO.Path.GetFileName(plugin.Path) + "?", Text,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return await SaveAsync(plugin.Path);
        return true;
    }

    void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (closeAfterSave) { settings.Save(); return; }
        if (busy) { e.Cancel = true; status.Text = "Please wait: still working."; return; }
        if (plugin == null || !all.Any(x => x.Changed)) { settings.Save(); return; }
        e.Cancel = true;                                   // ask, save in the background, then close
        BeginInvoke(async () =>
        {
            if (!await ConfirmDiscardAsync()) return;
            closeAfterSave = true;
            Close();
        });
    }

    // ---------------------------------------------------------------- TSV
    static string Tsv(string s) => (s ?? "").Replace("\t", " ").Replace("\r", "").Replace("\n", "\\n");
    static string UnTsv(string s) => s.Replace("\\n", "\n");

    void ExportTsv()
    {
        if (plugin == null) return;
        using var d = new SaveFileDialog
        {
            Filter = "Tab-separated text (*.tsv)|*.tsv|Text (*.txt)|*.txt",
            FileName = System.IO.Path.GetFileNameWithoutExtension(plugin.Path) + "_EN.tsv",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var rows = view.Count > 0 ? view.ToList() : all;
        var sb = new StringBuilder("FormID\tField\tType\tEditorID\tOriginal\tEnglish\r\n");
        foreach (var e in rows)
            sb.Append(e.FormID).Append('\t').Append(e.Field).Append('\t').Append(e.Type).Append('\t').Append(Tsv(e.EditorID))
              .Append('\t').Append(Tsv(e.Original)).Append('\t').Append(Tsv(e.English)).Append("\r\n");
        File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
        status.Text = $"Exported {rows.Count} rows (the rows shown now) to {System.IO.Path.GetFileName(d.FileName)}.";
    }

    void ImportTsv()
    {
        if (plugin == null) return;
        using var d = new OpenFileDialog { Filter = "Tab-separated text (*.tsv;*.txt)|*.tsv;*.txt|All files (*.*)|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        grid.EndEdit();
        var byKey = all.ToDictionary(e => e.Key);
        int n = 0, miss = 0;
        foreach (var line in File.ReadAllLines(d.FileName, Encoding.UTF8))
        {
            var c = line.Split('\t');
            if (c.Length < 6 || c[0] == "FormID") continue;
            if (!byKey.TryGetValue(c[0].Trim().ToUpperInvariant() + "|" + c[1].Trim(), out var e)) { miss++; continue; }
            string en = UnTsv(c[5]).Trim();
            if (en.Length == 0 || en == e.English) continue;
            e.English = en;
            n++;
        }
        ApplyFilter();
        status.Text = $"Imported {n} English text(s)" + (miss > 0 ? $", {miss} line(s) not found in this plugin" : "") + ".  " + status.Text;
    }
}
