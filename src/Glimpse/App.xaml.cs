using System.Globalization;
using System.Windows;
namespace Glimpse;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Português é o idioma padrão; GLIMPSE_UI_CULTURE permite testar outros recursos.
        var language = Environment.GetEnvironmentVariable("GLIMPSE_UI_CULTURE");
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en-US" : language); }
        catch (CultureNotFoundException) { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR"); }
        base.OnStartup(e);
    }
}
