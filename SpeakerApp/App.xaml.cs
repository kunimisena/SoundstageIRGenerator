using System.IO;
using System.Windows;
using SoundstageIRGenerator;
namespace SoundstageSpeakers;
public partial class SpeakerApplication:Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        bool check=e.Args.Contains("--check");
        UiLanguage.Initialize(check?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args.Last()))!,"workspace"):AppContext.BaseDirectory);
        if(check){try{Shutdown(await SpeakerWorkflowChecks.Run(e.Args.Last()));}catch(Exception ex){File.WriteAllText(e.Args.Last(),ex.ToString());Shutdown(1);}return;}
        var choice=new SpeakerModeDialog();if(choice.ShowDialog()!=true||choice.Selection is not {} mode){Shutdown();return;}
        var window=new SoundstageIRGenerator.MainWindow(mode);MainWindow=window;ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();
    }
}
