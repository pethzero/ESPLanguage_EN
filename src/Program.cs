using System.Text;

namespace ESPLanguageEN;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // ESPLanguage_EN.exe --test <plugin> <report.txt> : parse / rebuild check (no GUI)
        if (args.Length >= 3 && args[0] == "--test")
            return SelfTest.Run(args[1], args[2]);
        // ESPLanguage_EN.exe --auto-en <plugin> <output plugin> [lang] : translate every non-English FULL, no GUI
        if (args.Length >= 3 && args[0] == "--auto-en")
            return SelfTest.AutoEn(args[1], args[2], args.Length > 3 ? args[3] : "auto");

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        return 0;
    }
}
