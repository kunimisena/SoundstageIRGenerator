using System.Numerics;
using SoundstageIR.Core;
public static class StatisticsChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        const int sr=48000;var id=Guid.Parse("28a3a3f8-2d40-4b04-9bf2-0a111e97e070");
        var e=new Excitation{GainDb=0,Energy=[new(20,0),new(20000,0)],Decay=[new(20,.16),new(20000,.16)],OnsetMs=2,BuildMs=3};
        Complex Dtft(double[] h,double f){Complex z=0,w=Complex.FromPolarCoordinates(1,-2*Math.PI*f/sr),p=1;foreach(double v in h){z+=v*p;p*=w;}return z;}
        var aa=new List<Complex[]>();var ss=new List<Complex[]>();double energyA=0,energyS=0,energyLong=0;var longE=e.Clone();foreach(var k in longE.Decay)k.Y=.48;
        double[]? sumEdc=null;
        for(int seed=1;seed<=64;seed++)
        {
            var a=NoiseKernel.Generate(e,sr,seed,id,0);var b=NoiseKernel.Generate(e,sr,seed,id,1);var s=Dsp.Sum(a,b,1/Math.Sqrt(2));
            aa.Add([Dtft(a,1000),Dtft(a,1005),Dtft(a,1200)]);ss.Add([Dtft(s,1000),Dtft(s,1005),Dtft(s,1200)]);
            energyA+=a.Sum(v=>v*v);energyS+=s.Sum(v=>v*v);
            sumEdc??=new double[a.Length];double energy=0;for(int n=a.Length-1;n>=0;n--){energy+=a[n]*a[n];sumEdc[n]+=energy;}
            if(seed<=12)energyLong+=NoiseKernel.Generate(longE,sr,seed,id,0).Sum(v=>v*v);
        }
        Complex Corr(List<Complex[]> a,int j)=>a.Aggregate(Complex.Zero,(s,v)=>s+v[0]*Complex.Conjugate(v[j]))/Math.Sqrt(a.Sum(v=>Dsp.Power(v[0]))*a.Sum(v=>Dsp.Power(v[j])));
        double mean=energyA/64,meanSum=energyS/64,meanLong=energyLong/12;
        check(Math.Abs(Dsp.Db(mean))<.5,"Flat expected spectral energy equals unit impulse energy");
        check(Math.Abs(Dsp.Db(meanSum/mean))<.3,"Independent normalized sum preserves energy");
        check(Math.Abs(Dsp.Db(meanLong/mean))<.5,"Changing RT preserves integrated spectral energy");
        var c1=Corr(aa,1);var c2=Corr(ss,1);check((c1-c2).Magnitude<.22,"Normalized sum preserves cross-frequency complex correlation");
        check(c1.Magnitude>.75&&Corr(aa,2).Magnitude<.35,"Nearby frequency correlation and distant frequency decorrelation");
        double t10=Cross(-10),t30=Cross(-30),measured=(t30-t10)*3;
        check(Math.Abs(measured/.16-1)<.12,"Ensemble energy decay gives requested RT60");
        double Cross(double db){double initial=sumEdc![0];for(int i=0;i<sumEdc.Length;i++)if(Dsp.Db(sumEdc[i]/initial)<=db)return i/(double)sr;return double.NaN;}
        double[]? power=null;var shape=e.Clone();shape.Energy=[new(80,-5),new(1000,0),new(8000,-9)];
        for(int seed=1;seed<=12;seed++){var a=NoiseKernel.Generate(shape,sr,seed,id,0);var p=Dsp.SmoothedPowerAt(a,sr,3);power??=new double[p.Length];for(int j=0;j<p.Length;j++)power[j]+=p[j]/12;}
        foreach(double f in new[]{100.0,1000,8000}){int i=Enumerable.Range(0,Dsp.Frequencies.Length).MinBy(i=>Math.Abs(Math.Log(Dsp.Frequencies[i]/f)));check(Math.Abs(Dsp.Db(power![i])-shape.EnergyDb(f))<2,"Broad spectrum follows energy curve @"+f);}
        foreach(int count in new[]{6,8,12,32}){var group=Templates.Create(Distribution.Ring,count,0,0,90,e,"test");check(group.All(s=>s.Left.GainDb==e.GainDb),"Template keeps fixed reference weights "+count);}
        var cloned=Presets.BuiltIn[0].Create().Sources[0].CloneIndependent();check(cloned.Id!=Presets.BuiltIn[0].Create().Sources[0].Id,"Copied source has independent ID");
        var h1=NoiseKernel.Generate(e,sr,123,id,0);var padded=new double[h1.Length+40000];Array.Copy(h1,0,padded,18000,h1.Length);var p1=Dsp.SmoothedPowerAt(h1,sr,3);var p2=Dsp.SmoothedPowerAt(padded,sr,3);check(p1.Zip(p2).Max(p=>Math.Abs(Dsp.Db(p.First/p.Second)))<.15,"Power smoothing insensitive to common delay and FFT grid");
        var bright=new Excitation{GainDb=0,Energy=[new(20,0),new(20000,0)],Decay=[new(80,.12),new(1000,.3),new(8000,.08)],BuildMs=5};
        var rtLogs=new List<string>();
        foreach(double center in new[]{1000.0,8000})
        {
            double[]? edc=null;
            for(int seed=1;seed<=8;seed++)
            {
                var h=NoiseKernel.Generate(bright,sr,seed,id,0);var spec=Dsp.Spectrum(h);double octave=Math.Log(2)/6;
                for(int k=0;k<spec.Length;k++){double f=Math.Min(k,spec.Length-k)*(double)sr/spec.Length;double q=f>0?Math.Log(f/center)/octave:99;spec[k]*=Math.Abs(q)<1?Math.Cos(q*Math.PI/2):0;}
                Dsp.Fft(spec,true);edc??=new double[h.Length];double sum=0;for(int i=h.Length-1;i>=0;i--){sum+=spec[i].Real*spec[i].Real;edc[i]+=sum;}
            }
            double Find(double db){for(int i=(int)(.02*sr);i<edc!.Length;i++)if(Dsp.Db(edc[i]/edc[(int)(.02*sr)])<db)return i/(double)sr;return double.NaN;}
            double rt=(Find(-25)-Find(-5))*3;rtLogs.Add($"{center} Hz: expected {bright.Rt(center):F3}s measured {rt:F3}s");check(Math.Abs(rt/bright.Rt(center)-1)<.22,"Frequency-dependent RT "+center);
        }
        File.WriteAllLines(Path.Combine(folder,"statistics.txt"),new[]{$"Mean energy {Dsp.Db(mean):F3} dB; sum {Dsp.Db(meanSum):F3} dB; long {Dsp.Db(meanLong):F3} dB",$"C(1000,1005) single {c1}; sum {c2}; delta {(c1-c2).Magnitude:F4}",$"Measured ensemble RT {measured:F4}s"}.Concat(rtLogs));
    }
}
