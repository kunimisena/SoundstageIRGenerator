using System.Numerics;
namespace SoundstageIR.Core.Speakers;

public sealed record BandSynthesis(double[][] Drive,double[][] DryDrive,double[][] Target,
    double[][] Predicted,double[][] PredictedDry,double[][] PredictedWet,double[] CommonEq,
    int Delay,double ProjectionDb,double WetPercent,InverseResponse Inverse,int BalanceEvaluations);

// Hybrid path only. Full-band projects never enter this class.
public static class SpeakerBands
{
    // Matched digital LR4 sections. Low branch includes the upper allpass so that
    // three identical inputs sum to a unit-magnitude allpass, including endpoints.
    public static (Complex Low,Complex Mid,Complex High) Weights(double f,int sr,double low,double high)
    {
        (Complex L,Complex H) Split(double fc)
        {
            if(f<=0)return(Complex.One,Complex.Zero);
            if(f>=sr*.5)return(Complex.Zero,Complex.One);
            Complex s=new(0,Math.Tan(Math.PI*f/sr)/Math.Tan(Math.PI*fc/sr));
            var den=s*s+Math.Sqrt(2)*s+1;den*=den;
            return(1/den,s*s*s*s/den);
        }
        var a=low<=20?(Complex.Zero,Complex.One):Split(low);
        var b=high>=20000?(Complex.One,Complex.Zero):Split(high);
        return(a.Item1*(b.Item1+b.Item2),a.Item2*b.Item1,a.Item2*b.Item2);
    }
    // All three wet branches have independent random streams. Their power weights
    // sum to one while their phases follow the matched crossover sections.
    public static (Complex Low,Complex Mid,Complex High) ReverbWeights((Complex Low,Complex Mid,Complex High) crossover)
    {
        double scale=1/Math.Sqrt(Dsp.Power(crossover.Low)+Dsp.Power(crossover.Mid)+Dsp.Power(crossover.High));
        return(crossover.Low*scale,crossover.Mid*scale,crossover.High*scale);
    }
    public static BandSynthesis Generate(SpeakerProject p,GenerationResult target,GenerationResult playback,
        CancellationToken ct=default,Action<string>? progress=null)
    {
        int sr=p.Field.SampleRate;var outsideParameters=p.OutsideParameters();
        bool dryOn=p.Field.Direct.Enabled&&p.Field.ReflectionEnergyPercent<100;
        bool wetOn=!p.Field.Direct.Enabled||p.Field.ReflectionEnergyPercent>0;
        (double[][] Wet,double[][] Reference) GenerateOutside(Excitation e,bool enabled,int streamBase)
        {
            double[][] wet=[[0],[0],[0],[0]],reference=[[0],[0],[0],[0]];
            if(wetOn&&enabled)
            {
                var flat=e.Clone();flat.Energy=flat.Energy.Select(k=>new Knot(k.X,0)).ToList();flat.GainDb=flat.TiltDbPerOct=0;
                foreach(int c in new[]{0,3})
                {
                    int stream=p.Field.StrictMirror?streamBase:streamBase+c;
                    reference[c]=NoiseKernel.Generate(flat,sr,p.Field.Seed,p.NoiseId,stream,ct);
                    wet[c]=NoiseKernel.Generate(e,sr,p.Field.Seed,p.NoiseId,stream,ct);
                }
            }
            return(wet,reference);
        }
        // Curves stop at the crossover. Only their constant endpoint continuation
        // enters the smooth overlap; there are no hidden in-band decay/energy knots.
        var low=GenerateOutside(outsideParameters.Low,p.InverseLowHz>20,40);
        var high=GenerateOutside(outsideParameters.High,p.InverseHighHz<20000,48);
        int support=Math.Max(target.Kernels.Max(h=>h.Length),low.Wet.Concat(high.Wet).Max(h=>h.Length))+playback.Kernels.Max(h=>h.Length);
        int n=Dsp.Pow2(Math.Max(sr*2,checked(support*2))),bins=n/2+1;
        Complex[][] Spectra(double[][] x)=>x.Select(h=>Dsp.Spectrum(h,n)).ToArray();
        var f=Spectra(playback.Kernels);var td=Spectra(target.FinalDirectPaths);var tw=Spectra(target.FinalReflectionPaths);
        var bl=Spectra(low.Wet);var bh=Spectra(high.Wet);
        double desiredWet=EnergyBalance.Energy(target.FinalReflectionPaths,sr);
        double WetScale(double[][] reference,bool enabled)=>wetOn&&enabled
            ?Math.Sqrt((desiredWet>1e-25?desiredWet:1)/Math.Max(1e-280,MatrixEnergy(Multiply(f,Spectra(reference)),sr))):0;
        // Flat-spectrum references retain the energy curve's virtual 0 dB in each branch.
        double lowScale=WetScale(low.Reference,p.InverseLowHz>20),highScale=WetScale(high.Reference,p.InverseHighHz<20000);
        double dryScale=dryOn?Math.Sqrt(EnergyBalance.Energy(target.FinalDirectPaths,sr)/Math.Max(1e-280,EnergyBalance.Energy(playback.Kernels,sr))):0;
        var d=Arrays(n);var r=Arrays(n);var intendedD=Arrays(n);var intendedR=Arrays(n);
        var scale=new double[bins];var weak=new double[bins];var freq=new double[bins];
        for(int k=0;k<bins;k++)
        {
            double u=Dsp.Power(f[0][k])+Dsp.Power(f[2][k]),v=Dsp.Power(f[1][k])+Dsp.Power(f[3][k]);
            scale[k]=Math.Sqrt((u+v)*.5);freq[k]=k*(double)sr/n;
            var z=Complex.Conjugate(f[0][k])*f[1][k]+Complex.Conjugate(f[2][k])*f[3][k];
            double strongest=(u+v+Math.Sqrt((u-v)*(u-v)+4*Dsp.Power(z)))*.5;
            weak[k]=Dsp.Power(f[0][k]*f[3][k]-f[1][k]*f[2][k])/Math.Max(1e-300,strongest*scale[k]*scale[k]);
        }
        var local=Dsp.SmoothPower(freq,weak,6);double floor=scale.Max()*1e-4,gain=Math.Pow(10,p.MaximumInverseGainDb/20);
        var recorder=new SpeakerTransform.ResponseRecorder(n,sr);
        double align=target.ZeroSample-playback.ZeroSample;
        for(int k=0;k<bins;k++)
        {
            if(k%2048==0)ct.ThrowIfCancellationRequested();
            double s=Math.Max(1e-150,scale[k]),fade=scale[k]*scale[k]/(scale[k]*scale[k]+floor*floor);
            var q=SpeakerInverse.Solve(f[0][k]/s,f[1][k]/s,f[2][k]/s,f[3][k]/s,gain,Math.Max(0,local[k]-weak[k]));
            var w=Weights(freq[k],sr,p.InverseLowHz,p.InverseHighHz);var outside=w.Low+w.High;var wetWeights=ReverbWeights(w);
            var shift=Complex.FromPolarCoordinates(1,-2*Math.PI*k*align/n);
            recorder.Add(k,q.Select(v=>v*w.Mid).ToArray(),fade/s);
            for(int row=0;row<2;row++)for(int col=0;col<2;col++)
            {
                int c=row*2+col;var bd=(row==col?dryScale:0)*shift;var rw=(wetWeights.Low*bl[c][k]*lowScale+wetWeights.High*bh[c][k]*highScale)*shift;
                var cd=dryOn?(row==col?Complex.One:Complex.Zero)+fade/s*(q[row*2]*(td[col][k]-f[col][k])+q[row*2+1]*(td[2+col][k]-f[2+col][k])):Complex.Zero;
                var cw=wetOn?fade/s*(q[row*2]*tw[col][k]+q[row*2+1]*tw[2+col][k]):Complex.Zero;
                d[c][k]=outside*bd+w.Mid*cd;r[c][k]=rw+wetWeights.Mid*cw;
                var fd=f[c][k]*dryScale*shift;
                var fw=(wetWeights.Low*lowScale*(f[row*2][k]*bl[col][k]+f[row*2+1][k]*bl[2+col][k])+wetWeights.High*highScale*(f[row*2][k]*bh[col][k]+f[row*2+1][k]*bh[2+col][k]))*shift;
                intendedD[c][k]=outside*fd+w.Mid*td[c][k];intendedR[c][k]=fw+wetWeights.Mid*tw[c][k];
            }
        }
        Hermitian(d);Hermitian(r);Hermitian(intendedD);Hermitian(intendedR);
        var ed=Multiply(f,d);var er=Multiply(f,r);
        progress?.Invoke(TextCatalog.English?"Calibrating final ear energy":"校准最终耳端能量");
        var calibration=Calibrate(ed,er,p.Field,ct);double wetGain=calibration.Gain;
        var eq=EqDesigner.MinimumPhaseSpectrum(calibration.Db,ct);
        for(int c=0;c<4;c++)for(int k=0;k<n;k++)
        {d[c][k]*=eq[k];r[c][k]*=wetGain*eq[k];intendedD[c][k]=(intendedD[c][k]+wetGain*intendedR[c][k])*eq[k];}
        var projected=Project(d,r,ct);
        var drive=projected.Dry.Zip(projected.Wet).Select(pair=>Dsp.Sum(pair.First,pair.Second)).ToArray();
        var predicted=SpeakerInverse.Multiply(playback.Kernels,drive,ct);
        double referenceDb=new[]{0,1}.SelectMany(ear=>Dsp.SmoothedDbAt(Dsp.Sum(predicted[2*ear],predicted[2*ear+1]),sr,p.Field.Smooth1)).Average();
        if(!double.IsFinite(referenceDb))throw new ArithmeticException("Invalid hybrid output reference / 分频输出参考无效");
        double outputGain=Math.Pow(10,(p.Field.OutputDb-referenceDb)/20);
        if(!double.IsFinite(outputGain))throw new ArithmeticException("Hybrid gain overflow / 分频增益溢出");
        for(int c=0;c<4;c++)for(int i=0;i<drive[c].Length;i++)
        {drive[c][i]=(float)(drive[c][i]*outputGain);projected.Dry[c][i]*=outputGain;}
        predicted=SpeakerInverse.Multiply(playback.Kernels,drive,ct);
        var predictedDry=SpeakerInverse.Multiply(playback.Kernels,projected.Dry,ct);
        var predictedWet=EnergyBalance.Subtract(predicted,predictedDry);
        double wet=EnergyBalance.Percent(EnergyBalance.Energy(predictedDry,sr),EnergyBalance.Energy(predictedWet,sr));
        var desired=intendedD.Select(h=>{Dsp.Fft(h,true);return h.Take(n/2).Select(v=>v.Real*outputGain).ToArray();}).ToArray();
        return new(drive,projected.Dry,desired,predicted,predictedDry,predictedWet,
            EqDesigner.ToImpulse(eq,n/2),projected.Delay,projected.Error,wet,recorder.Result,calibration.Evaluations);
    }
    static Complex[][] Arrays(int n)=>Enumerable.Range(0,4).Select(_=>new Complex[n]).ToArray();
    static void Hermitian(Complex[][] a)
    {int n=a[0].Length;foreach(var h in a){h[0]=h[0].Real;h[n/2]=h[n/2].Real;for(int k=1;k<n/2;k++)h[n-k]=Complex.Conjugate(h[k]);}}
    static Complex[][] Multiply(Complex[][] a,Complex[][] b)
    {
        int n=a[0].Length;var r=Arrays(n);for(int row=0;row<2;row++)for(int col=0;col<2;col++)for(int k=0;k<n;k++)
            r[row*2+col][k]=a[row*2][k]*b[col][k]+a[row*2+1][k]*b[2+col][k];return r;
    }
    static double BinWeight(int k,int n,int sr)
    {double df=sr/(double)n;return Math.Max(0,Math.Min((k+.5)*df,20000)-Math.Max((k-.5)*df,20))/df;}
    static double MatrixEnergy(Complex[][] h,int sr)
    {int n=h[0].Length;double sum=0;for(int k=0;k<=n/2;k++)sum+=BinWeight(k,n,sr)*h.Sum(c=>Dsp.Power(c[k]));return 2*sum/n;}
    static (double Gain,double[] Db,int Evaluations) Calibrate(Complex[][] d,Complex[][] r,Project p,CancellationToken ct)
    {
        int n=d[0].Length,bins=n/2+1,sr=p.SampleRate;
        var dp=new double[bins];var rp=new double[bins];var ds=new double[bins];var rs=new double[bins];var cross=new double[bins];
        for(int k=0;k<bins;k++)
        {
            dp[k]=d.Sum(c=>Dsp.Power(c[k]));rp[k]=r.Sum(c=>Dsp.Power(c[k]));
            for(int ear=0;ear<2;ear++){var x=d[ear*2][k]+d[ear*2+1][k];var y=r[ear*2][k]+r[ear*2+1][k];ds[k]+=.5*Dsp.Power(x);rs[k]+=.5*Dsp.Power(y);cross[k]+=(x*Complex.Conjugate(y)).Real;}
        }
        // Smooth only the audible-band response; extend its edges before adding the
        // output bandpass, so stop-band attenuation is never inverted.
        var grid=Dsp.Frequencies;
        double[] Smooth(double[] x)=>Dsp.SmoothedPowerSpectrum(x,sr,p.Smooth1,grid,20,20000);
        ds=Smooth(ds);rs=Smooth(rs);cross=Smooth(cross);
        double strength=p.Equalize?p.EarEqStrengthPercent/100:0;
        int evals=0;
        (double Error,double[] Db) Eval(double gainDb)
        {
            ct.ThrowIfCancellationRequested();evals++;double g=Math.Pow(10,gainDb/20);
            var dbGrid=grid.Select((_,i)=>-strength*10*Math.Log10(Math.Max(1e-280,ds[i]+g*g*rs[i]+g*cross[i]))).ToArray();
            var db=EqDesigner.InterpolateGain(dbGrid,grid,n,sr);double max=double.NegativeInfinity;
            for(int k=0;k<bins;k++){db[k]+=Dsp.BandpassDb(k*(double)sr/n,sr);max=Math.Max(max,db[k]);}
            double a=0,b=0;for(int k=0;k<bins;k++){double w=BinWeight(k,n,sr)*Math.Pow(10,(db[k]-max)/10);a+=w*dp[k];b+=w*rp[k];}
            double fraction=p.ReflectionEnergyPercent/100;
            return(Math.Log(Math.Max(1e-280,b)/Math.Max(1e-280,a))+2*Math.Log(g)-Math.Log(Math.Max(1e-280,fraction)/Math.Max(1e-280,1-fraction)),db);
        }
        double dryEnergy=dp.Sum(),wetEnergy=rp.Sum();
        if(dryEnergy<1e-25||wetEnergy<1e-25||!p.Direct.Enabled||p.ReflectionEnergyPercent is <=0 or >=100)return(1,Eval(0).Db,evals);
        double initial=10*Math.Log10(dryEnergy/wetEnergy*p.ReflectionEnergyPercent/(100-p.ReflectionEnergyPercent));
        var first=Eval(initial);double best=initial,error=Math.Abs(first.Error);var bestDb=first.Db;
        if(error<1e-6)return(Math.Pow(10,best/20),bestDb,evals);
        double lo=initial,hi=initial;var left=first;var right=first;double step=6;
        for(int j=0;j<16&&left.Error*right.Error>=0;j++)
        {lo=initial-step;hi=initial+step;left=Eval(lo);right=Eval(hi);step*=2;}
        if(left.Error*right.Error>=0)throw new ArithmeticException("Cannot calibrate hybrid wet energy / 分频混响能量校准未收敛");
        for(int j=0;j<35;j++)
        {
            double x=Math.Clamp((lo*right.Error-hi*left.Error)/(right.Error-left.Error),lo+.1*(hi-lo),hi-.1*(hi-lo));var v=Eval(x);
            if(Math.Abs(v.Error)<error){best=x;bestDb=v.Db;error=Math.Abs(v.Error);}if(error<1e-6)break;
            if(Math.Sign(v.Error)==Math.Sign(left.Error)){lo=x;left=v;}else{hi=x;right=v;}
        }
        if(error>1e-4)throw new ArithmeticException("Cannot calibrate hybrid wet energy / 分频混响能量校准未收敛");
        return(Math.Pow(10,best/20),bestDb,evals);
    }
    static (double[][] Dry,double[][] Wet,int Delay,double Error) Project(Complex[][] dry,Complex[][] wet,CancellationToken ct)
    {
        int n=dry[0].Length;foreach(var h in dry.Concat(wet)){ct.ThrowIfCancellationRequested();Dsp.Fft(h,true);}
        double total=dry.Concat(wet).Sum(h=>h.Sum(v=>v.Real*v.Real));int delay=64,length;double error;
        double Window(int j,int len)=>j>=len?0:j<32?.5-.5*Math.Cos(Math.PI*j/32):j>len-256?.5-.5*Math.Cos(Math.PI*(len-1-j)/255):1;
        do
        {
            ct.ThrowIfCancellationRequested();length=n/2+delay;error=0;
            foreach(var h in dry.Concat(wet))for(int i=0;i<n;i++){double w=Window((i+delay)%n,length);error+=h[i].Real*h[i].Real*(1-w)*(1-w);}
            if(error/Math.Max(total,1e-300)<1e-8||delay>=n/4)break;delay*=2;
        }while(true);
        double[][] Render(Complex[][] a)=>a.Select(h=>Enumerable.Range(0,length).Select(j=>h[(j-delay+n)%n].Real*Window(j,length)).ToArray()).ToArray();
        return(Render(dry),Render(wet),delay,Dsp.Db(error/Math.Max(total,1e-300)));
    }
}
