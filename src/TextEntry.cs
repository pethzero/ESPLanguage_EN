namespace ESPLanguageEN;

// one FULL (or DESC) text of one record = one row of the grid
public sealed class TextEntry
{
    public RecordNode Rec;
    public Sub Sub;
    public string Key;                                  // FormID|Field, for import / reload

    public string Type { get; set; }
    public string FormID { get; set; }
    public string EditorID { get; set; }
    public string Field { get; set; }
    public string Original { get; set; }                // decoded text that is in the file now
    public string Enc { get; set; }

    string english;
    bool valid = true;

    // what will be written on save; validity is computed once here, not on every repaint of the grid
    public string English
    {
        get => english;
        set { english = value; valid = string.IsNullOrEmpty(value) || TextCodec.CanWrite(value); }
    }

    public bool IsForeign => TextCodec.IsForeign(Enc);
    public bool Changed => !string.IsNullOrEmpty(english) && (english != Original || IsForeign);
    public bool Valid => valid;

    public string Status =>
        !valid ? "Invalid chars" :
        Changed ? "Changed" :
        IsForeign ? "Not English (" + Enc + ")" : "";
}
