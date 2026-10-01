using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundstageIR.Core;

public sealed class Knot
{
    public double X { get; set; }
    public double Y { get; set; }
    public Knot() { }
    public Knot(double x, double y) { X = x; Y = y; }
}
public sealed class Excitation
{
    public double GainDb { get; set; }
    public double TiltDbPerOct { get; set; }
    public const double SoundSpeed = 343;
    public double FirstReflectionExtraPath { get; set; } = .5;
    public double MixingPath { get; set; } = 1;
    [JsonIgnore] public double OnsetMs { get => FirstReflectionExtraPath / SoundSpeed * 1000; set => FirstReflectionExtraPath = value * SoundSpeed / 1000; }
    [JsonIgnore] public double BuildMs { get => MixingPath / SoundSpeed * 1000; set => MixingPath = value * SoundSpeed / 1000; }
    public List<Knot> Energy { get; set; } = [new(80,0), new(1000,0), new(8000,-6)];
    public List<Knot> Decay { get; set; } = [new(80,.18), new(1000,.18), new(8000,.12)];
    // -1 = first reflection; 0 = mixing marker; 1 = reference tail length.
    public List<Knot> Envelope { get; set; } = [new(-1,0), new(0,-1), new(1,-60)];
    public double EnvelopeDistance(double u) => FirstReflectionExtraPath + (u < 0 ? (u+1)*MixingPath : MixingPath+u*Rt(1000)*SoundSpeed);
    public double EnvelopeCoordinate(double metres) => metres < FirstReflectionExtraPath+MixingPath && MixingPath>0
        ? (metres-FirstReflectionExtraPath)/MixingPath-1 : (metres-FirstReflectionExtraPath-MixingPath)/(Rt(1000)*SoundSpeed);
    public double EnergyDb(double f) => Curves.At(Energy,f,true,false) + GainDb + TiltDbPerOct*Math.Log2(Math.Max(20,f)/1000);
    public double Rt(double f) => Curves.At(Decay,f,true,true);
    public Excitation Clone() => ProjectIO.Clone(this);
    public void Validate()
    {
        Range(GainDb,-120,60,"源增益"); Range(TiltDbPerOct,-24,24,"频谱 tilt");
        Range(FirstReflectionExtraPath,0,EditingLimits.MaxPath,"首反射额外路程"); Range(MixingPath,0,EditingLimits.MaxPath,"扩散建立路程");
        Curves.Validate(Energy,20,20000,-120,60,false,"能量曲线");
        Curves.Validate(Decay,20,20000,EditingLimits.MinRt,EditingLimits.MaxRt,false,"衰减时间");
        if(Envelope is not {} curve)throw new ArgumentException("缺少完整包络。");
        else
        {
            Curves.Validate(curve,-1,1,-120,24,false,"完整能量包络");
            if(curve[0].X!=-1 || curve[^1].X!=1 || curve[^1].Y!=-60 || !curve.Any(k=>k.X==0))
                throw new ArgumentException("完整包络须保留首反射、建立标记和末端控制点；末端为 −60 dB。");
            if(curve[^2].Y<=-60)throw new ArgumentException("完整包络最后一段须下降至 −60 dB。");
        }
    }
    public static void Range(double v,double min,double max,string label)
    { if(!double.IsFinite(v)||v<min||v>max) throw new ArgumentException($"{label}须在 {min} 至 {max} 之间。"); }
}
public sealed class ReflectionPair
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "反射源";
    public string Group { get; set; } = "环绕";
    public bool Enabled { get; set; } = true;
    public double Azimuth { get; set; } = -45;
    public double Elevation { get; set; }
    public bool AutoRight { get; set; } = true;
    public double RightGainDb { get; set; } = -3;
    public double RightTilt { get; set; }
    public Excitation Left { get; set; } = new();
    public Excitation Right { get; set; } = new();
    [JsonIgnore] public bool Median => Math.Abs(Math.Sin(Azimuth*Math.PI/180)*Math.Cos(Elevation*Math.PI/180)) < 1e-9;
    [JsonIgnore] public int Multiplicity => Median ? 1 : 2;
    [JsonIgnore] public string Caption => $"{(Enabled?"●":"○")} {Name}   {Azimuth:0.#}° / {Elevation:0.#}°";
    public Excitation EffectiveRight()
    {
        if(Median) return Left.Clone();
        if(!AutoRight) return Right.Clone();
        var e=Left.Clone(); e.GainDb+=RightGainDb; e.TiltDbPerOct+=RightTilt; return e;
    }
    public ReflectionPair CloneIndependent() { var s=ProjectIO.Clone(this); s.Id=Guid.NewGuid(); s.Name+=" 副本"; return s; }
}
public sealed class DirectSettings
{
    public bool Enabled { get; set; } = true;
    public double Angle { get; set; } = 30;
    public double Elevation { get; set; }
    public double AirAbsorptionDistance { get; set; } = 3;
    public bool AirAbsorption { get; set; }
}
public sealed class Project
{
    public int Version { get; set; } = 5;
    public string Name { get; set; } = "空白模板";
    public string TemplateName { get; set; } = "空白模板";
    public List<ReflectionPair> TemplateSources { get; set; } = [];
    public int SampleRate { get; set; } = 48000;
    public long Seed { get; set; } = 20260920;
    public bool StrictMirror { get; set; }
    public bool Equalize { get; set; } = true;
    public int Smooth1 { get; set; } = 12;
    public int Smooth2 { get; set; } = 3;
    public double EarEqStrengthPercent { get; set; } = 100;
    public double CenterEqStrengthPercent { get; set; } = 0;
    public double ReflectionEnergyPercent { get; set; } = 5;
    public double OutputDb { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HeadModelKind HeadModel { get; set; } = HeadModelKind.Sphere;
    public bool FabianCtfCompensation { get; set; } = true;
    public double HeadRadius { get; set; } = .0875;
    public double HeadShadow { get; set; } = 1;
    public DirectSettings Direct { get; set; } = new();
    public List<ReflectionPair> Sources { get; set; } = [];
    [JsonIgnore] public int DirectionCount => Sources.Where(s=>s.Enabled).Sum(s=>s.Multiplicity);
    public void Validate()
    {
        if(Version!=5) throw new ArgumentException("此项目版本不受支持。");
        if(SampleRate!=44100&&SampleRate!=48000&&SampleRate!=96000) throw new ArgumentException("采样率须为 44100、48000 或 96000。");
        if(Sources.Count>32 || Sources.Sum(s=>s.Multiplicity)>32 || TemplateSources.Count>32 || TemplateSources.Sum(s=>s.Multiplicity)>32) throw new ArgumentException("总方向数不能超过 32。");
        if(Sources.Select(s=>s.Id).Distinct().Count()!=Sources.Count || TemplateSources.Select(s=>s.Id).Distinct().Count()!=TemplateSources.Count) throw new ArgumentException("源 ID 必须唯一。");
        if(!Direct.Enabled&&!Sources.Any(s=>s.Enabled)) throw new ArgumentException("至少启用直达声或一个反射源。");
        if(!Enum.IsDefined(HeadModel)) throw new ArgumentException("未知人头模型。");
        Excitation.Range(HeadRadius,.05,.12,"头部半径"); Excitation.Range(HeadShadow,0,2,"头部遮挡");
        Excitation.Range(Smooth1,1,24,"一级平滑分母"); Excitation.Range(Smooth2,1,24,"二级平滑分母");
        Excitation.Range(EarEqStrengthPercent,0,100,"一级 EQ 强度");Excitation.Range(CenterEqStrengthPercent,0,100,"二级合成 EQ 强度");
        Excitation.Range(ReflectionEnergyPercent,0,100,"混响能量占比");
        Excitation.Range(OutputDb,-36,18,"输出增益");
        Excitation.Range(Direct.Angle,0,180,"音箱方位角"); Excitation.Range(Direct.Elevation,-90,90,"音箱仰角");
        Excitation.Range(Direct.AirAbsorptionDistance,0,10000,"空气吸收距离");
        foreach(var s in Sources.Concat(TemplateSources))
        {
            Excitation.Range(s.Azimuth,-180,0,"左侧方位角"); Excitation.Range(s.Elevation,-90,90,"仰角");
            Excitation.Range(s.RightGainDb,-120,60,"R 相对增益"); Excitation.Range(s.RightTilt,-24,24,"R tilt");
            s.Left.Validate(); s.Right.Validate();
        }
    }
}
public static class ProjectIO
{
    public static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Options);
    public static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(Serialize(value),Options)!;
    public static Project Deserialize(string json)
    {
        var p=JsonSerializer.Deserialize<Project>(json,Options)??throw new ArgumentException("空项目。");
        p.Validate();return p;
    }

    public static Project Load(string path)=>Deserialize(File.ReadAllText(path));
    public static void Save(Project p,string path) { p.Validate(); File.WriteAllText(path,Serialize(p)); }
}

public static class Curves
{
    public static void Validate(List<Knot> a,double xmin,double xmax,double ymin,double ymax,bool decreasing,string name)
    {
        if(a.Count<2||a.Count>EditingLimits.MaxKnots) throw new ArgumentException($"{name}需要 2–256 个控制点。");
        for(int i=0;i<a.Count;i++)
        {
            Excitation.Range(a[i].X,xmin,xmax,name+" 横坐标");Excitation.Range(a[i].Y,ymin,ymax,name+" 纵坐标");
            if(i>0&&(a[i].X<=a[i-1].X || decreasing&&a[i].Y>a[i-1].Y)) throw new ArgumentException($"{name}控制点必须按横坐标递增，衰减形状须不升高。");
        }
    }
    public static double At(IReadOnlyList<Knot> a,double x,bool logX=false,bool logY=false)
    {
        if(x<=a[0].X)return a[0].Y;if(x>=a[^1].X)return a[^1].Y;
        int n=a.Count; Span<double> xx=stackalloc double[n], yy=stackalloc double[n], d=stackalloc double[n-1], h=stackalloc double[n-1], m=stackalloc double[n];
        for(int i=0;i<n;i++){xx[i]=logX?Math.Log(a[i].X):a[i].X; yy[i]=logY?Math.Log(a[i].Y):a[i].Y;}
        for(int i=0;i<n-1;i++){h[i]=xx[i+1]-xx[i];d[i]=(yy[i+1]-yy[i])/h[i];}
        if(n==2){m[0]=m[1]=d[0];} else {
            m[0]=End(h[0],h[1],d[0],d[1]);m[n-1]=End(h[n-2],h[n-3],d[n-2],d[n-3]);
            for(int i=1;i<n-1;i++)m[i]=d[i-1]*d[i]<=0?0:(3*(h[i-1]+h[i]))/((2*h[i]+h[i-1])/d[i-1]+(h[i]+2*h[i-1])/d[i]);
        }
        double z=logX?Math.Log(x):x;int k=0;while(k<n-2&&z>xx[k+1])k++;
        double t=(z-xx[k])/h[k],t2=t*t,t3=t2*t;
        double v=(2*t3-3*t2+1)*yy[k]+(t3-2*t2+t)*h[k]*m[k]+(-2*t3+3*t2)*yy[k+1]+(t3-t2)*h[k]*m[k+1];
        return logY?Math.Exp(v):v;
    }
    static double End(double h0,double h1,double d0,double d1)
    {double m=((2*h0+h1)*d0-h0*d1)/(h0+h1);if(Math.Sign(m)!=Math.Sign(d0))return 0;if(Math.Sign(d0)!=Math.Sign(d1)&&Math.Abs(m)>3*Math.Abs(d0))return 3*d0;return m;}
}
