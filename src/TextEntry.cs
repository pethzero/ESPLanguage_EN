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
    public string English { get; set; }                 // what will be written on save

    public bool IsForeign => TextCodec.IsForeign(Enc);
    public bool Changed => !string.IsNullOrEmpty(English) && (English != Original || IsForeign);
    public bool Valid => string.IsNullOrEmpty(English) || TextCodec.CanWrite(English);

    public string Status =>
        !Valid ? "Invalid chars" :
        Changed ? "Changed" :
        IsForeign ? "Not English (" + Enc + ")" : "";
}
