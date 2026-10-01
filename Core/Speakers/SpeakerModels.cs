using System.Text.Json;
using System.Text.Json.Serialization;
namespace SoundstageIR.Core.Speakers;
public enum SpeakerMode { SimpleReverb, SpatialField }
public sealed class SpeakerPosition
{
    public double Azimuth {get;set;}
    public double Elevation {get;set;}
    public double Distance {get;set;}=1.7;
    public void Validate(){Excitation.Range(Azimuth,-180,180,"Azimuth / 方位角");Excitation.Range(Elevation,-90,90,"Elevation / 仰角");Excitation.Range(Distance,.2,50,"Distance / 距离 (m)");}
}
public sealed class SpeakerProject
{
    public int Version {get;set;}=1;
    [JsonConverter(typeof(JsonStringEnumConverter))] public SpeakerMode Mode {get;set;}
    public Project Field {get;set;}=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听").Create();
    public Excitation Left {get;set;}=new();
    public Excitation Right {get;set;}=new();
    public bool LinkParameters {get;set;}=true;
    public Guid NoiseId {get;set;}=Guid.NewGuid();
    public SpeakerPosition LeftSpeaker {get;set;}=new(){Azimuth=-30};
    public SpeakerPosition RightSpeaker {get;set;}=new(){Azimuth=30};
    public bool AirAbsorption {get;set;}=true;
    public double MaximumInverseGainDb {get;set;}=12;
    public void Validate()
    {
        if(Version!=1||!Enum.IsDefined(Mode))throw new ArgumentException("Unsupported speaker project / 音箱配置版本不受支持");
        var check=ProjectIO.Clone(Field);if(Mode==SpeakerMode.SimpleReverb){check.Sources=[];check.TemplateSources=[];check.Direct.Enabled=true;}check.Validate();Left.Validate();if(!LinkParameters)Right.Validate();LeftSpeaker.Validate();RightSpeaker.Validate();
        Excitation.Range(MaximumInverseGainDb,0,36,"Inverse gain / 求逆增益 (dB)");
    }
    public static SpeakerProject FromPreset(Preset preset,SpeakerMode mode,Project? field=null)
    {
        var p=field??preset.Create();var e=p.Sources.FirstOrDefault()?.Left.Clone()??new Excitation();
        if(mode==SpeakerMode.SimpleReverb&&preset==Presets.Blank)p.ReflectionEnergyPercent=0;
        e.GainDb=0;return new(){Mode=mode,Field=p,Left=e,Right=e.Clone()};
    }
    public static SpeakerProject Load(string path)
    {
        var p=JsonSerializer.Deserialize<SpeakerProject>(File.ReadAllText(path),ProjectIO.Options)??throw new InvalidDataException("Empty project / 空配置");p.Validate();return p;
    }
    public void Save(string path){Validate();File.WriteAllText(path,ProjectIO.Serialize(this));}
}
public sealed record SpeakerResult(SpeakerProject Project,double[][] Drive,double[][] Target,double[][] Predicted,
    double[][] Playback,double[][] Inverse,int LatencySamples,double InverseProjectionDb,double RelativeErrorDb,
    double MaximumDriveGainDb,double WetPercent,double ZeroSample,List<string> Warnings)
{
    public InverseResponse? InverseResponse {get;init;}
    public GenerationResult? TargetAnalysis {get;init;}
    public GenerationResult? PlaybackAnalysis {get;init;}
    public SpeakerRiskReport? Checks {get;init;}
    public int SampleRate=>Project.Field.SampleRate;
    public double Duration=>Drive.Max(h=>h.Length)/(double)SampleRate;
    // The file renderer accepts row-major 2x2 matrices; here rows are speakers, not ears.
    public GenerationResult ForAudio()=>new(){Project=Project.Field,OutputRoutes=SpeakerExporter.Routes,ExportProject=Project,ZeroSample=ZeroSample,Raw=Drive,Kernels=Drive,EarEq=[[1],[1]],SecondEq=[1],Bandpass=[1],Contributions=[],DirectPaths=Drive};
}
