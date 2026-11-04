using System.Globalization;
using System.Windows;
namespace Glimpse;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // English is the default. This opt-in is for testing the pt-BR resources until settings are implemented.
        var language = Environment.GetEnvironmentVariable("GLIMPSE_UI_CULTURE");
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en-US" : language); }
        catch (CultureNotFoundException) { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); }
        base.OnStartup(e);
    }
}
