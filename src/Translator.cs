using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ESPLanguageEN;

// "Auto EN": only the non-English parts of a name are translated, the rest stays as it is:
//   "(lilyirm) 벚꽃 밤의 환상 가발 1" -> "(lilyirm) " + translate("벚꽃 밤의 환상 가발") + " 1"
// Order: glossary.txt (your own words) -> translation_cache.txt (earlier results) -> online engine:
//   Google (free)          unofficial free endpoint, no key, may refuse with 429 when Google limits it
//   DeepL (API key)        official DeepL API (DeepL API Free: 500,000 characters / month)
//   Google Cloud (API key) official Cloud Translation API v2
//   Local AI               Qwen / Llama / any model on an OpenAI-compatible local server (Ollama, LM Studio, llama.cpp)
public sealed class Translator
{
    public const string GlossaryFile = "glossary.txt";
    public const string CacheFile = "translation_cache.txt";

    public static readonly string[] Engines = { "Google (free)", "DeepL (API key)", "Google Cloud (API key)", "Local AI (Ollama / LM Studio)", "Claude (API key)" };
    public string ClaudeKey = "";
    public string ClaudeModel = ClaudeEngine.DefaultModel;
    public static bool IsClaude(string engine) => engine == Engines[4];
    public const string DefaultLocalUrl = "http://localhost:11434/v1";
    public const string DefaultLocalModel = "qwen2.5:7b";
    public string Engine = Engines[0];
    public string ApiKey = "";
    public string LocalUrl = DefaultLocalUrl;            // any OpenAI-compatible server: Ollama, LM Studio, llama.cpp, Jan...
    public string LocalModel = DefaultLocalModel;
    public static bool IsLocal(string engine) => engine == Engines[3];

    // a local model can need minutes to load into memory on the first request
    static readonly HttpClient LocalHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    readonly string dir;
    public readonly Dictionary<string, string> Glossary = new();
    public readonly Dictionary<string, string> Cache = new();
    bool cacheDirty;

    static readonly HttpClient Http = CreateHttp();
    static readonly Regex Run = new(@"[^\x00-\x7F](?:[^\x00-\x7F]|[\s\-_.,'&+/:0-9]+(?=[^\x00-\x7F]))*", RegexOptions.Compiled);
    static readonly string[] Small = { "a", "an", "and", "the", "of", "in", "on", "with", "for", "to", "or", "at", "by" };

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return h;
    }

    public Translator(string folder)
    {
        dir = folder;
        string g = System.IO.Path.Combine(dir, GlossaryFile);
        if (!File.Exists(g)) File.WriteAllText(g, DefaultGlossary, new UTF8Encoding(true));
        LoadPairs(g, '=', Glossary);
        LoadPairs(System.IO.Path.Combine(dir, CacheFile), '\t', Cache);
    }

    static void LoadPairs(string path, char sep, Dictionary<string, string> into)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.TrimStart().StartsWith("#")) continue;
            int i = line.IndexOf(sep);
            if (i <= 0) continue;
            string k = line[..i].Trim(), v = line[(i + 1)..].Trim();
            if (k.Length > 0 && v.Length > 0) into[k] = v;
        }
    }

    public void SaveCache()
    {
        if (!cacheDirty) return;
        var sb = new StringBuilder("# source<TAB>english - results of Google Translate, edit or delete lines freely\r\n");
        foreach (var kv in Cache.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key).Append('\t').Append(kv.Value).Append("\r\n");
        File.WriteAllText(System.IO.Path.Combine(dir, CacheFile), sb.ToString(), new UTF8Encoding(true));
        cacheDirty = false;
    }

    // "auto" -> language of the run by its script (Hangul / Kana / Han)
    static string LangOf(string run, string lang)
    {
        if (lang != "auto") return lang;
        bool han = false;
        foreach (char c in run)
        {
            if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F)) return "ko";
            if (c >= 0x3040 && c <= 0x30FF) return "ja";
            if ((c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)) han = true;
        }
        return han ? "zh-CN" : "auto";
    }

    string Known(string run)
    {
        if (Glossary.TryGetValue(run, out var g)) return g;
        if (Cache.TryGetValue(run, out var c)) return c;
        // every word is in the glossary -> join them
        var words = run.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && words.All(Glossary.ContainsKey))
            return string.Join(" ", words.Select(w => Glossary[w]));
        return null;
    }

    public async Task<(string[] Results, string Error)> TranslateAsync(
        IList<string> texts, string lang, bool titleCase, IProgress<string> progress)
    {
        var runsPerText = texts.Select(t => Run.Matches(t).Select(m => m.Value.Trim()).ToList()).ToList();
        var todo = runsPerText.SelectMany(r => r).Distinct().Where(r => Known(r) == null).ToList();
        string error = null;

        foreach (var group in todo.GroupBy(r => LangOf(r, lang)))
        {
            var batch = new List<string>();
            int len = 0;
            var all = group.ToList();
            for (int i = 0; i <= all.Count; i++)
            {
                if (i < all.Count && (batch.Count == 0 || (len + all[i].Length < 1500 && batch.Count < 40)))
                {
                    batch.Add(all[i]);
                    len += all[i].Length + 1;
                    continue;
                }
                if (batch.Count == 0) break;
                try
                {
                    progress?.Report($"Translating ({group.Key}) ... {Cache.Count} known");
                    await TranslateBatch(batch, group.Key);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    break;
                }
                batch.Clear();
                len = 0;
                if (i < all.Count) { i--; await Task.Delay(250); }
            }
            if (error != null) break;
        }
        SaveCache();

        var results = new string[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            bool complete = true;
            string s = Run.Replace(texts[i], m =>
            {
                string k = Known(m.Value.Trim());
                if (k == null) { complete = false; return m.Value; }
                string en = Clean(k, titleCase);
                return Lead(m.Value) + en + Trail(m.Value);
            });
            results[i] = complete ? Tidy(s) : null;
        }
        return (results, error);
    }

    async Task TranslateBatch(List<string> batch, string lang)
    {
        if (IsLocal(Engine) || IsClaude(Engine))
        {
            Func<List<string>, string, Task<List<string>>> ask = IsClaude(Engine) ? ClaudeAi : LocalAi;
            var res = await ask(batch, lang);
            if (res.Count == batch.Count)
            {
                for (int i = 0; i < batch.Count; i++) Put(batch[i], res[i]);
                return;
            }
            foreach (var r in batch)                    // the model returned a wrong number of lines: one by one
            {
                var one = await ask(new List<string> { r }, lang);
                if (one.Count > 0) Put(r, one[0]);
            }
            return;
        }
        if (Engine != Engines[0])
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException(Engine + " needs an API key (button \"API key...\").");
            var res = Engine == Engines[1] ? await DeepL(batch, lang) : await GoogleCloud(batch, lang);
            if (res.Count != batch.Count) throw new InvalidDataException($"{Engine}: {res.Count} answers for {batch.Count} texts");
            for (int i = 0; i < batch.Count; i++) Put(batch[i], res[i]);
            return;
        }
        string joined = string.Join("\n", batch);
        var lines = (await Google(joined, lang)).Replace("\r", "").Split('\n');
        if (lines.Length >= batch.Count && lines.Skip(batch.Count).All(string.IsNullOrWhiteSpace))
        {
            for (int i = 0; i < batch.Count; i++) Put(batch[i], lines[i]);
            return;
        }
        // line count changed: one by one
        foreach (var r in batch)
        {
            Put(r, (await Google(r, lang)).Replace("\r", "").Replace("\n", " "));
            await Task.Delay(150);
        }
    }

    void Put(string src, string en)
    {
        en = en.Trim();
        if (en.Length == 0 || en == src) return;
        Cache[src] = en;
        cacheDirty = true;
    }

    static async Task<string> Google(string q, string lang)
    {
        string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={lang}&tl=en&dt=t";
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("q", q) });
        using var resp = await Http.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode == 429 || body.Contains("Sorry..."))
                throw new HttpRequestException("the free Google service refused the request (429: too many requests / automated traffic). " +
                    "Use Engine \"DeepL (API key)\" (DeepL API Free costs nothing) or \"Google Cloud (API key)\", " +
                    "or Export TSV, translate it anywhere and Import TSV.");
            throw new HttpRequestException($"Google Translate answered {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var sb = new StringBuilder();
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
            foreach (var seg in root[0].EnumerateArray())
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 && seg[0].ValueKind == JsonValueKind.String)
                    sb.Append(seg[0].GetString());
        return sb.ToString();
    }

    async Task<List<string>> DeepL(List<string> batch, string lang)
    {
        string host = ApiKey.Trim().EndsWith(":fx") ? "api-free.deepl.com" : "api.deepl.com";
        var form = batch.Select(t => new KeyValuePair<string, string>("text", t)).ToList();
        form.Add(new("target_lang", "EN-US"));
        string src = lang switch { "ko" => "KO", "ja" => "JA", "zh-CN" => "ZH", _ => null };
        if (src != null) form.Add(new("source_lang", src));
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/v2/translate") { Content = new FormUrlEncodedContent(form) };
        req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + ApiKey.Trim());
        using var doc = await SendJson(req, "DeepL");
        return doc.RootElement.GetProperty("translations").EnumerateArray().Select(t => t.GetProperty("text").GetString()).ToList();
    }

    async Task<List<string>> GoogleCloud(List<string> batch, string lang)
    {
        var form = batch.Select(t => new KeyValuePair<string, string>("q", t)).ToList();
        form.Add(new("target", "en"));
        form.Add(new("format", "text"));
        if (lang != "auto") form.Add(new("source", lang));
        string url = "https://translation.googleapis.com/language/translate/v2?key=" + Uri.EscapeDataString(ApiKey.Trim());
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var doc = await SendJson(req, "Google Cloud");
        return doc.RootElement.GetProperty("data").GetProperty("translations").EnumerateArray()
                  .Select(t => System.Net.WebUtility.HtmlDecode(t.GetProperty("translatedText").GetString())).ToList();
    }

    // ------------------------------------------------------------ local AI (OpenAI-compatible chat API)
    static string LangName(string lang) => lang switch
    {
        "ko" => "Korean", "ja" => "Japanese", "zh-CN" => "Chinese", _ => "Korean, Chinese or Japanese",
    };

    string LocalBase()
    {
        string u = (LocalUrl ?? "").Trim().TrimEnd('/');
        if (u.Length == 0) u = DefaultLocalUrl;
        if (!u.EndsWith("/v1")) u += "/v1";            // "http://localhost:11434" works too
        return u;
    }

    static string AiSystem(string lang) =>
        $"You translate names from a Skyrim game mod (items, armor, spells, effects, NPCs) from {LangName(lang)} to English.\n" +
        "Rules: short natural English game names in Title Case; keep proper names (transliterate them); no notes, no quotes, " +
        "no explanations. You get a JSON array of strings. Reply with ONLY a JSON array of the English strings, " +
        "same count and same order.";

    async Task<List<string>> ClaudeAi(List<string> batch, string lang)
    {
        string text = await ClaudeEngine.Ask(ClaudeKey, ClaudeModel, AiSystem(lang), JsonSerializer.Serialize(batch, JsonOpts));
        return ParseList(text, batch.Count);
    }

    async Task<List<string>> LocalAi(List<string> batch, string lang)
    {
        string model = string.IsNullOrWhiteSpace(LocalModel) ? DefaultLocalModel : LocalModel.Trim();
        string system = AiSystem(lang);
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["temperature"] = 0,
            ["stream"] = false,
            ["messages"] = new object[]
            {
                new Dictionary<string, string> { ["role"] = "system", ["content"] = system },
                new Dictionary<string, string> { ["role"] = "user", ["content"] = JsonSerializer.Serialize(batch, JsonOpts) },
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, LocalBase() + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json"),
        };
        JsonDocument doc;
        try { doc = await SendJson(req, "Local AI (" + model + ")", LocalHttp); }
        catch (HttpRequestException ex) when (ex.StatusCode == null)
        {
            throw new HttpRequestException($"cannot reach the local AI server at {LocalBase()}.\n" +
                "Start Ollama (or LM Studio's server) first. Ollama: install from ollama.com, then run  ollama pull " + DefaultLocalModel +
                "\nDetails: " + ex.Message);
        }
        using (doc)
        {
            string text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return ParseList(text, batch.Count);
        }
    }

    static readonly JsonSerializerOptions JsonOpts = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // the JSON array in a model answer; tolerates <think> blocks, ```json fences and plain numbered lines
    internal static List<string> ParseList(string text, int expected)
    {
        text = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
        int a = text.IndexOf('['), b = text.LastIndexOf(']');
        if (a >= 0 && b > a)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<JsonElement>>(text[a..(b + 1)]);
                return list.Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()).ToList();
            }
            catch (JsonException) { }
        }
        var lines = text.Replace("\r", "").Split('\n')
            .Select(l => Regex.Replace(l, @"^\s*(?:\d+[.)]|[-*])\s*", "").Trim().Trim('"'))
            .Where(l => l.Length > 0 && !l.StartsWith("```")).ToList();
        return lines.Count == expected ? lines : new List<string>();
    }

    // model names the server has (Ollama: the pulled models)
    public async Task<List<string>> LocalModels()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LocalBase() + "/models");
        using var doc = await SendJson(req, "Local AI", LocalHttp);
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()).OrderBy(s => s).ToList();
    }

    static async Task<JsonDocument> SendJson(HttpRequestMessage req, string name, HttpClient client = null)
    {
        using var resp = await (client ?? Http).SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string hint = (int)resp.StatusCode switch
            {
                401 or 403 => " (wrong API key?)", 456 => " (monthly character limit reached)", 429 => " (too many requests, try again later)", _ => "",
            };
            throw new HttpRequestException($"{name} answered {(int)resp.StatusCode} {resp.ReasonPhrase}{hint}: {body[..Math.Min(body.Length, 200)]}", null, resp.StatusCode);
        }
        return JsonDocument.Parse(body);
    }

    // spaces around a run are kept, a space is added where Latin text would touch the translation
    static string Lead(string run) => run.Length > 0 && char.IsWhiteSpace(run[0]) ? " " : "\u0001";
    static string Trail(string run) => run.Length > 0 && char.IsWhiteSpace(run[^1]) ? " " : "\u0002";

    static string Tidy(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\u0001')
            {
                if (sb.Length > 0 && (char.IsLetterOrDigit(sb[^1]) || sb[^1] == ')' || sb[^1] == ']')) sb.Append(' ');
                continue;
            }
            if (c == '\u0002')
            {
                if (i + 1 < s.Length && (char.IsLetterOrDigit(s[i + 1]) || s[i + 1] == '(' || s[i + 1] == '[')) sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
    }

    // plain ASCII-friendly English (the game font is Windows-1252)
    public static string Clean(string s, bool titleCase)
    {
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"')
             .Replace('–', '-').Replace('—', '-').Replace("…", "...").Replace('【', '[').Replace('】', ']')
             .Replace('（', '(').Replace('）', ')').Replace('，', ',').Replace('：', ':').Replace('·', ' ');
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (!titleCase) return s;
        var words = s.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (w.Length == 0 || !char.IsLower(w[0])) continue;
            if (i > 0 && Small.Contains(w)) continue;
            words[i] = char.ToUpperInvariant(w[0]) + w[1..];
        }
        return string.Join(" ", words);
    }

    const string DefaultGlossary =
@"# ESPLanguage_EN glossary: your own translations, used before Google.
# One per line:  source = English
# A whole name part (""벚꽃 밤의 환상"") or single words (when every word of a part is listed, they are joined).
# Edit this file freely, then press ""Reload glossary"" (or restart).

# Korean
가발 = Wig
머리 = Hair
신발 = Shoes
부츠 = Boots
장갑 = Gloves
치마 = Skirt
스커트 = Skirt
스타킹 = Stockings
넥타이 = Tie
드레스 = Dress
속옷 = Underwear
갑옷 = Armor
투구 = Helmet
목걸이 = Necklace
귀걸이 = Earrings
반지 = Ring

# Chinese
假发 = Wig
头发 = Hair
鞋子 = Shoes
靴子 = Boots
手套 = Gloves
裙子 = Skirt
短裙 = Short Skirt
丝袜 = Stockings
袜子 = Socks
领带 = Tie
连衣裙 = Dress
内衣 = Underwear
盔甲 = Armor
头盔 = Helmet
项链 = Necklace
耳环 = Earrings
戒指 = Ring
";
}
