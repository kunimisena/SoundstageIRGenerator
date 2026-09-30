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
    public int AudioLevelModeIndex {get=>audioLevelModeIndex;set{audioLevelModeIndex=value;Notify();Notify(nameof(AudioNormalizesLoudness));}}
    public bool AudioNormalizesLoudness=>AudioLevelModeIndex==0;
    public string[] AudioLevelModes {get;}=["补偿到目标 LUFS，再限幅","保持卷积后电平，仅限幅","旁路：原始卷积结果"];
    public double AudioTargetLufs {get;set;}=-18;
    public string AudioSummary {get;private set;}="使用当前生成的最终四条核，输出普通双声道文件。";
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
        ChooseAudioCommand=new ActionCommand(_=>{var dialog=new OpenFileDialog{Filter="音频文件|*.wav;*.flac;*.mp3;*.m4a;*.aac;*.ogg;*.opus;*.aiff;*.wma|所有文件|*.*"};if(dialog.ShowDialog()==true)AudioInput=dialog.FileName;},()=>Ready);
        RenderAudioCommand=new ActionCommand(async _=>await RenderAudioAsync(),()=>CanExport&&!CheckingAudioTools&&File.Exists(AudioInput)&&AudioToolsAvailable);
        ApoExportCommand=new ActionCommand(async _=>await ExportApoAsync(),()=>CanExport);
    }
    public async Task<string?> ExportApoAsync(string? executableDirectory=null)
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus("参数已修改，请先重新生成。");return null;}
        try
        {
            Busy=true;Notify("");CommandManager.InvalidateRequerySuggested();var result=Result;
            LastExport=await Task.Run(()=>ApoExporter.Export(result,executableDirectory??AppContext.BaseDirectory));
            Status="APO 配置已导出："+LastExport;return LastExport;
        }
        catch(Exception ex){Status="APO 导出失败："+ex.Message;return null;}
        finally{Busy=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
    public async Task<AudioRenderResult?> RenderAudioAsync()
    {
        if(Busy||CommitTemplateEdits?.Invoke()==false)return null;Commit();if(Result==null||Stale){SetStatus("参数已修改，请先重新生成。");return null;}
        try
        {
            Busy=true;Progress=0;cancellation=new();Notify("");CommandManager.InvalidateRequerySuggested();
            var result=Result;var options=new AudioRenderOptions(AudioInput,AudioOutputParent,(AudioFormat)AudioFormatIndex,(AudioLevelMode)AudioLevelModeIndex,AudioTargetLufs,SelectedFfmpegPath);
            var progress=new Progress<(double Fraction,string Message)>(v=>{Progress=v.Fraction*100;Status=v.Message;Notify(nameof(Progress));Notify(nameof(Status));});
            var rendered=await Task.Run(()=>AudioRenderer.RenderAsync(result,options,progress,cancellation.Token));
            AudioLastFile=rendered.File;
            string Loudness(double? value)=>value is double n?$"{n:F2}":"不可测";
            AudioSummary=$"{AudioLevelModes[(int)rendered.Mode]}\n已导出：{rendered.File}\n{rendered.SampleRate} Hz · {(rendered.OutputSamples/(double)rendered.SampleRate):F2} 秒（含尾部）\n响度 {Loudness(rendered.Before?.IntegratedLufs)} → {Loudness(rendered.After?.IntegratedLufs)} LUFS · 成品真峰值 {Loudness(rendered.After?.TruePeakDbTp)} dBTP\n响度增益 {rendered.GainDb:+0.00;-0.00;0} dB · 峰值/编码额外修正 {rendered.SafetyGainDb:0.00} dB\n{(rendered.LimiterCeilingDb is double ceiling?$"限幅阈值 {ceiling:F2} dBFS · 成品峰值上限 0 dB":"")}\n{rendered.Note}";
            Status="歌曲处理完成。";return rendered;
        }
        catch(OperationCanceledException){Status="歌曲处理已取消，原文件保持不变。";return null;}
        catch(Exception ex){Status="歌曲处理失败："+ex.Message;return null;}
        finally{Busy=false;cancellation?.Dispose();cancellation=null;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
}
