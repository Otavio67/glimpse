using System.Globalization;
using System.Windows;
namespace Glimpse;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Contains("--verify-installation")) {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {
                // Load the actual WPF view and bundled libraries without showing a window or capturing a screen.
                var window=new Views.MainWindow();
                var viewModel=(ViewModels.MainViewModel)window.DataContext;
                if(viewModel.IsInRoom || viewModel.RoomControlsVisibility!=Visibility.Collapsed) throw new InvalidOperationException("Os controles da sala apareceram antes da entrada.");
                new Views.RoomSetupDialog(true,viewModel).Close();
                new Views.RoomSetupDialog(false,viewModel).Close();
                window.Close();
                if(!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory,"ffmpeg.exe"))) throw new System.IO.FileNotFoundException("O componente de video nao foi instalado.");
                Shutdown(0);
            }
            catch(Exception ex) { try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"installation-check.log"),ex.ToString()); } catch { } Shutdown(1); }
            return;
        }
        // Português é o idioma padrão; GLIMPSE_UI_CULTURE permite testar outros recursos.
        var language = Environment.GetEnvironmentVariable("GLIMPSE_UI_CULTURE");
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en-US" : language); }
        catch (CultureNotFoundException) { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR"); }
        base.OnStartup(e);
        new Views.MainWindow().Show();
    }
}
