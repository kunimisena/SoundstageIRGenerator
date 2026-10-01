using System.Windows;
namespace SoundstageIRGenerator;
public partial class App:Application
{
    internal static string? TestRoot;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool languageCheck=e.Args.Length>0&&e.Args[0]=="--language-check";
        if(languageCheck)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            TestRoot=System.IO.Path.Combine(System.IO.Path.GetFullPath(e.Args[1]),"workspace");
            UiLanguage.Initialize(TestRoot);
            Shutdown(await LanguageChecks.Run(e.Args[1]));return;
        }
        bool headless=e.Args.Length>0&&e.Args[0]=="--headless-check";
        if(headless)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            string report=e.Args.Length>1?System.IO.Path.GetFullPath(e.Args[1]):System.IO.Path.Combine(AppContext.BaseDirectory,"validation");
            TestRoot=System.IO.Path.Combine(report,"workspace");System.IO.Directory.CreateDirectory(TestRoot);
            UiLanguage.Initialize(TestRoot);
            SoundstageIR.Core.TextCatalog.SetLanguage("zh-CN");UiLanguage.ApplyResources();
            var checkWindow=new MainWindow();MainWindow=checkWindow;
            Shutdown(await checkWindow.RunHeadlessChecks(report));return;
        }
        UiLanguage.Initialize(MainViewModel.FindRoot());
        var window=new MainWindow();MainWindow=window;window.Show();
    }
}
