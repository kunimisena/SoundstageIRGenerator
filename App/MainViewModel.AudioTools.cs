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
    string rawAudioToolStatus="";
    public string AudioToolStatus {get=>TextCatalog.Diagnostic(rawAudioToolStatus);private set=>rawAudioToolStatus=value;}
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
        {loadError=SoundstageIR.Core.TextCatalog.T("T6A716F6B71")+ex.Message;}
        RefreshAudioTools();
        if(loadError!=null)AudioToolStatus=loadError;
        ChooseFfmpegCommand=new ActionCommand(async _=>
        {
            var dialog=new OpenFileDialog{Title=SoundstageIR.Core.TextCatalog.T("T1D0050B1F3"),Filter="FFmpeg|ffmpeg.exe",CheckFileExists=true};
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
            AudioToolsAvailable=true;AudioToolStatus=SoundstageIR.Core.TextCatalog.T("TC97E37250F");
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
        {AudioToolStatus+=SoundstageIR.Core.TextCatalog.T("T7A9E8E6A07")+ex.Message;Notify(nameof(AudioToolStatus));}
    }
    public async Task<bool> CheckAudioToolsAsync()
    {
        if(!CanConfigureAudioTools)return false;
        CheckingAudioTools=true;AudioToolStatus=SoundstageIR.Core.TextCatalog.T("T2DFB51E4D3");Notify("");CommandManager.InvalidateRequerySuggested();
        try
        {
            var tools=AudioRenderer.ResolveTools(SelectedFfmpegPath);
            string version=await Task.Run(()=>AudioRenderer.CheckToolsAsync(tools));
            FfmpegPathDisplay=tools.Ffmpeg;AudioToolsAvailable=true;
            AudioToolStatus=SoundstageIR.Core.TextCatalog.T("TCDE10AB9D2")+version+SoundstageIR.Core.TextCatalog.T("T4A6E30DE21");return true;
        }
        catch(Exception ex){AudioToolsAvailable=false;AudioToolStatus=SoundstageIR.Core.TextCatalog.T("T73747D1C2E")+ex.Message;return false;}
        finally{CheckingAudioTools=false;Notify("");CommandManager.InvalidateRequerySuggested();}
    }
}
