using System.Numerics;
namespace SoundstageIR.Core;
public sealed record EqReport(string Stage,double MaximumBoostDb,double MaximumCutDb,double RelativeBoostDb,double RelativeCutDb,double BoostHz,double CutHz,int Taps,double DesignErrorDb,double ResponseResidualDb);
public sealed record EqDesign(double[] Impulse,EqReport Report);
public sealed record EqSpectrum(double[] GainDb,Complex[] Spectrum,EqReport Report);
public static class EqDesigner
{
    // A one-second floor supports the 10-20 Hz bandpass transition for direct-only scenes.
    // Longer scenes supply their own time budget; no iterative length doubling.
    public static int Support(int rawLength,int sr)=>Math.Max(rawLength,sr);
    public static int FftLength(int rawLength,int sr)=>Dsp.Pow2(checked(4*(rawLength+Support(rawLength,sr)-1)));
    public static EqSpectrum Plan(Complex[] response,int sr,int denominator,double strength,string stage,int support,CancellationToken ct=default)
    {
        if(!double.IsFinite(strength)||strength<0||strength>1)throw new ArgumentOutOfRangeException(nameof(strength));
        int n=response.Length;var db=new double[n/2+1];
        if(strength==0)return new(db,Enumerable.Repeat(Complex.One,n).ToArray(),new(stage,0,0,0,0,0,0,1,0,0));
        ct.ThrowIfCancellationRequested();double high=Dsp.BandpassStopHigh(sr);
        var frequencies=DesignFrequencies(sr);
        double peak=response.Max(v=>v.Magnitude);
        if(!double.IsFinite(peak)||peak==0)throw new InvalidOperationException(stage+"：参考响应为空或包含无效数值。");
        var original=Dsp.SmoothedSpectrumDb(response.Select(v=>v/peak).ToArray(),sr,denominator,frequencies,10,high)
            .Select(v=>v+20*Math.Log10(peak)).ToArray();
        if(original.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException(stage+"：频段能量无法分辨，无法设计有效 EQ。");
        var target=original.Select(v=>-v*strength).ToArray();
        db=InterpolateGain(target,frequencies,n,sr);
        var spectrum=MinimumPhaseSpectrum(db,ct);
        var corrected=new Complex[n];for(int k=0;k<n;k++)corrected[k]=response[k]*spectrum[k];
        var actual=Dsp.SmoothedSpectrumDb(corrected,sr,denominator,Dsp.Frequencies,20,20000);
        double residual=Math.Sqrt(actual.Select((v,i)=>Math.Pow(v-original[i+121]*(1-strength),2)).Average());
        double mean=target.Skip(121).Take(Dsp.Frequencies.Length).Average();
        int max=Array.IndexOf(target,target.Max()),min=Array.IndexOf(target,target.Min());
        return new(db,spectrum,new(stage,target[max],target[min],target[max]-mean,target[min]-mean,frequencies[max],frequencies[min],support,0,residual));
    }
    public static double[] DesignFrequencies(int sr)=>Enumerable.Range(0,121).Select(i=>10*Math.Pow(2,i/121.0))
        .Concat(Dsp.Frequencies).Concat(Enumerable.Range(1,32).Select(i=>20000*Math.Pow(Dsp.BandpassStopHigh(sr)/20000,i/32.0))).ToArray();
    public static double[] InterpolateGain(double[] target,double[] frequencies,int n,int sr)
    {
        var db=new double[n/2+1];int j=0;
        for(int k=0;k<db.Length;k++)
        {
            double f=Math.Clamp(k*(double)sr/n,frequencies[0],frequencies[^1]);
            while(j<frequencies.Length-2&&frequencies[j+1]<f)j++;
            double t=Math.Log(f/frequencies[j])/Math.Log(frequencies[j+1]/frequencies[j]);
            db[k]=target[j]+t*(target[j+1]-target[j]);
        }
        return db;
    }
    public static Complex[] MinimumPhaseSpectrum(double[] db,CancellationToken ct=default)
    {
        int n=(db.Length-1)*2;var a=new Complex[n];double offset=db.Max()*Math.Log(10)/20;
        for(int i=0;i<=n/2;i++){a[i]=db[i]*Math.Log(10)/20-offset;if(i>0&&i<n/2)a[n-i]=a[i];}
        ct.ThrowIfCancellationRequested();Dsp.Fft(a,true);
        for(int i=1;i<n/2;i++)a[i]*=2;for(int i=n/2+1;i<n;i++)a[i]=0;
        Dsp.Fft(a);double scale=Math.Exp(offset);
        for(int i=0;i<n;i++){a[i]=Complex.Exp(a[i])*scale;if(!double.IsFinite(a[i].Magnitude))throw new InvalidOperationException("校正响应超出双精度表示范围。");}
        return a;
    }
    public static double[] ToImpulse(Complex[] spectrum,int length,bool taper=true)
    {
        var a=(Complex[])spectrum.Clone();Dsp.Fft(a,true);var h=new double[length];
        for(int i=0;i<length;i++)
        {
            double w=!taper||i<length*.9?1:.5+.5*Math.Cos(Math.PI*(i-length*.9)/(length-1-length*.9));
            h[i]=a[i].Real*w;
        }
        return h;
    }
    // Standalone EQ design for callers that need an FIR. The generator works with Plan directly.
    public static EqDesign Design(double[] response,int sr,int denominator,double strength=1,string stage="EQ",CancellationToken ct=default)
    {
        if(strength==0)return new([1.0],new(stage,0,0,0,0,0,0,1,0,0));
        int support=Support(response.Length,sr),n=FftLength(response.Length,sr);
        var plan=Plan(Dsp.Spectrum(response,n),sr,denominator,strength,stage,support,ct);
        var h=ToImpulse(plan.Spectrum,support);
        var actual=Dsp.SmoothedDbAt(Dsp.Convolve(response,h),sr,denominator);var original=Dsp.SmoothedDbAt(response,sr,denominator);
        double residual=Math.Sqrt(actual.Select((v,i)=>Math.Pow(v-original[i]*(1-strength),2)).Average());
        return new(h,plan.Report with {DesignErrorDb=ProjectionError(h,plan.Spectrum,sr),ResponseResidualDb=residual});
    }
    public static double ProjectionError(double[] h,Complex[] desired,int sr)
    {
        var actual=Dsp.Spectrum(h,desired.Length);double max=0;
        foreach(double f in Dsp.Frequencies)
        {
            int k=(int)Math.Round(f*actual.Length/sr);
            double delta=20*Math.Log10(actual[k].Magnitude/desired[k].Magnitude);max=Math.Max(max,Math.Abs(delta));
        }
        return max;
    }
}
