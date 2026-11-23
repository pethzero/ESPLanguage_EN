using System.Text;

namespace ESPLanguageEN;

// Plugin strings are raw bytes. An English Skyrim reads them as Windows-1252, so Korean / Chinese text
// saved as UTF-8 shows as "ë²šê½ƒ..." in xEdit and as boxes in game. We decode the raw bytes with the
// right code page for display, and always write the English text back as Windows-1252.
public static class TextCodec
{
    public static readonly string[] Modes =
    {
        "Auto", "UTF-8", "Korean (949)", "Chinese GBK (936)", "Chinese Big5 (950)", "Japanese (932)", "Western (1252)",
    };

    static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
    public static readonly Encoding Win1252Strict =
        Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    static readonly Encoding Win1252 = Encoding.GetEncoding(1252);

    static Encoding Strict(int cp) =>
        Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    public static byte[] Trim0(byte[] d)
    {
        int n = d.Length;
        while (n > 0 && d[n - 1] == 0) n--;
        return n == d.Length ? d : d[..n];
    }

    public static bool IsCjk(char c) =>
        (c >= 0x1100 && c <= 0x11FF) || (c >= 0x2E80 && c <= 0x30FF) || (c >= 0x3130 && c <= 0x318F) ||
        (c >= 0x3200 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFFEF);

    static bool AllNonAsciiCjk(string s)
    {
        bool any = false;
        foreach (char c in s)
        {
            if (c < 0x80) continue;
            if (!IsCjk(c) && c != '·' && c != '…' && c != '—') return false;
            any = true;
        }
        return any;
    }

    // returns the decoded text and the encoding name that was used
    public static (string Text, string Enc) Decode(byte[] raw, string mode)
    {
        bool ascii = true;
        foreach (var b in raw) if (b >= 0x80) { ascii = false; break; }
        if (ascii) return (Encoding.ASCII.GetString(raw), "ASCII");

        int forced = mode switch
        {
            "UTF-8" => 65001, "Korean (949)" => 949, "Chinese GBK (936)" => 936,
            "Chinese Big5 (950)" => 950, "Japanese (932)" => 932, "Western (1252)" => 1252, _ => 0,
        };
        if (forced == 65001) return (Encoding.UTF8.GetString(raw), "UTF-8");
        if (forced != 0) return (Encoding.GetEncoding(forced).GetString(raw), forced.ToString());

        try { return (Utf8Strict.GetString(raw), "UTF-8"); } catch { }
        foreach (int cp in new[] { 949, 936, 950, 932 })
        {
            try
            {
                string s = Strict(cp).GetString(raw);
                if (AllNonAsciiCjk(s)) return (s, cp.ToString());
            }
            catch { }
        }
        return (Win1252.GetString(raw), "1252");
    }

    // text that an English game cannot show as it is
    public static bool IsForeign(string enc) => enc != "ASCII" && enc != "1252";

    // characters Windows-1252 can hold above ASCII (built once; checking a string no longer throws/catches)
    static readonly HashSet<char> Cp1252 = BuildCp1252();

    static HashSet<char> BuildCp1252()
    {
        var set = new HashSet<char>();
        for (int b = 0x80; b <= 0xFF; b++)
        {
            try { set.Add(Win1252Strict.GetString(new[] { (byte)b })[0]); }
            catch { }                                  // 0x81, 0x8D, 0x8F, 0x90, 0x9D are undefined
        }
        return set;
    }

    public static bool CanWrite(string s)
    {
        foreach (char c in s)
            if (c >= 0x80 && !Cp1252.Contains(c)) return false;
        return true;
    }

    public static byte[] Encode(string s)
    {
        var b = Win1252Strict.GetBytes(s);
        var r = new byte[b.Length + 1];
        b.CopyTo(r, 0);
        return r;
    }
}
