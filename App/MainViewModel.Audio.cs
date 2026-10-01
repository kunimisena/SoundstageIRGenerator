using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public sealed partial class MainViewModel
{
    string audioInput="";
    public string AudioInput {get=>audioInput;set{audioInput=value;Notify();CommandManager.InvalidateRequerySuggested();}}
    public string AudioOutputParent {get;set;}="";
    public int AudioFormatIndex {get;set;}=2;
    public string[] AudioFormats {get;}=["WAV · float32","FLAC · 24 bit","M4A / AAC · 320 kbps"];
    int audioLevelModeIndex;
    public int AudioLevelModeIndex {get=>audioLevelModeIndex;set{if(Localizing||value<0)return;audioLevelModeIndex=value;Notify();Notify(nameof(AudioNormalizesLoudness));}}
    public bool AudioNormalizesLoudness=>AudioLevelModeIndex==0;
    public LocalizedOption[] AudioLevelModes {get;}=[new("TAE94FDE125"), new("TF40DCB29DB"), new("T8335BA2AB4")];
    public double AudioTargetLufs {get;set;}=-18;
    string rawAudioSummary=SoundstageIR.Core.TextCatalog.T("T7A0CF8AD05");
    public string AudioSummary {get=>TextCatalog.Diagnostic(rawAudioSummary);private set=>rawAudioSummary=value;}
    public ICommand ChooseAudioCommand {get;private set;}=null!;
    public ICommand RenderAudioCommand {get;private set;}=null!;
    public string AudioLastFile {get;private set;}="";
    public ICommand OpenAudioOutputCommand {get;private set;}=null!;
    public ICommand ApoExportCommand {get;private set;}=null!;
    void InitializeAudio()
    {
        AudioOutputParent=Path.Combine(Root,"processed-audio");
        InitializeAudioTools();
        OpenAudioOutputCommand=new ActionCommand(_=>Safe(()=>Process.Start(new ProcessStartInfo(Path.GetDirectoryName(AudioLastFile)!){UseShellExecute=true})),()=>File.Exists(AudioLastFile));
        ChooseAudioCommand=new ActionCommand(_=>{var dialog=new OpenFileDialog{Filter=SoundstageIR.Core.TextCatalog.T("T1547FA829D")};if(dialog.ShowDialog()==true)AudioInput=dialog.FileName;},()=>Ready);
        RenderAudioCommand=new ActionCommand(async _=>await RenderAudioAsync(),()=>CanExport&&!CheckingAudioTools&&File.Exists(AudioInput)&&AudioToolsAvailable);
        ApoExportCommand=new ActionCommand(async _=>await ExportApoAsync(),()=>CanExport);
    }
    public async Task<string?> ExportApoAsync(string? executableDirectory=null)
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus(SoundstageIR.Core.TextCatalog.T("T4B082BE8D7"));return null;}
        try
        {
            Busy=true;Notify("");CommandManager.InvalidateRequerySuggested();var result=Result;
            LastExport=await Task.Run(()=>ApoExporter.Export(result,executableDirectory??AppContext.BaseDirectory));
            Status=SoundstageIR.Core.TextCatalog.T("TFFFA023EB0")+LastExport;return LastExport;
        }
        catch(Exception ex){Status=SoundstageIR.Core.TextCatalog.T("TE1A22EA03E")+ex.Message;return null;}
        finally{Busy=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
    public async Task<AudioRenderResult?> RenderAudioAsync()
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus(SoundstageIR.Core.TextCatalog.T("T4B082BE8D7"));return null;}
        try
        {
            Busy=true;Progress=0;cancellation=new();Notify("");CommandManager.InvalidateRequerySuggested();
            var result=Result;var options=new AudioRenderOptions(AudioInput,AudioOutputParent,(AudioFormat)AudioFormatIndex,(AudioLevelMode)AudioLevelModeIndex,AudioTargetLufs,SelectedFfmpegPath);
            var progress=new Progress<(double Fraction,string Message)>(v=>{Progress=v.Fraction*100;Status=v.Message;Notify(nameof(Progress));Notify(nameof(Status));});
            var rendered=await Task.Run(()=>AudioRenderer.RenderAsync(result,options,progress,cancellation.Token));
            AudioLastFile=rendered.File;
            string Loudness(double? value)=>value is double n?$"{n:F2}":SoundstageIR.Core.TextCatalog.T("T2884D29F4C");
            AudioSummary=SoundstageIR.Core.TextCatalog.F("T1C2AE2EB77", AudioLevelModes[(int)rendered.Mode], rendered.File, rendered.SampleRate, (rendered.OutputSamples/(double)rendered.SampleRate), Loudness(rendered.Before?.IntegratedLufs), Loudness(rendered.After?.IntegratedLufs), Loudness(rendered.After?.TruePeakDbTp), rendered.GainDb, rendered.SafetyGainDb, (rendered.LimiterCeilingDb is double ceiling?SoundstageIR.Core.TextCatalog.F("T70288B8F03", ceiling):""), TextCatalog.Diagnostic(rendered.Note));
            Status=SoundstageIR.Core.TextCatalog.T("TF199967A1B");return rendered;
        }
        catch(OperationCanceledException){Status=SoundstageIR.Core.TextCatalog.T("TF45492104A");return null;}
        catch(Exception ex){Status=SoundstageIR.Core.TextCatalog.T("T88DE5B9C3F")+ex.Message;return null;}
        finally{Busy=false;cancellation?.Dispose();cancellation=null;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
}
