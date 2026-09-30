using System.Security.Cryptography;
using System.Text;
namespace SoundstageIR.Core;
public enum Distribution { Ring, Sphere, Hemisphere, Front, Pair }
public sealed class PresetOptions
{
    public double Rt { get; set; }
    public double ExtraPath { get; set; }
    public double MixingPath { get; set; }
    public double WetPercent { get; set; }
    public double AirDistance { get; set; }
    public bool AirAbsorption { get; set; }
    public int ClampForEditing()
    {
        int n=0;double C(double v,double a,double b){double r=EditingLimits.Clamp(v,a,b);if(r!=v)n++;return r;}
        Rt=C(Rt,EditingLimits.MinRt,EditingLimits.MaxRt);ExtraPath=C(ExtraPath,0,EditingLimits.MaxPath);
        MixingPath=C(MixingPath,0,EditingLimits.MaxPath);WetPercent=C(WetPercent,0,100);AirDistance=C(AirDistance,0,10000);return n;
    }
    public void Validate()
    {
        Excitation.Range(Rt,EditingLimits.MinRt,EditingLimits.MaxRt,"中频尾长参考");
        Excitation.Range(ExtraPath,0,EditingLimits.MaxPath,"首反射额外路程");Excitation.Range(MixingPath,0,EditingLimits.MaxPath,"扩散建立路程");
        Excitation.Range(WetPercent,0,100,"混响能量占比");Excitation.Range(AirDistance,0,10000,"空气吸收距离");
    }
}
public sealed record Preset(string Name,string Description,int Directions,Distribution Layout,double Rt,double ExtraPath,double MixingPath,double WetPercent,double AirDistance)
{
    // Separate integrated energy and decay profiles; neither is inferred from localization.
    public double[] Spectrum {get;init;}=[0,0,0,0,-2,-6,-14];
    public double[] Tail {get;init;}=[1,1,1,1,.85,.65,.4];
    public string? BaselineResource {get;init;}
    public bool AirAbsorptionDefault {get;init;}
    public Knot[]? EnvelopeShape {get;init;}
    static readonly double[] Frequencies=[20,80,250,1000,4000,8000,20000];
    public string Details => Directions==0?(WetPercent==0?"双音箱直达声 · 混响 0%":"直达声起点 · 自定义反射方向"):$"{Directions} 方向 · 尾长 {Rt:0.##} s · 混响 {WetPercent:0.#}%";
    public PresetOptions Defaults()=>new(){Rt=Rt,ExtraPath=ExtraPath,MixingPath=MixingPath,WetPercent=WetPercent,AirDistance=AirDistance,AirAbsorption=AirAbsorptionDefault};
    public Project Create(PresetOptions? options=null)
    {
        var o=options??Defaults();o.ClampForEditing();o.Validate();
        if(BaselineResource is not null)return CreateFromBaseline(o);
        var p=new Project{HeadModel=HeadModelKind.Fabian,Name=Name,TemplateName=Name,ReflectionEnergyPercent=o.WetPercent,Direct=new(){AirAbsorptionDistance=o.AirDistance,AirAbsorption=o.AirAbsorption}};
        if(Directions==0)return p;
        p.Sources=Templates.Create(Layout,Directions,0,0,Layout==Distribution.Front?60:120,new Excitation(),Name);
        bool hall=Rt>=1,compact=Rt<=.3;
        for(int i=0;i<p.Sources.Count;i++)
        {
            var source=p.Sources[i];source.Id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("SFS1:"+Name+":"+i)).AsSpan(0,16));
            double az=Math.Abs(source.Azimuth),height=Math.Abs(Math.Sin(source.Elevation*Math.PI/180));
            double rear=Smooth((az-95)/65),front=1-Smooth((az-25)/55);
            // Shared 0 dB reference. Main front/side directions stay exactly 0 dB;
            // rear and elevated arrivals are selectively quieter, never rebased to a source maximum.
            double weight=(az>115?(hall?-6:-3):0)+(Math.Abs(source.Elevation)>40?-3:0);
            double rtScale=compact?1-.12*front:1-(hall?.42:.28)*front-.1*(1-front)*(1-rear);
            double localRt=Rt*rtScale;
            double highLoss=(hall?2:1)*rear;
            double extra=Math.Min(EditingLimits.MaxPath,ExtraPath*(1+.2*rear+.15*height));
            double mix=Math.Min(EditingLimits.MaxPath,MixingPath*(1-.3*front+.15*rear+.1*height));
            var e=new Excitation
            {
                GainDb=weight,FirstReflectionExtraPath=extra,MixingPath=mix,
                Energy=Frequencies.Select((f,j)=>new Knot(f,Spectrum[j]-highLoss*Smooth(Math.Log2(Math.Max(1000,f)/1000)/4))).ToList(),
                Decay=Frequencies.Select((f,j)=>new Knot(f,Bound(localRt*Tail[j]))).ToList(),
                Envelope=EnvelopeShape?.Select(k=>new Knot(k.X,k.Y)).ToList() ?? (hall
                    ? [new(-1,0),new(-.55,-.7),new(0,-2-2*front),new(.18,-16),new(.55,-39),new(1,-60)]
                    : [new(-1,0),new(0,-1),new(1,-60)])
            };
            source.Left=e;source.Right=e.Clone();source.RightGainDb=-3;source.RightTilt=0;
            string role=Math.Abs(source.Elevation)>40?"上方":az>115?"后侧":az<55?"前侧":"侧向";
            source.Name=$"{role} {i+1:00}";
        }
        RoundDefaults(p);
        foreach(var source in p.Sources)foreach(var e in new[]{source.Left,source.Right})
        {
            if(o.Rt!=Rt)foreach(var k in e.Decay)k.Y=Bound(k.Y*(o.Rt/Rt));
            if(o.ExtraPath!=ExtraPath)e.FirstReflectionExtraPath=Math.Clamp(ExtraPath>0?e.FirstReflectionExtraPath*o.ExtraPath/ExtraPath:o.ExtraPath,0,EditingLimits.MaxPath);
            if(o.MixingPath!=MixingPath)e.MixingPath=Math.Clamp(MixingPath>0?e.MixingPath*o.MixingPath/MixingPath:o.MixingPath,0,EditingLimits.MaxPath);
        }
        p.TemplateSources=ProjectIO.Clone(p.Sources);p.Validate();return p;
    }
    static void RoundDefaults(Project p)
    {
        foreach(var s in p.Sources)
        {
            s.Azimuth=Math.Round(s.Azimuth,3);s.Elevation=Math.Round(s.Elevation,3);
            foreach(var e in new[]{s.Left,s.Right})
            {
                e.FirstReflectionExtraPath=Math.Round(e.FirstReflectionExtraPath,3);
                e.MixingPath=Math.Round(e.MixingPath,3);
                foreach(var curve in new[]{e.Energy,e.Decay,e.Envelope!})foreach(var k in curve)
                {k.X=Math.Round(k.X,3);k.Y=Math.Round(k.Y,3);}
            }
        }
    }
    Project CreateFromBaseline(PresetOptions o)
    {
        using var stream=typeof(Preset).Assembly.GetManifestResourceStream(BaselineResource!)
            ?? throw new InvalidOperationException("缺少内置模板数据。");
        using var reader=new StreamReader(stream);
        var p=ProjectIO.Deserialize(reader.ReadToEnd());
        p.Name=Name;p.TemplateName=Name;p.ReflectionEnergyPercent=o.WetPercent;
        p.Direct.AirAbsorption=o.AirAbsorption;p.Direct.AirAbsorptionDistance=o.AirDistance;
        foreach(var source in p.Sources)foreach(var e in new[]{source.Left,source.Right})
        {
            // Identity settings preserve the authored floating-point values and random source IDs.
            if(o.Rt!=Rt)foreach(var k in e.Decay)k.Y=Bound(k.Y*(o.Rt/Rt));
            if(o.ExtraPath!=ExtraPath)e.FirstReflectionExtraPath=Math.Clamp(e.FirstReflectionExtraPath*(o.ExtraPath/ExtraPath),0,EditingLimits.MaxPath);
            if(o.MixingPath!=MixingPath)e.MixingPath=Math.Clamp(e.MixingPath*(o.MixingPath/MixingPath),0,EditingLimits.MaxPath);
        }
        if(o.Rt!=Rt||o.ExtraPath!=ExtraPath||o.MixingPath!=MixingPath)p.TemplateSources=ProjectIO.Clone(p.Sources);
        p.Validate();return p;
    }
    static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    static double Bound(double rt)=>Math.Clamp(rt,EditingLimits.MinRt,EditingLimits.MaxRt);
}
public static class Presets
{
    public static readonly Preset[] BuiltIn=[
        new("紧凑监听","宽频短尾，平坦低中频、柔和高频滚降；轻量近场起点。",6,Distribution.Ring,.20,.35,.8,8,3)
            {Spectrum=[0,0,0,0,-1.5,-5,-13],Tail=[1,1,1,1,.85,.65,.4]},
        new("宽阔监听","（推荐）轻量混响、柔和反射起点与舒展尾部，适合日常听音乐。",8,Distribution.Ring,.78,.28,1.25,8,5)
            {BaselineResource="SoundstageIR.Core.WideMonitor.json",AirAbsorptionDefault=true,
             Spectrum=[0,0,0,-.5,-2.5,-7,-16],Tail=[1.05,1.05,1.03,1,.9,.72,.45]},
        new("前向空间","近乎平坦的宽频短反射，少量前向空间；高频轻吸收。",6,Distribution.Front,.24,.3,.7,9,3)
            {Spectrum=[0,0,0,0,-.5,-2.5,-8],Tail=[1,1,1,1,.95,.8,.55]},
        new("温暖环绕","从低频向高频连续吸收；较长的低频尾部与柔和高频。",12,Distribution.Ring,.55,.55,2.5,28,4)
            {Spectrum=[0,0,-.3,-1.5,-6,-11,-21],Tail=[1.12,1.12,1.07,1,.8,.6,.35]},
        new("明亮短厅","较宽的反射频带与紧凑尾部；亮度和尾长分别设定。",8,Distribution.Sphere,.48,.6,3,32,6)
            {Spectrum=[0,0,0,0,-.7,-3,-9],Tail=[1,1,1,1,.93,.8,.5]},
        new("上方包围","反射环绕头部上方，宽频尾部逐渐向高频衰减。",12,Distribution.Hemisphere,.62,.65,3.5,24,8)
            {Spectrum=[0,0,0,-.3,-2.5,-7,-16],Tail=[1,1,1,1,.88,.7,.42]},
        new("控制室","清晰直接的声像，克制的早段反射与轻量短尾；宽频吸收平滑过渡。",8,Distribution.Sphere,.22,.65,1.0,5,3)
            {Spectrum=[0,0,-.5,-1,-3,-7,-15],Tail=[1.15,1.15,1.08,1,.8,.6,.35],
             EnvelopeShape=[new(-1,-2),new(0,-6),new(.08,-20),new(.35,-36),new(1,-60)]},
        new("自由场","两只虚拟音箱经人头到达双耳；混响关闭，保留可调音箱角度与空间 EQ。",0,Distribution.Ring,.2,0,0,0,3),
        new("柔和音乐厅","混响占主体，低频略长，密集早段之后温和消退。",12,Distribution.Sphere,1.35,.75,6,65,12)
            {Spectrum=[0,0,-.2,-1,-4.5,-10,-22],Tail=[1.18,1.18,1.10,1,.85,.63,.34]},
        new("悠长大厅","远座位的混响比例；宽低频平台、较长低音与逐渐变暗的长尾。",12,Distribution.Sphere,2.3,.85,9,80,20)
            {Spectrum=[0,0,-.4,-1.5,-6,-12,-25],Tail=[1.22,1.22,1.12,1,.8,.55,.28]}];
    public static readonly Preset Blank=new("空白模板","从直达声开始，逐步添加自己的反射源。",0,Distribution.Ring,.2,.5,1,16,3);
    public static IEnumerable<Preset> All => new[]{Blank}.Concat(BuiltIn);
}
public static class Templates
{
    public static List<ReflectionPair> Create(Distribution kind,int total,double azOffset,double elevation,double aperture,Excitation template,string group)
    {
        if(total<2||total>32||total%2!=0)throw new ArgumentException("模板方向数量为 2–32 之间的偶数。");
        var output=new List<ReflectionPair>();int pairs=total/2;
        for(int i=0;i<pairs;i++)
        {
            double az,el=elevation;
            if(kind==Distribution.Ring)az=-180*(i+.5)/pairs;
            else if(kind==Distribution.Front)az=-Math.Clamp(aperture,2,180)*(i+.5)/pairs;
            else if(kind==Distribution.Pair)az=-Math.Clamp(aperture*(i+1)/pairs,1,179);
            else { double y=kind==Distribution.Hemisphere?(i+.5)/pairs:1-2*(i+.5)/pairs; el=Math.Asin(y)*180/Math.PI;az=-180*((i*.6180339887498949+.25)%1); }
            az=Fold(az+azOffset);el=Math.Clamp(el+((kind==Distribution.Sphere||kind==Distribution.Hemisphere)?elevation:0),-89.9,89.9);
            // Keep requested paired direction count; median points can be edited explicitly later.
            az=Math.Clamp(az,-179.9,-.1);
            var a=template.Clone();
            output.Add(new(){Name=$"{group} {i+1:00}",Group=group,Azimuth=az,Elevation=el,Left=a,Right=a.Clone()});
        }
        return output;
    }
    public static double Fold(double az){az=((az+180)%360+360)%360-180;return -Math.Abs(az);}
}
