using System.Windows;
namespace StatisticalFieldStudio;
public partial class App:Application
{
    internal static string? TestRoot;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool headless=e.Args.Length>0&&e.Args[0]=="--headless-check";
        if(headless)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            string report=e.Args.Length>1?System.IO.Path.GetFullPath(e.Args[1]):System.IO.Path.Combine(AppContext.BaseDirectory,"validation");
            TestRoot=System.IO.Path.Combine(report,"workspace");System.IO.Directory.CreateDirectory(TestRoot);
            var checkWindow=new MainWindow();MainWindow=checkWindow;
            Shutdown(await checkWindow.RunHeadlessChecks(report));return;
        }
        var window=new MainWindow();MainWindow=window;window.Show();
    }
}
