using System.Text;

namespace ESPLanguageEN;

// --test <plugin> <report>: 1) unchanged save == original bytes  2) every record rebuilt (and recompressed)
// still parses to the same subrecords  3) lists the texts found
static class SelfTest
{
    public static int Run(string path, string report)
    {
        var sb = new StringBuilder();
        int rc = 0;
        try
        {
            var orig = File.ReadAllBytes(path);
            var p = Plugin.Load(path);
            bool same = p.ToBytes().AsSpan().SequenceEqual(orig);
            sb.AppendLine($"{System.IO.Path.GetFileName(path)}: {orig.Length} bytes, localized={p.Localized}");
            sb.AppendLine($"unchanged save identical: {same}");
            if (!same) rc = 1;

            var p2 = Plugin.Load(path);
            int recs = 0, comp = 0;
            foreach (var r in p2.Records()) { _ = r.Subs; r.Dirty = true; recs++; if (r.Compressed) comp++; }
            string tmp = System.IO.Path.GetTempFileName();
            File.WriteAllBytes(tmp, p2.ToBytes());
            var p3 = Plugin.Load(tmp);
            var a = p.Records().ToList();
            var b = p3.Records().ToList();
            int diff = 0;
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var sa = RecordNode.BuildSubs(a[i].Subs);
                var sbb = RecordNode.BuildSubs(b[i].Subs);
                if (!sa.AsSpan().SequenceEqual(sbb) || a[i].FormId != b[i].FormId) diff++;
            }
            File.Delete(tmp);
            sb.AppendLine($"rebuilt all: {recs} records ({comp} compressed), count {a.Count}/{b.Count}, different: {diff}");
            if (diff != 0 || a.Count != b.Count) rc = 1;

            foreach (var r in p.Records())
            {
                if (r.Type == "TES4") continue;
                foreach (var s in r.Subs)
                {
                    if (s.Type != "FULL" && s.Type != "DESC") continue;
                    var raw = TextCodec.Trim0(s.Data);
                    if (raw.Length == 0) continue;
                    var (t, enc) = TextCodec.Decode(raw, "Auto");
                    if (TextCodec.IsForeign(enc))
                        sb.AppendLine($"  {r.Type} {r.FormId:X8} {s.Type} [{enc}] {t}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("ERROR: " + ex);
            rc = 2;
        }
        File.WriteAllText(report, sb.ToString(), new UTF8Encoding(true));
        return rc;
    }

    public static int AutoEn(string path, string outPath, string lang)
    {
        var log = new StringBuilder();
        var p = Plugin.Load(path);
        var items = new List<(Sub Sub, RecordNode Rec, string Text)>();
        foreach (var r in p.Records())
        {
            if (r.Type == "TES4") continue;
            foreach (var s in r.Subs)
            {
                if (s.Type != "FULL") continue;
                var (t, enc) = TextCodec.Decode(TextCodec.Trim0(s.Data), "Auto");
                if (TextCodec.IsForeign(enc)) items.Add((s, r, t));
            }
        }
        var tr = new Translator(AppContext.BaseDirectory);
        string ini = System.IO.Path.Combine(AppContext.BaseDirectory, "ESPLanguage_EN.ini");
        if (File.Exists(ini))
            foreach (var line in File.ReadAllLines(ini))
            {
                if (line.StartsWith("Engine=")) tr.Engine = line[7..];
                if (line.StartsWith("ApiKey=")) tr.ApiKey = line[7..];
                if (line.StartsWith("LocalUrl=") && line.Length > 9) tr.LocalUrl = line[9..];
                if (line.StartsWith("LocalModel=") && line.Length > 11) tr.LocalModel = line[11..];
                if (line.StartsWith("ClaudeKey=")) tr.ClaudeKey = line[10..];
                if (line.StartsWith("ClaudeModel=") && line.Length > 12) tr.ClaudeModel = line[12..];
            }
        var (res, error) = tr.TranslateAsync(items.Select(i => i.Text).ToList(), lang, true, null).GetAwaiter().GetResult();
        int n = 0;
        for (int i = 0; i < items.Count; i++)
        {
            log.AppendLine($"{items[i].Rec.FormId:X8}  {items[i].Text}  =>  {res[i] ?? "(not translated)"}");
            if (res[i] == null || !TextCodec.CanWrite(res[i])) continue;
            items[i].Sub.Data = TextCodec.Encode(res[i]);
            items[i].Rec.Dirty = true;
            n++;
        }
        File.WriteAllBytes(outPath, p.ToBytes());
        log.AppendLine($"{n} of {items.Count} translated" + (error != null ? ", error: " + error : ""));
        File.WriteAllText(outPath + ".log.txt", log.ToString(), new UTF8Encoding(true));
        return error == null ? 0 : 1;
    }
}
