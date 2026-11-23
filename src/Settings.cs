namespace ESPLanguageEN;

public enum EngineKind { GoogleFree, DeepL, GoogleCloud, Claude, LocalAI, Nllb }

// everything the user sets, in one place; stored in ESPLanguage_EN.ini next to the exe
public sealed class AppSettings
{
    public static readonly string IniPath = Path.Combine(AppContext.BaseDirectory, "ESPLanguage_EN.ini");

    public static readonly (EngineKind Kind, string Id, string Name)[] EngineList =
    {
        (EngineKind.Claude, "claude", "Claude"),
        (EngineKind.LocalAI, "local", "Local AI (Ollama / LM Studio)"),
        (EngineKind.Nllb, "nllb", "NLLB-200 (local server)"),
        (EngineKind.DeepL, "deepl", "DeepL"),
        (EngineKind.GoogleCloud, "gcloud", "Google Cloud"),
        (EngineKind.GoogleFree, "google", "Google (free)"),
    };
    public static string NameOf(EngineKind k) => EngineList.First(e => e.Kind == k).Name;

    public static EngineKind KindByName(string name, EngineKind fallback)
    {
        foreach (var e in EngineList) if (e.Name == name) return e.Kind;
        return fallback;
    }

    static EngineKind KindById(string id, EngineKind fallback)
    {
        foreach (var e in EngineList) if (e.Id == id) return e.Kind;
        return fallback;
    }

    static string LangCode(string v)
    {
        foreach (var l in Langs) if (l.Code == v || l.Name == v || l.Name.StartsWith(v + " ")) return l.Code;
        return "auto";
    }

    public static readonly (string Name, string Code)[] Langs =
        { ("Auto (by script)", "auto"), ("Korean", "ko"), ("Chinese", "zh-CN"), ("Japanese", "ja") };

    public const string DefaultLocalUrl = "http://localhost:11434/v1";
    public const string DefaultLocalModel = "qwen2.5:7b";
    public const string DefaultNllbUrl = "http://localhost:8000/v1";

    // general
    public EngineKind Engine = EngineKind.GoogleFree;
    public string Lang = "auto";
    public bool TitleCase = true;
    public string TextMode = "Auto";             // TextCodec.Modes
    public bool IncludeDesc;
    public bool RomanizeSetNames;                // "요대선몽 - 소매" -> "Yodaeseonmong - Sleeves"
    public string LastDir = "";

    // engines
    public string DeepLKey = "";
    public string GoogleKey = "";
    public string ClaudeKey = "";
    public string ClaudeModel = ClaudeEngine.DefaultModel;
    public string LocalUrl = DefaultLocalUrl;
    public string LocalModel = DefaultLocalModel;
    public string NllbUrl = DefaultNllbUrl;
    public string NllbFolder = "";               // folder with start_nllb.bat, for the "Start server" button

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public static AppSettings Load()
    {
        var s = new AppSettings();
        if (!File.Exists(IniPath)) return s;
        string legacyKey = "", legacyEngine = "";
        foreach (var line in File.ReadAllLines(IniPath))
        {
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            string k = line[..i].Trim().TrimStart('﻿'), v = line[(i + 1)..].Trim();
            switch (k)
            {
                case "EngineId": s.Engine = KindById(v, s.Engine); break;
                case "Engine": legacyEngine = v; break;            // 1.1.0 wrote the display name
                case "Lang": s.Lang = LangCode(v); break;
                case "TitleCase": s.TitleCase = v == "1"; break;
                case "TextMode": if (TextCodec.Modes.Contains(v)) s.TextMode = v; break;
                case "Desc": s.IncludeDesc = v == "1"; break;
                case "SetNames": s.RomanizeSetNames = v == "romanize"; break;
                case "LastDir": s.LastDir = v; break;
                case "DeepLKey": s.DeepLKey = v; break;
                case "GoogleKey": s.GoogleKey = v; break;
                case "ApiKey": legacyKey = v; break;                 // 1.1.0: one key shared by DeepL and Google Cloud
                case "ClaudeKey": s.ClaudeKey = v; break;
                case "ClaudeModel": if (v.Length > 0) s.ClaudeModel = v; break;
                case "LocalUrl": if (v.Length > 0) s.LocalUrl = v; break;
                case "LocalModel": if (v.Length > 0) s.LocalModel = v; break;
                case "NllbUrl": if (v.Length > 0) s.NllbUrl = v; break;
                case "NllbFolder": s.NllbFolder = v; break;
            }
        }
        // settings from 1.1.0
        if (legacyKey.Length > 0)
        {
            if (legacyKey.StartsWith("AIza")) { if (s.GoogleKey.Length == 0) s.GoogleKey = legacyKey; }   // Google API keys start with AIza
            else if (s.DeepLKey.Length == 0) s.DeepLKey = legacyKey;
        }
        if (!File.ReadAllText(IniPath).Contains("EngineId=") && legacyEngine.Length > 0)
        {
            s.Engine = legacyEngine switch
            {
                var x when x.StartsWith("DeepL") => EngineKind.DeepL,
                var x when x.StartsWith("Google Cloud") => EngineKind.GoogleCloud,
                var x when x.StartsWith("Claude") => EngineKind.Claude,
                var x when x.StartsWith("Local AI") => s.LocalUrl.Contains(":8000") ? EngineKind.Nllb : EngineKind.LocalAI,
                _ => EngineKind.GoogleFree,
            };
            if (s.Engine == EngineKind.Nllb) s.NllbUrl = s.LocalUrl;
        }
        return s;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(IniPath, new[]
            {
                "[General]",
                "EngineId=" + EngineList.First(e => e.Kind == Engine).Id,
                "Lang=" + Lang,
                "TitleCase=" + (TitleCase ? 1 : 0),
                "TextMode=" + TextMode,
                "Desc=" + (IncludeDesc ? 1 : 0),
                "SetNames=" + (RomanizeSetNames ? "romanize" : "translate"),
                "LastDir=" + LastDir,
                "",
                "[DeepL]", "DeepLKey=" + DeepLKey,
                "",
                "[GoogleCloud]", "GoogleKey=" + GoogleKey,
                "",
                "[Claude]", "ClaudeKey=" + ClaudeKey, "ClaudeModel=" + ClaudeModel,
                "",
                "[LocalAI]", "LocalUrl=" + LocalUrl, "LocalModel=" + LocalModel,
                "",
                "[NLLB]", "NllbUrl=" + NllbUrl, "NllbFolder=" + NllbFolder,
            });
        }
        catch { }
    }

    // one line for the status bar: which engine Auto EN will use, and whether it is ready
    public string EngineSummary(out bool ready)
    {
        ready = true;
        string name = NameOf(Engine);
        switch (Engine)
        {
            case EngineKind.DeepL: ready = DeepLKey.Length > 0; return ready ? name : name + " — no API key";
            case EngineKind.GoogleCloud: ready = GoogleKey.Length > 0; return ready ? name : name + " — no API key";
            case EngineKind.Claude: ready = ClaudeKey.Length > 0; return ready ? name + " · " + ClaudeModel : name + " — no API key";
            case EngineKind.LocalAI: return name.Split(' ')[0] + " AI · " + LocalModel;
            case EngineKind.Nllb: return "NLLB-200 · " + NllbUrl.Replace("http://", "");
            default: return name;
        }
    }
}
