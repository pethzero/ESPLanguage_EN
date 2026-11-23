using System.Text;

namespace ESPLanguageEN;

// Korean -> Latin letters (Revised Romanization of Korean), for names that have no meaning to translate:
//   요대선몽 -> Yodaeseonmong,  성홍렵야 -> Seonghongnyeobya
// Hangul syllables are split into initial / vowel / final; the common sound rules between syllables are applied
// (final consonant moves to a following ㅇ, ㄹ+ㄹ = ll, ㄴ+ㄹ = ll, nasal before ㄴ/ㅁ, ㄹ after ㅇ/ㅁ = n).
// Characters that are not Hangul syllables are kept as they are.
public static class Romanizer
{
    static readonly string[] Initial = { "g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h" };
    static readonly string[] Vowel = { "a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i" };
    // final consonant at the end of a word / before a consonant
    static readonly string[] Final = { "", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "l", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t" };
    // final consonant carried over to a following ㅇ (liaison): 선이 -> seoni, 렵야 -> ryeobya
    static readonly string[] Carry = { "", "g", "kk", "ks", "n", "nj", "nh", "d", "r", "lg", "lm", "lb", "ls", "lt", "lp", "lh", "m", "b", "bs", "s", "ss", "ng", "j", "ch", "k", "t", "p", "h" };

    const int FinalNone = 0, FinalN = 4, FinalR = 8, FinalM = 16, FinalNg = 21, FinalK = 1, FinalP = 17, FinalT = 7;
    const int InitR = 5, InitN = 2, InitM = 6, InitSilent = 11;

    static bool IsSyllable(char c) => c >= 0xAC00 && c <= 0xD7A3;

    public static bool HasHangul(string s) => s.Any(IsSyllable);

    public static string Korean(string text)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!IsSyllable(c)) { sb.Append(c); continue; }
            int idx = c - 0xAC00, ini = idx / 588, vow = idx % 588 / 28, fin = idx % 28;
            int nextIni = i + 1 < text.Length && IsSyllable(text[i + 1]) ? (text[i + 1] - 0xAC00) / 588 : -1;

            // initial: ㄹ after a nasal (ㅇ/ㅁ) final of the previous syllable reads n (홍렵 -> hongnyeop)
            string first = Initial[ini];
            if (i > 0 && IsSyllable(text[i - 1]) && ini == InitR)
            {
                int prevFin = (text[i - 1] - 0xAC00) % 28;
                if (prevFin == FinalNg || prevFin == FinalM || prevFin == FinalK || prevFin == FinalP) first = "n";
                else if (prevFin == FinalR || prevFin == FinalN) first = "l";
            }
            sb.Append(first).Append(Vowel[vow]);

            if (fin == FinalNone) continue;
            if (nextIni == InitSilent && fin != FinalNg) { sb.Append(Carry[fin]); continue; }       // moves to the next syllable
            if (nextIni == InitR && (fin == FinalR || fin == FinalN)) { sb.Append('l'); continue; }  // 신라 -> silla
            if (nextIni == InitR || nextIni == InitN || nextIni == InitM)                             // nasal assimilation
            {
                string f = Final[fin];
                sb.Append(f == "k" ? "ng" : f == "t" ? "n" : f == "p" ? "m" : f);
                continue;
            }
            sb.Append(Final[fin]);
        }
        return sb.ToString();
    }

    // "요대선몽" -> "Yodaeseonmong"; every word starts with a capital letter
    public static string Name(string text)
    {
        var words = Korean(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
