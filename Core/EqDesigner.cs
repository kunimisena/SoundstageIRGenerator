using System.Numerics;
namespace SoundstageIR.Core;
public sealed record EqReport(string Stage,double MaximumBoostDb,double MaximumCutDb,double RelativeBoostDb,double RelativeCutDb,double BoostHz,double CutHz,int Taps,double DesignErrorDb,double ResponseResidualDb);
public sealed record EqDesign(double[] Impulse,EqReport Report);
public static class EqDesigner
{
    public static EqDesign Design(double[] response,int sr,int denominator,double strength=1,string stage="EQ",CancellationToken ct=default)
    {
        if(!double.IsFinite(strength)||strength<0||strength>1)throw new ArgumentOutOfRangeException(nameof(strength));
        if(strength==0)return new([1.0],new(stage,0,0,0,0,0,0,1,0,0));
        var original=Dsp.SmoothedDbAt(response,sr,denominator);
        if(original.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException(stage+"：参考响应为空或频段能量无法分辨，无法设计有效 EQ。");
        var target=original.Select(v=>-v*strength).ToArray();double mean=target.Average();
        var shape=target.Select(v=>v-mean).ToArray();
        double DbAt(double f)
        {
            double x=Math.Log(Math.Clamp(f,20,20000)/20)/Math.Log(1000)*(shape.Length-1);
            int j=Math.Min(shape.Length-2,(int)x);return shape[j]+(shape[j+1]-shape[j])*(x-j);
        }
        int taps=(int)Math.Round(4096.0*sr/48000),stagnant=0;double bestError=double.PositiveInfinity;double[] best=[];
        while(true)
        {
            ct.ThrowIfCancellationRequested();var h=MinimumPhaseDb(DbAt,sr,taps);
            double error=FitError(h,shape,sr);
            if(!double.IsFinite(error))throw new InvalidOperationException(stage+"：EQ 响应超出数值精度。");
            double improvement=bestError-error;
            if(error<bestError){bestError=error;best=h;}
            if(bestError<=.05)break;
            // Stop on an accuracy plateau, report the residual; never clip requested gains.
            stagnant=improvement<Math.Max(.002,bestError*.01)?stagnant+1:0;
            if(stagnant>=2)break;
            taps=checked(taps*2);
        }
        double scale=Math.Pow(10,mean/20);
        for(int i=0;i<best.Length;i++)best[i]*=scale;
        if(!double.IsFinite(scale)||scale==0||best.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException(stage+"：EQ 系数无法用双精度表示。");
        ct.ThrowIfCancellationRequested();var actual=Dsp.SmoothedDbAt(Dsp.Convolve(response,best),sr,denominator);
        double residual=Math.Sqrt(actual.Select((v,i)=>Math.Pow(v-original[i]*(1-strength),2)).Average());
        int max=Array.IndexOf(target,target.Max()),min=Array.IndexOf(target,target.Min());
        return new(best,new(stage,target[max],target[min],shape[max],shape[min],Dsp.Frequencies[max],Dsp.Frequencies[min],best.Length,bestError,residual));
    }
    static double[] MinimumPhaseDb(Func<double,double> db,int sr,int taps)
    {
        int n=Dsp.Pow2(checked(Math.Max(32768,taps*8)));var a=new Complex[n];
        double offset=double.NegativeInfinity;
        for(int i=0;i<=n/2;i++){double v=db((double)i*sr/n)*Math.Log(10)/20;offset=Math.Max(offset,v);a[i]=v;}
        for(int i=0;i<=n/2;i++){a[i]-=offset;if(i>0&&i<n/2)a[n-i]=a[i];}
        Dsp.Fft(a,true);for(int i=1;i<n/2;i++)a[i]*=2;for(int i=n/2+1;i<n;i++)a[i]=0;
        Dsp.Fft(a);for(int i=0;i<n;i++)a[i]=Complex.Exp(a[i]);Dsp.Fft(a,true);
        double scale=Math.Exp(offset);var h=new double[taps];
        for(int i=0;i<taps;i++){double window=i<taps*.9?1:.5+.5*Math.Cos(Math.PI*(i-taps*.9)/(taps-1-taps*.9));h[i]=a[i].Real*window*scale;}
        return h;
    }
    static double FitError(double[] h,double[] target,int sr)
    {
        var spectrum=Dsp.Spectrum(h,Dsp.Pow2(Math.Max(65536,checked(h.Length*8))));double max=0;
        for(int i=0;i<target.Length;i++)
        {
            double bin=Dsp.Frequencies[i]*spectrum.Length/sr;int k=(int)bin;double t=bin-k;
            double power=Dsp.Power(spectrum[k])*(1-t)+Dsp.Power(spectrum[k+1])*t;
            max=Math.Max(max,Math.Abs(10*Math.Log10(power)-target[i]));
        }
        return max;
    }
}
