using System.Numerics;
namespace StatisticalField.Core;
public readonly record struct HeadFilter(double B0,double B1,double A1,double Delay);
public static class HeadModel
{
    public const double C=343;
    public static HeadFilter At(double az,double el,int ear,Project p)
    {
        double x=Math.Sin(az*Math.PI/180)*Math.Cos(el*Math.PI/180);
        double theta=Math.Acos(Math.Clamp((ear==0?-1:1)*x,-1,1));return AtAngle(theta,p);
    }
    public static HeadFilter AtAngle(double theta,Project p)
    {
        double w=2*C/p.HeadRadius,k=2.0*p.SampleRate,original=1.05+.95*Math.Cos(theta/(5*Math.PI/6)*Math.PI);
        double alpha=Math.Max(.015,1+p.HeadShadow*(original-1));
        double delay=p.HeadRadius/C*(theta<=Math.PI/2?-Math.Cos(theta):theta-Math.PI/2);
        return new((alpha*k+w)/(k+w),(w-alpha*k)/(k+w),(w-k)/(w+k),delay);
    }
    public static double[] Impulse(HeadFilter q,int sr)
    {
        var h=new double[(int)Math.Ceiling(.006*sr)];h[0]=q.B0;for(int n=1;n<h.Length;n++)h[n]=(n==1?q.B1:0)-q.A1*h[n-1];return h;
    }
    public static double[] Apply(double[] kernel,HeadFilter q,int sr)
    {
        var output=new double[kernel.Length+(int)Math.Ceiling(.006*sr)];double last=0,prev=0;
        for(int i=0;i<output.Length;i++){double x=i<kernel.Length?kernel[i]:0;double y=q.B0*x+q.B1*prev-q.A1*last;output[i]=y;prev=x;last=y;}return output;
    }
    // ISO 9613-1 / ECMA-108 Annex A: humidity h and RH are percentages (50, not 0.5).
    // Fixed 20 C, 50% RH and standard pressure; attenuation in dB/m.
    public static double AirDbPerMetre(double f)
    {
        const double temp=293.15,refT=293.15;double h=50*Math.Pow(10,-6.8346*Math.Pow(273.16/temp,1.261)+4.6151);
        double fo=24+4.04e4*h*(.02+h)/(.391+h);
        double fn=Math.Pow(temp/refT,-.5)*(9+280*h*Math.Exp(-4.170*(Math.Pow(temp/refT,-1.0/3)-1)));
        return 8.686*f*f*(1.84e-11*Math.Sqrt(temp/refT)+Math.Pow(temp/refT,-2.5)*(.01275*Math.Exp(-2239.1/temp)/(fo+f*f/fo)+.1068*Math.Exp(-3352/temp)/(fn+f*f/fn)));
    }
    public static double[] Air(Project p)=>p.Direct.AirAbsorption?Dsp.MinimumPhase(f=>Math.Pow(10,-AirDbPerMetre(f)*p.Direct.AirAbsorptionDistance/20),p.SampleRate,512):[1.0];
}
public sealed class Contribution
{
    public Guid Id {get;init;}
    public string Name {get;init;}="";
    public double[] LeftInputKernel {get;init;}=[];
    public double[] RightInputKernel {get;init;}=[];
    public double[][] EarPaths {get;init;}=[];
}
public sealed class GenerationResult
{
    public required Project Project {get;init;}
    public required double[][] Raw {get;init;}
    public required double[][] Kernels {get;init;}
    public required double[][] EarEq {get;init;}
    public required double[] SecondEq {get;init;}
    public required double[] Bandpass {get;init;}
    public required List<Contribution> Contributions {get;init;}
    public required double[][] DirectPaths {get;init;}
    public double ReflectionPercentBeforeEq {get;init;}
    public double ReflectionPercentAfterEq {get;init;}
    public double ReflectionGain {get;init;}=1;
    public double[][] FinalDirectPaths {get;init;}=[];
    public double[][] FinalReflectionPaths {get;init;}=[];
    public double ZeroSample {get;init;}
    public double CommonGainDb {get;init;}
    public double OutputReferenceDb {get;init;}
    public string EqResidualReference {get;init;}="";
    public List<EqReport> EqReports {get;init;}=[];
    public List<string> Warnings {get;init;}=[];
    public double PeakBoundDb {get;init;}
    public double MaxBinGainDb {get;init;}
    public double Seconds {get;init;}
    public double EqResidualDb {get;init;}
    public double Duration=>Kernels[0].Length/(double)Project.SampleRate;
}
public static class Generator
{
    public static readonly string[] RouteNames=["L_to_LeftEar","R_to_LeftEar","L_to_RightEar","R_to_RightEar"];
    public static GenerationResult Generate(Project project,IProgress<(double Fraction,string Message)>? progress=null,CancellationToken ct=default)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();var p=ProjectIO.Clone(project);p.Validate();int sr=p.SampleRate;
        ct.ThrowIfCancellationRequested();var head=new HeadRenderer(p);
        var air=HeadModel.Air(p);var reference=head.At(-p.Direct.Angle,p.Direct.Elevation,0);
        var front=Dsp.Convolve(air,reference.Impulse);int peak=FirstPeak(front);
        double referenceDelay=reference.Delay+peak/(double)sr;
        // Shared lead-in covers the selected model and resampler support, independent of tail length.
        int guard=16+(int)Math.Ceiling(Math.Max(0,(referenceDelay-head.MinimumDelay)*sr));
        var direct=NewPaths(1);var sum=NewPaths(1);var contributions=new List<Contribution>();
        if(p.Direct.Enabled)
        {
            for(int input=0;input<2;input++)for(int ear=0;ear<2;ear++)
            {
                var q=head.At((input==0?-1:1)*p.Direct.Angle,p.Direct.Elevation,ear);
                var h=q.Apply(air,sr);
                AddDelayed(ref direct[ear*2+input],h,(q.Delay-referenceDelay)*sr+guard);
            }
            Accumulate(sum,direct);
        }
        var active=p.Sources.Where(s=>s.Enabled).ToList();int index=0;
        foreach(var source in active)
        {
            ct.ThrowIfCancellationRequested();progress?.Report((.75*index/Math.Max(1,active.Count),$"生成 {source.Name} · {index+1}/{active.Count}"));
            var a=NoiseKernel.Generate(source.Left,sr,p.Seed,source.Id,0,ct);
            var b=source.Median&&p.StrictMirror?a:NoiseKernel.Generate(source.EffectiveRight(),sr,p.Seed,source.Id,1,ct);
            var paths=NewPaths(1);
            Place(a,0,source.Azimuth,source.Elevation,paths);Place(b,1,source.Azimuth,source.Elevation,paths);
            if(!source.Median)
            {
                var ar=p.StrictMirror?a:NoiseKernel.Generate(source.Left,sr,p.Seed,source.Id,2,ct);
                var br=p.StrictMirror?b:NoiseKernel.Generate(source.EffectiveRight(),sr,p.Seed,source.Id,3,ct);
                Place(br,0,-source.Azimuth,source.Elevation,paths);Place(ar,1,-source.Azimuth,source.Elevation,paths);
            }
            Accumulate(sum,paths);Pad(paths);
            contributions.Add(new(){Id=source.Id,Name=source.Name,LeftInputKernel=a,RightInputKernel=b,EarPaths=paths});index++;
        }
        void Place(double[] kernel,int input,double az,double el,double[][] dest)
        {
            for(int ear=0;ear<2;ear++){ct.ThrowIfCancellationRequested();var q=head.At(az,el,ear);var y=q.Apply(kernel,sr);AddDelayed(ref dest[ear*2+input],y,(q.Delay-referenceDelay)*sr+guard);}
        }
        double reflectionGain=1;
        {
            var wet=NewPaths(1);foreach(var contribution in contributions)Accumulate(wet,contribution.EarPaths);
            reflectionGain=EnergyBalance.Mix(direct,wet,p.ReflectionEnergyPercent,sr);
            foreach(var contribution in contributions)foreach(var h in contribution.EarPaths)Scale(h,reflectionGain);
            sum=NewPaths(1);Accumulate(sum,direct);Accumulate(sum,wet);
        }
        double before=EnergyBalance.Percent(EnergyBalance.Energy(direct,sr),EnergyBalance.Energy(EnergyBalance.Subtract(sum,direct),sr));
        Pad(sum);Pad(direct);if(p.StrictMirror){sum[3]=(double[])sum[0].Clone();sum[2]=(double[])sum[1].Clone();}
        var raw=sum.Select(x=>(double[])x.Clone()).ToArray();double[][] eq=[[1.0],[1.0]];double[] eq2=[1.0];
        var reports=new List<EqReport>();var warnings=new List<string>();
        double[] Design(double[] response,int smoothing,double strength,string stage)
        {
            var design=EqDesigner.Design(response,sr,smoothing,strength,stage,ct);reports.Add(design.Report);
            var report=design.Report;
            if(report.RelativeBoostDb>12||report.RelativeCutDb< -24)warnings.Add($"{stage}：相对平均增益，最大提升 {report.RelativeBoostDb:F1} dB @ {report.BoostHz:F0} Hz，最大衰减 {report.RelativeCutDb:F1} dB @ {report.CutHz:F0} Hz。");
            if(report.DesignErrorDb>.1)warnings.Add($"{stage}：实际 EQ 与设计曲线最大相差 {report.DesignErrorDb:F2} dB（{report.Taps} 点），修正幅度未截断。");
            if(report.ResponseResidualDb>.5)warnings.Add($"{stage}：校正后平滑响应相对目标的 RMS 偏差 {report.ResponseResidualDb:F2} dB。");
            return design.Impulse;
        }
        if(p.Equalize)
        {
            progress?.Report((.79,"一级平滑最小相位 EQ"));ct.ThrowIfCancellationRequested();
            eq[0]=Design(Dsp.Sum(sum[0],sum[1]),p.Smooth1,p.EarEqStrengthPercent/100,p.StrictMirror?"一级共同 EQ":"一级左耳 EQ");
            eq[1]=p.StrictMirror?(double[])eq[0].Clone():Design(Dsp.Sum(sum[2],sum[3]),p.Smooth1,p.EarEqStrengthPercent/100,"一级右耳 EQ");
            for(int c=0;c<4;c++){ct.ThrowIfCancellationRequested();sum[c]=Dsp.Convolve(sum[c],eq[c/2]);}
            if(!p.StrictMirror)
            {
                progress?.Report((.85,"二级双耳复响应合成 EQ"));
                var left=Dsp.Sum(sum[0],sum[1]);var right=Dsp.Sum(sum[2],sum[3]);
                eq2=Design(Dsp.Sum(left,right,.5),p.Smooth2,p.CenterEqStrengthPercent/100,"二级共同 EQ");
                for(int c=0;c<4;c++){ct.ThrowIfCancellationRequested();sum[c]=Dsp.Convolve(sum[c],eq2);}
            }
        }
        progress?.Report((.90,"最终 20 Hz–20 kHz 最小相位带通"));
        ct.ThrowIfCancellationRequested();var bandpass=Dsp.OutputBandpass(sr);
        for(int c=0;c<4;c++){ct.ThrowIfCancellationRequested();sum[c]=Dsp.Convolve(sum[c],bandpass);}
        progress?.Report((.92,"共同增益与最终核分析"));Pad(sum);ct.ThrowIfCancellationRequested();
        bool combined=p.Equalize&&!p.StrictMirror&&p.CenterEqStrengthPercent>0;
        double[] ReferenceDb(double[][] paths)
        {
            var left=Dsp.Sum(paths[0],paths[1]);var right=Dsp.Sum(paths[2],paths[3]);
            return combined?Dsp.SmoothedDbAt(Dsp.Sum(left,right,.5),sr,p.Smooth2):Dsp.SmoothedDbAt(left,sr,p.Smooth1).Concat(Dsp.SmoothedDbAt(right,sr,p.Smooth1)).ToArray();
        }
        double meanDb=ReferenceDb(sum).Average();
        var output=OutputGain.Apply(sum,p.OutputDb-meanDb);
        double gainDb=output.CommonGainDb,gain=Math.Pow(10,gainDb/20),referenceDb=p.OutputDb;
        if(output.FinalPeakDb>0)warnings.Add($"最大频点路径幅度和 {output.FinalPeakDb:F1} dB；可据此设置播放余量。");
        if(output.SamplePeakBoundDb>0)warnings.Add($"短瞬态仍可能超过数字满刻度，任意输入峰值的保守上界为 {output.SamplePeakBoundDb:F1} dB；播放增益及耳机 EQ 需要相应余量。");
        var finalDirect=NewPaths(1);
        for(int c=0;c<4;c++)
        {
            ct.ThrowIfCancellationRequested();
            finalDirect[c]=Dsp.Convolve(Dsp.Convolve(Dsp.Convolve(direct[c],eq[c/2]),eq2),bandpass);Scale(finalDirect[c],gain);
        }
        var finalWet=EnergyBalance.Subtract(sum,finalDirect);
        double after=EnergyBalance.Percent(EnergyBalance.Energy(finalDirect,sr),EnergyBalance.Energy(finalWet,sr));
        double residual=Math.Sqrt(ReferenceDb(sum).Select(v=>Math.Pow(v-referenceDb,2)).Average());
        if(!double.IsFinite(residual))throw new InvalidOperationException("最终校正响应无法有效分析。");
        progress?.Report((1,"完成"));
        return new(){ReflectionPercentBeforeEq=before,ReflectionPercentAfterEq=after,ReflectionGain=reflectionGain,FinalDirectPaths=finalDirect,FinalReflectionPaths=finalWet,Project=p,Raw=raw,Kernels=sum,EarEq=eq,SecondEq=eq2,Bandpass=bandpass,Contributions=contributions,DirectPaths=direct,ZeroSample=guard,CommonGainDb=gainDb,OutputReferenceDb=referenceDb,EqResidualReference=combined?"两耳复响应平均":"每耳同相输入响应",EqReports=reports,Warnings=warnings,PeakBoundDb=output.SamplePeakBoundDb,MaxBinGainDb=output.FinalPeakDb,Seconds=watch.Elapsed.TotalSeconds,EqResidualDb=residual};
    }
    public static int FirstPeak(double[] h)
    {double max=h.Max(Math.Abs);for(int i=0;i<h.Length;i++)if(Math.Abs(h[i])>=max*.1&&(i==0||Math.Abs(h[i])>=Math.Abs(h[i-1]))&&(i==h.Length-1||Math.Abs(h[i])>=Math.Abs(h[i+1])))return i;return 0;}
    public static double[][] NewPaths(int n)=>Enumerable.Range(0,4).Select(_=>new double[n]).ToArray();
    public static void Scale(double[] x,double g){for(int i=0;i<x.Length;i++)x[i]*=g;}
    public static void Pad(double[][] a){int n=a.Max(x=>x.Length);for(int c=0;c<a.Length;c++)if(a[c].Length!=n)Array.Resize(ref a[c],n);}
    public static void Accumulate(double[][] target,double[][] source){for(int c=0;c<4;c++){if(target[c].Length<source[c].Length)Array.Resize(ref target[c],source[c].Length);for(int i=0;i<source[c].Length;i++)target[c][i]+=source[c][i];}}
    public static void AddDelayed(ref double[] dest,double[] input,double samples)
    {
        int integer=(int)Math.Floor(samples);double frac=samples-integer;const int half=16;
        if(integer-half<0)throw new InvalidOperationException("共同时间前导不足。");
        int len=integer+input.Length+half+1;if(dest.Length<len)Array.Resize(ref dest,len);
        Span<double> weights=stackalloc double[33];double sum=0;
        for(int k=-half;k<=half;k++){double x=k-frac;double sinc=Math.Abs(x)<1e-12?1:Math.Sin(Math.PI*x)/(Math.PI*x);double w=.5+.5*Math.Cos(Math.PI*k/(half+1));weights[k+half]=sinc*w;sum+=sinc*w;}
        for(int k=-half;k<=half;k++){double w=weights[k+half]/sum;for(int i=0;i<input.Length;i++)dest[integer+k+i]+=w*input[i];}
    }
}
