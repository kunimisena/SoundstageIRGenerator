using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;

public sealed partial class MainViewModel
{
    sealed record AudioToolPreferences(string FfmpegPath);
    public string SelectedFfmpegPath {get;private set;}="";
    public string FfmpegPathDisplay {get;private set;}="";
    public string AudioToolStatus {get;private set;}="";
    public bool AudioToolsAvailable {get;private set;}
    public bool CheckingAudioTools {get;private set;}
    public bool CanConfigureAudioTools=>Ready&&!CheckingAudioTools;
    public ICommand ChooseFfmpegCommand {get;private set;}=null!;
    public ICommand AutoFfmpegCommand {get;private set;}=null!;
    public ICommand CheckFfmpegCommand {get;private set;}=null!;
    string AudioToolsSettingsFile=>Path.Combine(Root,"settings","audio-tools.json");
    void InitializeAudioTools()
    {
        string? loadError=null;
        try
        {
            if(File.Exists(AudioToolsSettingsFile))SelectedFfmpegPath=JsonSerializer.Deserialize<AudioToolPreferences>(File.ReadAllText(AudioToolsSettingsFile))?.FfmpegPath??"";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
        {loadError="路径设置读取失败："+ex.Message;}
        RefreshAudioTools();
        if(loadError!=null)AudioToolStatus=loadError;
        ChooseFfmpegCommand=new ActionCommand(async _=>
        {
            var dialog=new OpenFileDialog{Title="选择 FFmpeg（同目录需有 ffprobe.exe）",Filter="FFmpeg|ffmpeg.exe",CheckFileExists=true};
            if(File.Exists(SelectedFfmpegPath))dialog.FileName=SelectedFfmpegPath;
            if(dialog.ShowDialog()==true){SetAudioToolsPath(dialog.FileName);await CheckAudioToolsAsync();}
        },()=>CanConfigureAudioTools);
        AutoFfmpegCommand=new ActionCommand(async _=>{SetAudioToolsPath("");await CheckAudioToolsAsync();},()=>CanConfigureAudioTools);
        CheckFfmpegCommand=new ActionCommand(async _=>await CheckAudioToolsAsync(),()=>CanConfigureAudioTools);
    }
    void RefreshAudioTools()
    {
        FfmpegPathDisplay=SelectedFfmpegPath;
        try
        {
            var tools=AudioRenderer.ResolveTools(SelectedFfmpegPath);FfmpegPathDisplay=tools.Ffmpeg;
            AudioToolsAvailable=true;AudioToolStatus="已找到 FFmpeg 和 ffprobe；处理前会自动检查可用性。";
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException)
        {AudioToolsAvailable=false;AudioToolStatus=ex.Message;}
        Notify("");CommandManager.InvalidateRequerySuggested();
    }
    public void SetAudioToolsPath(string path)
    {
        if(!CanConfigureAudioTools)return;
        SelectedFfmpegPath=path;RefreshAudioTools();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AudioToolsSettingsFile)!);
            File.WriteAllText(AudioToolsSettingsFile,JsonSerializer.Serialize(new AudioToolPreferences(path),new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {AudioToolStatus+="\n路径仅用于本次会话，保存失败："+ex.Message;Notify(nameof(AudioToolStatus));}
    }
    public async Task<bool> CheckAudioToolsAsync()
    {
        if(!CanConfigureAudioTools)return false;
        CheckingAudioTools=true;AudioToolStatus="正在检查 FFmpeg…";Notify("");CommandManager.InvalidateRequerySuggested();
        try
        {
            var tools=AudioRenderer.ResolveTools(SelectedFfmpegPath);
            string version=await Task.Run(()=>AudioRenderer.CheckToolsAsync(tools));
            FfmpegPathDisplay=tools.Ffmpeg;AudioToolsAvailable=true;
            AudioToolStatus="可用 · "+version+"\n卷积、重采样、响度测量与限幅检查通过。";return true;
        }
        catch(Exception ex){AudioToolsAvailable=false;AudioToolStatus="FFmpeg 不可用："+ex.Message;return false;}
        finally{CheckingAudioTools=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
}
