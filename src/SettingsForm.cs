using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ESPLanguageEN;

// Settings: General + one page per translation engine. Every engine page has its own fields only,
// a Test button (one sample through that engine with the values being edited) and "Use this engine".
public sealed class SettingsForm : Form
{
    public AppSettings Result { get; private set; }
    readonly AppSettings s;
    readonly ListBox nav = new()
    {
        Dock = DockStyle.Left, Width = 210, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 38, IntegralHeight = false, BackColor = Theme.Surface, ForeColor = Theme.Text,
    };
    readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(28, 20, 28, 12) };
    readonly List<(string Title, EngineKind? Engine, Panel Page)> pages = new();
    readonly ComboBox cboEngine = Combo(260);

    const int W = 540;                                   // page content width (fits without a horizontal scrollbar)
    const string Sample = "벚꽃 밤의 환상 가발";

    static string NavTitle(EngineKind k) => k switch
    {
        EngineKind.LocalAI => "Local AI",
        EngineKind.Nllb => "NLLB-200",
        _ => AppSettings.NameOf(k),
    };

    public SettingsForm(AppSettings current, EngineKind? startEngine = null)
    {
        s = current.Clone();
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(880, 660);
        Font = Theme.Ui;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        pages.Add(("General", null, GeneralPage()));
        foreach (var (kind, _, _) in AppSettings.EngineList) pages.Add((NavTitle(kind), kind, EnginePage(kind)));

        foreach (var p in pages) { nav.Items.Add(p.Title); host.Controls.Add(p.Page); }
        nav.DrawItem += DrawNav;
        nav.SelectedIndexChanged += (_, _) => { for (int i = 0; i < pages.Count; i++) pages[i].Page.Visible = i == nav.SelectedIndex; };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Surface };
        var save = Button("Save", 100, accent: true);
        var cancel = Button("Cancel", 100);
        save.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        var foot = new Label { Text = "Saved in ESPLanguage_EN.ini next to the exe.", AutoSize = true, ForeColor = Theme.TextFaint, Location = new Point(16, 19) };
        bottom.Controls.AddRange(new Control[] { foot, save, cancel });
        bottom.Resize += (_, _) => { cancel.Location = new Point(bottom.Width - 116, 12); save.Location = new Point(bottom.Width - 224, 12); };
        AcceptButton = save;
        CancelButton = cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK) { Collect(); Result = s; } };

        Controls.Add(host);
        Controls.Add(nav);
        Controls.Add(bottom);

        int start = startEngine == null ? 0 : Math.Max(0, pages.FindIndex(p => p.Engine == startEngine));
        nav.SelectedIndex = start;
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);
        Shown += (_, _) => { foreach (var p in pages) Theme.DarkScroll(p.Page); };
    }

    // ------------------------------------------------------------ controls
    static ComboBox Combo(int width)
    {
        var c = new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
        Theme.DarkComboBox(c);
        return c;
    }

    static TextBox Box(string text, int width, bool secret = false) => new()
    {
        Text = text, Width = width, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Text,
        UseSystemPasswordChar = secret,
    };

    static Button Button(string text, int width, bool accent = false)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = accent ? Theme.Accent : Theme.Surface2, ForeColor = accent ? Color.White : Theme.Text,
        };
        b.FlatAppearance.BorderColor = accent ? Theme.Accent : Theme.Border;
        b.FlatAppearance.MouseOverBackColor = accent ? Theme.AccentHover : Theme.Hover;
        return b;
    }

    static Label Caption(string text) => new() { Text = text, AutoSize = true, ForeColor = Theme.TextDim };

    static Label Note(string text, int height) => new() { Text = text, Width = W, Height = height, ForeColor = Theme.TextDim };

    static LinkLabel Link(string text, string url)
    {
        var l = new LinkLabel
        {
            Text = text, AutoSize = true, LinkColor = Theme.AccentText, ActiveLinkColor = Color.White, VisitedLinkColor = Theme.AccentText,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        l.LinkClicked += (_, _) => Open(url);
        return l;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ESPLanguage_EN"); }
    }

    // vertical layout helper for one page
    sealed class Flow
    {
        public readonly Panel Page = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false, AutoScroll = true };
        int y;
        public Flow(string title, string subtitle)
        {
            Page.Controls.Add(new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0) });
            y = 40;
            if (subtitle != null) Add(new Label { Text = subtitle, ForeColor = Theme.AccentText, AutoSize = true }, 22);
            y += 6;
        }
        public T Add<T>(T c, int gap = 12, int x = 0) where T : Control
        {
            c.Location = new Point(x, y);
            Page.Controls.Add(c);
            y += c.Height + gap;
            return c;
        }
        // caption on the left, control(s) on the right, one row
        public void Row(string caption, params Control[] cs)
        {
            var cap = Caption(caption);
            cap.Location = new Point(0, y + 5);
            Page.Controls.Add(cap);
            int x = 130, h = 0;
            foreach (var c in cs)
            {
                c.Location = new Point(x, y);
                Page.Controls.Add(c);
                x += c.Width + 8;
                h = Math.Max(h, c.Height);
            }
            y += h + 12;
        }
        public void Space(int px) => y += px;
        public void Section(string text)
        {
            y += 6;
            Add(new Label { Text = text.ToUpperInvariant(), Font = Theme.Small, ForeColor = Theme.TextFaint, AutoSize = true }, 2);
            Add(new Panel { Height = 1, Width = W, BackColor = Theme.Border }, 12);
        }
    }

    // ------------------------------------------------------------ pages
    readonly Dictionary<EngineKind, Action> collectors = new();
    ComboBox cboLang, cboText;
    CheckBox chkTitle, chkDesc;

    Panel GeneralPage()
    {
        var f = new Flow("General", "How Auto EN works for every engine");
        foreach (var e in AppSettings.EngineList) cboEngine.Items.Add(e.Name);
        cboEngine.SelectedItem = AppSettings.NameOf(s.Engine);
        cboEngine.SelectedIndexChanged += (_, _) => nav.Invalidate();
        f.Row("Engine", cboEngine);
        f.Add(Note("Auto EN uses this engine. Set it up on its page (left) and press Test there. It can also be switched from the toolbar.", 36));

        f.Section("Translation");
        cboLang = Combo(200);
        foreach (var l in AppSettings.Langs) cboLang.Items.Add(l.Name);
        cboLang.SelectedIndex = Math.Max(0, Array.FindIndex(AppSettings.Langs, l => l.Code == s.Lang));
        f.Row("Translate from", cboLang);
        chkTitle = new CheckBox { Text = "Title Case  (\"Cherry Blossom Wig\" instead of \"cherry blossom wig\")", Checked = s.TitleCase, AutoSize = true, ForeColor = Theme.Text };
        f.Row("", chkTitle);

        f.Section("Reading the plugin");
        cboText = Combo(200);
        cboText.Items.AddRange(TextCodec.Modes);
        cboText.SelectedItem = s.TextMode;
        if (cboText.SelectedIndex < 0) cboText.SelectedIndex = 0;
        f.Row("Text in file", cboText);
        f.Add(Note("How the bytes of a name are decoded. Auto (UTF-8, then Korean 949 / Chinese 936 / Big5 / Japanese 932) is right for almost every plugin; change it only if Original still looks garbled.", 54));
        chkDesc = new CheckBox { Text = "Also list descriptions (DESC): book text, item descriptions", Checked = s.IncludeDesc, AutoSize = true, ForeColor = Theme.Text };
        f.Row("", chkDesc);

        f.Section("Files next to the exe");
        string dir = AppContext.BaseDirectory;
        var g = Button("glossary.txt", 130);
        g.Click += (_, _) => Open(Path.Combine(dir, Translator.GlossaryFile));
        var c = Button("Translation cache", 150);
        c.Click += (_, _) => { var p = Path.Combine(dir, Translator.CacheFile); if (File.Exists(p)) Open(p); else MessageBox.Show(this, "No cache yet: it is written after the first Auto EN.", Text); };
        var b = Button("Backups", 100);
        b.Click += (_, _) => { var p = Path.Combine(dir, "backups"); Directory.CreateDirectory(p); Open(p); };
        f.Row("Open", g, c, b);
        return f.Page;
    }

    Panel EnginePage(EngineKind kind)
    {
        Flow f;
        switch (kind)
        {
            case EngineKind.Claude:
            {
                f = new Flow("Claude", "Anthropic API  ·  best quality for game names  ·  paid per use");
                var key = Box(s.ClaudeKey, 320, true);
                f.Row("API key", key, ShowToggle(key));
                var model = Combo(260);
                model.DropDownStyle = ComboBoxStyle.DropDown;
                model.Items.AddRange(ClaudeEngine.Models);
                model.Text = s.ClaudeModel;
                f.Row("Model", model);
                f.Add(Note("claude-opus-5-5 gives the best names. claude-haiku-5-5 costs much less and is enough for most item names.", 36));
                f.Add(Link("Get an API key: console.anthropic.com → API keys", "https://console.anthropic.com/settings/keys"));
                collectors[kind] = () => { s.ClaudeKey = key.Text.Trim(); s.ClaudeModel = string.IsNullOrWhiteSpace(model.Text) ? ClaudeEngine.DefaultModel : model.Text.Trim(); };
                break;
            }
            case EngineKind.LocalAI:
            {
                f = new Flow("Local AI", "Ollama / LM Studio / llama.cpp on your PC  ·  offline, free");
                var url = Box(s.LocalUrl, 320);
                var ollama = Button("Ollama", 80);
                var lms = Button("LM Studio", 96);
                ollama.Click += (_, _) => url.Text = AppSettings.DefaultLocalUrl;
                lms.Click += (_, _) => url.Text = "http://localhost:1234/v1";
                f.Row("Server URL", url);
                f.Row("Preset", ollama, lms);
                var model = Combo(260);
                model.DropDownStyle = ComboBoxStyle.DropDown;
                model.Text = s.LocalModel;
                var find = Button("Find models", 120);
                f.Row("Model", model, find);
                var info = f.Add(Note("", 22));
                find.Click += async (_, _) =>
                {
                    find.Enabled = false;
                    Status(info, null, "Asking " + url.Text.Trim() + " ...");
                    try
                    {
                        var names = await Translator.LocalModels(url.Text);
                        model.Items.Clear();
                        model.Items.AddRange(names.ToArray());
                        if (names.Count > 0 && !names.Contains(model.Text)) model.Text = names[0];
                        Status(info, true, names.Count > 0 ? $"Connected: {names.Count} model(s). Pick one above." : "Connected, but no model yet:  ollama pull qwen2.5:7b");
                        if (names.Count > 0) model.DroppedDown = true;
                    }
                    catch (Exception ex) { Status(info, false, "Cannot connect: " + ex.Message); }
                    finally { find.Enabled = true; }
                };
                f.Add(Note("Ollama: install from ollama.com, then in a terminal:  ollama pull qwen2.5:7b\n" +
                           "LM Studio: load a model → Developer → Start server.\n" +
                           "Best for Korean / Chinese / Japanese → English: Qwen 2.5 (7B, or 14B with 16 GB+ VRAM).", 56));
                f.Add(Link("Download Ollama: ollama.com", "https://ollama.com/download"));
                collectors[kind] = () =>
                {
                    s.LocalUrl = string.IsNullOrWhiteSpace(url.Text) ? AppSettings.DefaultLocalUrl : url.Text.Trim();
                    s.LocalModel = string.IsNullOrWhiteSpace(model.Text) ? AppSettings.DefaultLocalModel : model.Text.Trim();
                };
                break;
            }
            case EngineKind.Nllb:
            {
                f = new Flow("NLLB-200", "Meta's translation model on your PC  ·  offline, free, fast");
                var url = Box(s.NllbUrl, 320);
                f.Row("Server URL", url);
                var folder = Box(s.NllbFolder, 300);
                var browse = Button("Browse", 80);
                browse.Click += (_, _) =>
                {
                    using var d = new FolderBrowserDialog { Description = "Folder that contains start_nllb.bat", UseDescriptionForTitle = true, SelectedPath = folder.Text };
                    if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath;
                };
                f.Row("Server folder", folder, browse);
                // server state: checked when the page opens, after Start / Stop, and with Check
                var state = new Label { AutoSize = false, Width = W - 130, Height = 40, ForeColor = Theme.TextDim, Text = "" };
                f.Row("Server", state);
                var start = Button("Start server", 120, accent: true);
                var stop = Button("Stop server", 110);
                var check = Button("Check", 80);
                f.Row("", start, stop, check);

                async Task<bool> Check()
                {
                    try
                    {
                        using var doc = await Translator.NllbHealth(url.Text);
                        var r = doc.RootElement;
                        string model = r.TryGetProperty("model", out var m) ? m.GetString()?.Split('/').Last() : "?";
                        string dev = r.TryGetProperty("device", out var d) ? d.GetString() : "?";
                        int idle = r.TryGetProperty("idle_s", out var i) ? i.GetInt32() / 60 : 0;
                        double limit = r.TryGetProperty("idle_minutes", out var l) ? l.GetDouble() : 0;
                        Status(state, true, $"Running  ·  {model} on {dev}  ·  idle {idle} min" +
                            (limit > 0 ? $"  ·  stops itself after {limit:g} min idle" : "  ·  never stops by itself"));
                        return true;
                    }
                    catch
                    {
                        state.ForeColor = Theme.TextDim;
                        state.Text = "○  Not running  (nothing uses the GPU)";
                        return false;
                    }
                }

                f.Page.VisibleChanged += async (_, _) => { if (f.Page.Visible) await Check(); };
                check.Click += async (_, _) => await Check();

                start.Click += async (_, _) =>
                {
                    if (await Check()) return;                                   // already running: nothing to start
                    string bat = Path.Combine(folder.Text.Trim(), "start_nllb.bat");
                    if (!File.Exists(bat)) { Status(state, false, "start_nllb.bat not found: set Server folder (e.g. E:\\nllb)."); return; }
                    try { Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(bat) }); }
                    catch (Exception ex) { Status(state, false, ex.Message); return; }
                    start.Enabled = false;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        while (sw.Elapsed < TimeSpan.FromMinutes(10) && !IsDisposed)     // first start: minutes; later: seconds
                        {
                            Status(state, null, $"Starting in its own window ... {sw.Elapsed.TotalSeconds:0} s (loading the model)");
                            await Task.Delay(2000);
                            if (IsDisposed) return;
                            if (await Check()) return;
                        }
                        if (!IsDisposed) Status(state, false, "Did not start: look at the server window for the error.");
                    }
                    finally { if (!IsDisposed) start.Enabled = true; }
                };

                stop.Click += async (_, _) =>
                {
                    if (!await Check()) return;                                  // nothing to stop
                    stop.Enabled = false;
                    try
                    {
                        Status(state, null, "Stopping ...");
                        try { await Translator.NllbStop(url.Text); } catch { }
                        for (int n = 0; n < 10 && await Check(); n++) await Task.Delay(500);
                        if (await Check())                                       // did not answer: stop_nllb.bat ends the process
                        {
                            string bat = Path.Combine(folder.Text.Trim(), "stop_nllb.bat");
                            if (File.Exists(bat))
                            {
                                var port = new Uri(url.Text.Trim()).Port.ToString();
                                using var p = Process.Start(new ProcessStartInfo(bat, port + " nopause") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(bat) });
                                await Task.Run(() => p?.WaitForExit(15000));
                            }
                        }
                        if (!await Check()) Status(state, true, "Stopped. The GPU memory is free.");
                        else Status(state, false, "Still running: close its window, or run stop_nllb.bat.");
                    }
                    finally { stop.Enabled = true; }
                };
                f.Add(Note("Install once with tools\\install_nllb.bat (see README), then Start server. The first start loads the model for a few minutes and saves a fast copy; later starts take seconds. Uses about 7 GB of VRAM (3.3B).\n" +
                           "NLLB is literal: add game words it gets wrong to glossary.txt.", 72));
                collectors[kind] = () =>
                {
                    s.NllbUrl = string.IsNullOrWhiteSpace(url.Text) ? AppSettings.DefaultNllbUrl : url.Text.Trim();
                    s.NllbFolder = folder.Text.Trim();
                };
                break;
            }
            case EngineKind.DeepL:
            {
                f = new Flow("DeepL", "DeepL API  ·  free up to 500,000 characters / month");
                var key = Box(s.DeepLKey, 320, true);
                f.Row("API key", key, ShowToggle(key));
                f.Add(Note("Sign up for \"DeepL API Free\" (not DeepL Pro for documents). A free key ends with :fx.", 22));
                f.Add(Link("Get a key: deepl.com → API", "https://www.deepl.com/pro-api"));
                collectors[kind] = () => s.DeepLKey = key.Text.Trim();
                break;
            }
            case EngineKind.GoogleCloud:
            {
                f = new Flow("Google Cloud", "Cloud Translation API  ·  paid after the free tier");
                var key = Box(s.GoogleKey, 320, true);
                f.Row("API key", key, ShowToggle(key));
                f.Add(Note("Enable \"Cloud Translation API\" in a Google Cloud project, then Credentials → Create API key (starts with AIza).", 36));
                f.Add(Link("console.cloud.google.com", "https://console.cloud.google.com/apis/library/translate.googleapis.com"));
                collectors[kind] = () => s.GoogleKey = key.Text.Trim();
                break;
            }
            default:
            {
                f = new Flow("Google (free)", "No key  ·  often refused");
                f.Add(Note("Uses Google's free web endpoint. Nothing to set up, but Google often refuses automated requests (error 429) after a few tries. " +
                           "Fine for a quick test; use another engine for whole plugins.", 56));
                collectors[kind] = () => { };
                break;
            }
        }

        f.Space(8);
        var test = Button("Test", 90, accent: true);
        var use = Button("Use this engine", 140);
        var result = new Label { AutoSize = false, Width = W - 380, Height = 64, ForeColor = Theme.TextDim, Text = $"Test translates \"{Sample}\"." };
        f.Row("", test, use, result);
        use.Click += (_, _) => { cboEngine.SelectedItem = AppSettings.NameOf(kind); Status(result, true, "Auto EN will use " + AppSettings.NameOf(kind) + "."); };
        test.Click += async (_, _) =>
        {
            Collect();
            var t = s.Clone();
            t.Engine = kind;
            test.Enabled = false;
            Status(result, null, "Testing ...");
            var sw = Stopwatch.StartNew();
            try
            {
                string en = await Translator.TestAsync(t, Sample);
                Status(result, true, $"OK in {sw.Elapsed.TotalSeconds:0.0} s:  {en}");
            }
            catch (Exception ex) { Status(result, false, ex.Message); }
            finally { test.Enabled = true; }
        };
        return f.Page;
    }

    static void Status(Label l, bool? ok, string text)
    {
        l.ForeColor = ok == null ? Theme.TextDim : ok.Value ? Theme.Changed : Theme.Invalid;
        l.Text = ok == null ? text : (ok.Value ? "✓  " : "✗  ") + text;
    }

    static CheckBox ShowToggle(TextBox box)
    {
        var c = new CheckBox { Text = "Show", AutoSize = true, ForeColor = Theme.TextDim, Margin = Padding.Empty };
        c.CheckedChanged += (_, _) => box.UseSystemPasswordChar = !c.Checked;
        return c;
    }

    void Collect()
    {
        s.Engine = AppSettings.KindByName(cboEngine.SelectedItem as string, s.Engine);
        s.Lang = AppSettings.Langs[Math.Max(0, cboLang.SelectedIndex)].Code;
        s.TitleCase = chkTitle.Checked;
        s.TextMode = cboText.SelectedItem as string ?? "Auto";
        s.IncludeDesc = chkDesc.Checked;
        foreach (var c in collectors.Values) c();
    }

    // ESPLanguage_EN.exe --screenshots <folder> : every page drawn to a PNG off screen (README / checking the layout)
    public static void SaveScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        using var f = new SettingsForm(AppSettings.Load()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
        f.Show();
        Application.DoEvents();
        for (int i = 0; i < f.pages.Count; i++)
        {
            f.nav.SelectedIndex = i;
            Application.DoEvents();
            using var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize));
            bmp.Save(Path.Combine(folder, $"settings_{i}_{f.pages[i].Title.Replace(" ", "").Replace("(", "").Replace(")", "")}.png"));
        }
        f.Close();
    }

    // ------------------------------------------------------------ navigation list
    void DrawNav(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var page = pages[e.Index];
        bool sel = e.Index == nav.SelectedIndex;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, e.Bounds);
        var r = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 3, e.Bounds.Width - 16, e.Bounds.Height - 6);
        if (sel) Theme.FillRound(g, Theme.Mix(Theme.Surface, Theme.Accent, 0.28f), r, 6);

        if (e.Index == 1)                                   // line between General and the engines
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Right - 12, e.Bounds.Y);
        string text = page.Title;
        var font = sel ? Theme.UiBold : Theme.Ui;
        TextRenderer.DrawText(g, text, font, new Rectangle(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 40, e.Bounds.Height),
            sel ? Color.White : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // the engine Auto EN uses now
        if (page.Engine is { } k && (cboEngine.SelectedItem as string) == AppSettings.NameOf(k))
        {
            using var dot = new SolidBrush(Theme.Changed);
            g.FillEllipse(dot, e.Bounds.Right - 24, e.Bounds.Y + e.Bounds.Height / 2 - 4, 8, 8);
        }
    }
}
