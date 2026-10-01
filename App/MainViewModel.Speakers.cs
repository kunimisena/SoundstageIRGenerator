using System.IO;
using System.Windows.Input;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public sealed partial class MainViewModel
{
    public SpeakerProject? Speaker {get;private set;}
    public SpeakerResult? SpeakerResult {get;private set;}
    public bool IsSpeaker=>Speaker!=null;
    public bool SimpleSpeaker=>Speaker?.Mode==SpeakerMode.SimpleReverb;
    public bool SpatialSpeaker=>Speaker?.Mode==SpeakerMode.SpatialField;
    public bool DirectionalControls=>!SimpleSpeaker;
    public string SpeakerModeLabel=>TextCatalog.T(SimpleSpeaker?"Speaker.Simple":"Speaker.Spatial");
    public bool LinkSpeakerKernels {get=>Speaker?.LinkParameters??true;set{if(Speaker==null||Busy||value==Speaker.LinkParameters)return;Speaker.LinkParameters=value;Commit();}}
    public string SpeakerEnergySummary=>TextCatalog.English
        ?$"{(SimpleSpeaker?"Speaker-drive":SpeakerResult!.BandResult!=null?"Predicted-ear":"Target-ear")} wet energy: {SpeakerResult!.WetPercent:0.0}%"
        :$"{(SimpleSpeaker?"音箱驱动":SpeakerResult!.BandResult!=null?"预测耳端":"目标耳端")}混响能量：{SpeakerResult!.WetPercent:0.0}%";
    public string SpeakerMetrics=>SpeakerResult is not {} r?"":TextCatalog.English
        ?$"Kernel: {r.Duration:0.000} s · {r.SampleRate} Hz\nInverse delay: {r.LatencySamples*1000.0/r.SampleRate:0.00} ms\nRelative reconstruction error: {r.RelativeErrorDb:0.00} dB\nProjection residual: {r.InverseProjectionDb:0.00} dB\n{SpeakerEnergySummary}\n{GenerationNotes}"
        :$"核长：{r.Duration:0.000} s · {r.SampleRate} Hz\n求逆共同延迟：{r.LatencySamples*1000.0/r.SampleRate:0.00} ms\n相对还原误差：{r.RelativeErrorDb:0.00} dB\n投影残差：{r.InverseProjectionDb:0.00} dB\n{SpeakerEnergySummary}\n{GenerationNotes}";
    public void InitializeSpeaker(SpeakerMode mode)
    {
        Speaker=SpeakerProject.FromPreset(Presets.BuiltIn.Single(p=>p.Name=="宽阔监听"),mode,ProjectIO.Clone(P));
        VisualChanged+=()=>{_=UpdateSpeakerPrecheckAsync();};
        ExportParent=Path.Combine(Root,"SpeakerExports");snapshot=SerializeState();LoadCards();Notify("");
    }
    public void SetSpeakerProject(SpeakerProject project)
    {
        project.Validate();undo.Push(snapshot);redo.Clear();Speaker=project;P=project.Field;Result=null;SpeakerResult=null;
        LastExport="";pendingEdits=false;Stale=true;AnalysisCache.Clear();SpeakerAnalysis.Clear();ProjectFile="";
        snapshot=SerializeState();RefreshSources();LoadCards();Notify("");VisualChanged?.Invoke();
    }
    void ApplyTemplateProject(Project p)
    {
        if(Speaker==null){SetProject(p);return;}
        var next=ProjectIO.Clone(Speaker);next.Field=p;next.OutsideBand=OutsideReverb.Create(p,next.InverseLowHz,next.InverseHighHz);
        if(SimpleSpeaker){var e=p.Sources.FirstOrDefault()?.Left.Clone()??new Excitation();e.GainDb=0;next.Left=e;next.Right=e.Clone();}
        SetSpeakerProject(next);
    }
    string SerializeState(){if(Speaker==null)return ProjectIO.Serialize(P);Speaker.Field=P;return ProjectIO.Serialize(Speaker);}
    void RestoreState(string json)
    {
        if(Speaker==null)P=ProjectIO.Deserialize(json);
        else{Speaker=System.Text.Json.JsonSerializer.Deserialize<SpeakerProject>(json,ProjectIO.Options)!;P=Speaker.Field;}
    }
    void ValidateState(){if(Speaker==null)P.Validate();else{Speaker.Field=P;Speaker.Validate();}}
    void RefreshSpeakerResultIdentity()
    {
        if(SpeakerResult==null){Stale=true;return;}
        Speaker!.Field=P;var compare=ProjectIO.Clone(Speaker);compare.Field.Name=SpeakerResult.Project.Field.Name;compare.Field.TemplateName=SpeakerResult.Project.Field.TemplateName;
        Stale=ProjectIO.Serialize(compare)!=ProjectIO.Serialize(SpeakerResult.Project);
        if(!stale){SpeakerResult.Project.Field.Name=P.Name;SpeakerResult.Project.Field.TemplateName=P.TemplateName;}
    }
    public void SaveProjectFile(string file){ValidateState();if(Speaker==null)ProjectIO.Save(P,file);else Speaker.Save(file);}
    public void LoadProjectFile(string file){if(Speaker==null)SetProject(ProjectIO.Load(file));else SetSpeakerProject(SpeakerProject.Load(file));}
    public void MirrorSpeakerPosition(){if(Speaker==null)return;Speaker.RightSpeaker=ProjectIO.Clone(Speaker.LeftSpeaker);Speaker.RightSpeaker.Azimuth=-Speaker.LeftSpeaker.Azimuth;Commit();}
    void RefreshSpeakerLabels(){Notify(nameof(SpeakerModeLabel));Notify(nameof(SpeakerEnergySummary));Notify(nameof(SpeakerMetrics));CheckNotify();}
    public SpeakerAnalysisCache SpeakerAnalysis {get;}=new();
    public Task<PlotData> GetAnalysisAsync(int subject,int kind,bool smooth,bool bandpass)=>SpeakerResult is {} speaker
        ?SpeakerAnalysis.GetAsync(speaker,Selected?.Id,subject,kind,smooth,bandpass)
        :AnalysisCache.GetAsync(Result!,Selected?.Id,subject,kind,smooth,bandpass);
}
