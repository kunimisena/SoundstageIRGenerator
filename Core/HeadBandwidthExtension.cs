using System.Numerics;
namespace SoundstageIR.Core;

// Extend native FABIAN bandwidth before audio resampling can introduce image zeros.
// Retain the measured in-band excess phase; only magnitude has a new band edge.
public static class HeadBandwidthExtension
{
    public const double LowHz=20,HighHz=20000;
    public static int LeadSamples(int sr)=>(int)Math.Round(48.0*sr/FabianData.NativeRate);
    public static double Delay(int sr)=>-LeadSamples(sr)/(double)sr;

    public static EarTransfer Apply(double[] nativeImpulse,int sr,CancellationToken ct=default)
    {
        if(sr is not (44100 or 48000 or 96000))throw new ArgumentOutOfRangeException(nameof(sr));
        ct.ThrowIfCancellationRequested();
        int nativeN=Dsp.Pow2(Math.Max(FabianData.NativeRate*4,nativeImpulse.Length*8));
        var native=Dsp.Spectrum(nativeImpulse,nativeN);
        var nativeDb=new double[nativeN/2+1];
        for(int k=0;k<nativeDb.Length;k++)nativeDb[k]=Level(native[k]);
        var nativeMinimum=EqDesigner.MinimumPhaseSpectrum(nativeDb,ct);
        var excess=new double[nativeDb.Length];
        double previous=0,unwrapped=0;
        for(int k=0;k<excess.Length;k++)
        {
            double phase=(native[k]/nativeMinimum[k]).Phase;
            if(k==0)unwrapped=phase;else unwrapped+=Math.IEEERemainder(phase-previous,2*Math.PI);
            excess[k]=unwrapped;previous=phase;
        }
        int n=Dsp.Pow2(sr*4),half=n/2,lead=LeadSamples(sr);
        double NativeBin(double f)=>f*nativeN/FabianData.NativeRate;
        double Phase(double f)
        {
            double bin=NativeBin(f);int i=(int)bin;double t=bin-i;
            return excess[i]*(1-t)+excess[i+1]*t;
        }
        double edgePhase=Phase(HighHz),slope=(edgePhase-Phase(HighHz-1000))/1000;
        double endPhase=edgePhase+(sr*.5-HighHz)*slope;
        double endAdjustment=Math.Round(endPhase/Math.PI)*Math.PI-endPhase;
        var targetDb=new double[half+1];var phaseOut=new double[half+1];
        for(int k=0;k<=half;k++)
        {
            double f=k*(double)sr/n;
            // Cubic complex interpolation retains narrow native notches, unlike
            // interpolating dB through the bottom of a notch.
            targetDb[k]=Level(Interpolate(native,NativeBin(Math.Clamp(f,LowHz,HighHz))));
            double phase;
            if(f<=HighHz)phase=Phase(f);
            else
            {
                double t=(f-HighHz)/(sr*.5-HighHz);
                phase=edgePhase+(f-HighHz)*slope+endAdjustment*t*t*(3-2*t);
            }
            phaseOut[k]=phase-2*Math.PI*k*lead/n;
        }
        var spectrum=EqDesigner.MinimumPhaseSpectrum(targetDb,ct);
        for(int k=0;k<=half;k++)
        {
            spectrum[k]*=Complex.FromPolarCoordinates(1,phaseOut[k]);
            if(k==0||k==half)spectrum[k]=new(spectrum[k].Real,0);
            else spectrum[n-k]=Complex.Conjugate(spectrum[k]);
        }
        // Finite causal projection uses the same ~1 ms allowance as the ordinary
        // resampler. A minimum-phase residual correction removes projection error
        // without imposing any gain floor on a measured notch.
        double absoluteTolerance=1e-5*Math.Pow(10,targetDb.Max()/20);
        for(int pass=0;pass<8;pass++)
        {
            ct.ThrowIfCancellationRequested();
            var time=(Complex[])spectrum.Clone();Dsp.Fft(time,true);
            double total=time.Sum(v=>v.Real*v.Real),tail=0;int needed=lead+nativeImpulse.Length;
            for(int k=half-1;k>=0;k--)
            {
                tail+=time[k].Real*time[k].Real;
                if(tail>total*1e-12){needed=Math.Max(needed,k+1);break;}
            }
            int length=Math.Min(half,Math.Max(needed,(int)Math.Ceiling(needed/.9)+32));
            double[] h;Complex[] actual;double error;
            while(true)
            {
                h=new double[length];
                for(int i=0;i<length;i++)h[i]=time[i].Real*(i<length*.9?1:.5+.5*Math.Cos(Math.PI*(i-length*.9)/(length-1-length*.9)));
                actual=Dsp.Spectrum(h,n);error=0;
                bool accurate=true;
                for(int k=0;k<=half;k++)
                {
                    double delta=Math.Abs(Level(actual[k])-targetDb[k]);error=Math.Max(error,delta);
                    // Relative plus absolute numerical accuracy, never a magnitude floor.
                    if(delta>.05 && Math.Abs(actual[k].Magnitude-Math.Pow(10,targetDb[k]/20))>absoluteTolerance)accurate=false;
                }
                if(accurate)return new(h,Delay(sr));
                if(length==half)break;
                length=Math.Min(half,checked(length*2));
            }
            if(pass==7)throw new InvalidOperationException($"FABIAN 带外延伸重建误差过大：{error:F3} dB。");
            var correctionDb=new double[half+1];
            for(int k=0;k<=half;k++)correctionDb[k]=targetDb[k]-Level(actual[k]);
            var correction=EqDesigner.MinimumPhaseSpectrum(correctionDb,ct);
            for(int k=0;k<n;k++)spectrum[k]=actual[k]*correction[k];
        }
        throw new InvalidOperationException();
    }
    static double Level(Complex value)
    {
        double magnitude=value.Magnitude;
        if(magnitude==0||!double.IsFinite(magnitude))throw new InvalidOperationException("FABIAN 带宽重建遇到精确零点或无效数值。");
        return 20*Math.Log10(magnitude);
    }
    static Complex Interpolate(Complex[] a,double bin)
    {
        int i=(int)bin;double t=bin-i;
        return a[i-1]*(-t*(t-1)*(t-2)/6)+a[i]*((t+1)*(t-1)*(t-2)/2)
            -a[i+1]*((t+1)*t*(t-2)/2)+a[i+2]*((t+1)*t*(t-1)/6);
    }
}
