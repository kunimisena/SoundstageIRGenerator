using System.Numerics;
using System.Runtime.Loader;
using SoundstageIR.Core;
public static class HeadReferenceChecks
{
    public static void BandwidthAudit(Action<bool,string> check)
    {
        var p=new Project{HeadModel=HeadModelKind.Fabian};var head=new HeadRenderer(p);
        var directions=Presets.BuiltIn.SelectMany(v=>HeadReferenceEq.Directions(v.Create()))
            .Concat(new[]{(0.0,90.0),(0.0,-90.0),(180.0,-60.0),(90.0,-60.0)}).Distinct().ToArray();
        foreach(var (az,el) in directions)
        {
            var left=head.At(az,el,0);var mirror=head.At(-az,el,1);
            check(left.Impulse.SequenceEqual(mirror.Impulse)&&left.Delay==mirror.Delay,$"Extended head mirrors sample-exactly at {az}/{el}");
        }
        var original=Presets.BuiltIn.First().Create();
        string json=ProjectIO.Serialize(original);
        string obsolete=json.Insert(json.IndexOf('{')+1,"\"FabianMagnitudeFloor\":false,\"FabianExtendBandwidth\":false,\"HeadReferenceEqualization\":false,");
        check(ProjectIO.Serialize(ProjectIO.Deserialize(obsolete))==json,"Obsolete preview switches cannot disable fixed processing or reappear in saved projects");
        var token=new CancellationTokenSource();token.Cancel();bool cancelled=false;
        try{HeadBandwidthExtension.Apply([1.0],48000,token.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled,"Bandwidth reconstruction observes cancellation");
    }
    public static void Run(Action<bool,string> check,string folder,string? auditionOracle=null)
    {
        const int sr=48000,n=8192;
        double a=.999,angle=2*Math.PI*8550/sr;double[] notch=[1,-2*a*Math.Cos(angle),a*a];
        var one=HeadReferenceEq.Build([new(notch,notch)],n,sr);
        var db=one.GainDb();var h=Dsp.Spectrum(notch,n);int first=(int)Math.Ceiling(20.0*n/sr),last=(int)Math.Floor(20000.0*n/sr);
        check(Enumerable.Range(first,last-first+1).Max(i=>Math.Abs(db[i]+20*Math.Log10(h[i].Magnitude)))<1e-9,"Single-direction unsmoothed head inverse retains narrow notch resolution");
        var negative=notch.Select(v=>-v).ToArray();var shifted=new double[notch.Length+137];Array.Copy(notch,0,shifted,137,notch.Length);
        var cancel=HeadReferenceEq.Build([new(notch,notch),new(negative,negative)],n,sr);
        check(cancel.GainDb().Zip(db).Max(v=>Math.Abs(v.First-v.Second))<1e-9,"Opposite-polarity directions do not create a spurious calibration zero");
        var delayed=HeadReferenceEq.Build([new(notch,notch),new(shifted,shifted)],n,sr);
        check(delayed.GainDb().Zip(db).Max(v=>Math.Abs(v.First-v.Second))<1e-8,"Inter-direction propagation delay cannot introduce comb correction");
        double[] l=[.8,.2],r=[1,-.1];var model=HeadReferenceEq.Build([new(notch,l),new(r,notch)],n,sr);
        var power=model.ReferencePower();var hl=Dsp.Spectrum(l,n);var hr=Dsp.Spectrum(r,n);
        check(Enumerable.Range(first,last-first+1).Max(k=>Math.Abs(power[k]-(2*Dsp.Power(h[k])+Dsp.Power(hl[k])+Dsp.Power(hr[k]))/4))<1e-10,"Reference is the direct sum of individual ear and direction powers");
        var plan=model.Plan(1024);check(Enumerable.Range(first,last-first+1).Max(k=>Math.Abs(10*Math.Log10(power[k]*Dsp.Power(plan.Spectrum[k]))))<1e-8,"Common head EQ makes noncoherent reference flat without smoothing");
        check(Enumerable.Range(first,last-first+1).Max(k=>(h[k]*plan.Spectrum[k]/(hl[k]*plan.Spectrum[k])-h[k]/hl[k]).Magnitude)<1e-10,"Common correction preserves complex binaural and directional ratios");
        var identity=HeadReferenceEq.Build([new([1.0],[1.0]),new([1.0],[1.0])],n,sr);
        check(identity.GainDb().Max(Math.Abs)<1e-10,"Unit head remains unity independently of direction count");
        var token=new CancellationTokenSource();token.Cancel();bool cancelled=false;
        try{HeadReferenceEq.Build([new(l,r)],n,sr,token.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled,"Reference calculation observes cancellation");
        var directions=Presets.BuiltIn.Single(v=>v.Name=="宽阔监听").Create();var unchanged=ProjectIO.Clone(directions);
        unchanged.Seed++;unchanged.StrictMirror=!unchanged.StrictMirror;unchanged.Smooth1=1;unchanged.ReflectionEnergyPercent=90;
        foreach(var source in unchanged.Sources){source.Left.OnsetMs+=10;foreach(var point in source.Left.Decay)point.Y*=2;source.Left.GainDb-=6;}
        check(HeadReferenceEq.Directions(directions).SequenceEqual(HeadReferenceEq.Directions(unchanged)),"Unit-impulse reference depends on enabled directions, not noise, envelopes, weights or EQ smoothing");
        var median=ProjectIO.Clone(directions);median.Sources.Clear();median.Direct.Angle=0;
        check(HeadReferenceEq.Directions(median).Length==1,"Coincident median directions are tested once");
        Complex At(double[] impulse,double f,int rate)
        {
            var step=Complex.FromPolarCoordinates(1,-2*Math.PI*f/rate);Complex z=1,sum=0;
            foreach(double sample in impulse){sum+=sample*z;z*=step;}return sum;
        }
        foreach(int rate in new[]{44100,48000,96000})foreach(var direction in new[]{(-30.0,0.0),(30.0,0.0),(90.0,0.0),(180.0,0.0),(180.0,-60.0),(90.0,60.0)})
        {
            var original=FabianData.MeasuredAt(direction.Item1,direction.Item2,0,44100,true);
            var extended=FabianData.At(direction.Item1,direction.Item2,0,rate,true);
            int size=Dsp.Pow2(rate*8);var after=Dsp.Spectrum(extended.Impulse,size);
            var frequencies=Enumerable.Range(0,1001).Select(k=>rate*.5*k/1000).Concat(new[]{0.0,5,10,19,20,8550,19500,20000,21000,22000,rate*.5});
            double error=0,bandError=0;
            foreach(double f in frequencies.Where(f=>f<=rate*.5))
            {
                int k=(int)Math.Round(f*size/rate);double actualF=k*(double)rate/size;
                double wanted=At(original.Impulse,Math.Clamp(actualF,20,20000),44100).Magnitude;
                double delta=Math.Abs(20*Math.Log10(after[k].Magnitude/wanted));error=Math.Max(error,delta);
                if(actualF>=20&&actualF<=20000)bandError=Math.Max(bandError,delta);
            }
            Console.WriteLine($"EXTEND sr={rate} direction={direction} error={error:R} bandError={bandError:R} taps={extended.Impulse.Length} peakMs={(Generator.FirstPeak(extended.Impulse)/(double)rate+extended.Delay)*1000:R}");
            check(error<.15,$"Band edges extend through Nyquist and in-band magnitudes survive {rate}/{direction}");
            check(extended.Delay==HeadBandwidthExtension.Delay(rate)&&Math.Abs(Generator.FirstPeak(extended.Impulse)/(double)rate+extended.Delay-Generator.FirstPeak(original.Impulse)/44100.0)<.0005,$"Extension retains direct timing without half-kernel delay {rate}/{direction}");
        }
        double notchAngle=2*Math.PI*50000/262144;double radius=.99999;
        double[] deepNotch=[1,-2*radius*Math.Cos(notchAngle),radius*radius];
        var retained=HeadBandwidthExtension.Apply(deepNotch,44100);
        double notchHz=44100.0*50000/262144;
        double beforeDb=20*Math.Log10(At(deepNotch,notchHz,44100).Magnitude),afterDb=20*Math.Log10(At(retained.Impulse,notchHz,44100).Magnitude);
        check(beforeDb< -90&&afterDb< -90&&Math.Abs(afterDb-beforeDb)<.15,"In-band notch below -90 dB is retained without an amplitude floor");
        var unity=HeadBandwidthExtension.Apply([1.0],48000);
        check(Dsp.Spectrum(unity.Impulse,16384).Max(v=>Math.Abs(v.Magnitude-1))<1e-8,"Unit response remains flat with only the declared common lead-in");
        var metrics=new List<object>();
        foreach(int rate in new[]{44100,48000,96000})
        {
            var p=Presets.BuiltIn.Single(v=>v.Name=="宽阔监听").Create();p.SampleRate=rate;p.Sources=p.Sources.Take(1).ToList();
            foreach(var point in p.Sources[0].Left.Decay)point.Y=.08;
            p.StrictMirror=rate==44100;p.CenterEqStrengthPercent=rate==96000?65:0;p.ReflectionEnergyPercent=35;
            var result=Generator.Generate(p);var off=ProjectIO.Clone(p);off.Equalize=false;var baseline=Generator.Generate(off);
            check(result.HeadEq.Length>1&&result.EqReports.Any(v=>v.Stage.Contains("人头 EQ")),"Head calibration appears in actual correction and reports "+rate);
            check(SpectralTestReference.SameSources(result,baseline),"Actual noise kernels and head paths preserved up to one wet scalar "+rate);
            check(result.Kernels.All(v=>v.All(double.IsFinite))&&result.ZeroSample==baseline.ZeroSample&&result.Duration==baseline.Duration,"Finite output, same support and direct zero "+rate);
            check(result.WetBalance.Converged&&Math.Abs(result.ReflectionPercentAfterEq-35)<.05,"Both EQ layers and bandpass retain final wet target "+rate);
            check(SpectralTestReference.FirstEq(result),"Per-ear EQ measures the head-corrected reference "+rate);
            if(p.CenterEqStrengthPercent>0)check(SpectralTestReference.SecondEq(result).Zip(result.SecondEq).Max(v=>Math.Abs(v.First-v.Second))<1e-9,"Center EQ measures complex sum after head and per-ear EQ");
            check(new[]{200.0,8550,12000}.All(f=>Enumerable.Range(0,4).All(c=>SpectralTestReference.Route(result,c,f))),"Exported routes include the common head correction "+rate);
            if(p.StrictMirror)check(result.Kernels[0].SequenceEqual(result.Kernels[3])&&result.Kernels[1].SequenceEqual(result.Kernels[2]),"Strict mirror remains sample-exact");
            var export=Exporter.Export(result,folder);var wav=Exporter.ReadWave(TestFiles.Get(export,"Matrix_PATHS_LL_RL_LR_RR.wav"));
            check(wav.Channels.Zip(result.Kernels).All(v=>v.First.Zip(v.Second).All(x=>(float)x.First==(float)x.Second)),"Float32 export routes match preview "+rate);
            check(ProjectIO.Serialize(ProjectIO.Load(TestFiles.Get(export,"project.json")))==ProjectIO.Serialize(p),"Saved project retains parameters without head experiment switches "+rate);
            metrics.Add(new{rate,result.Seconds,result.Duration,result.ReflectionPercentAfterEq,result.WetBalance,result.ProjectionErrorDb,result.EqReports});
        }
        var favorite=Presets.BuiltIn.Single(v=>v.Name=="宽阔监听").Create();
        var on=ProjectIO.Clone(favorite);var current=Generator.Generate(on);
        if(auditionOracle!=null)
        {
            string json=ProjectIO.Serialize(favorite);
            json=json.Insert(json.IndexOf('{')+1,"\"FabianExtendBandwidth\":true,\"HeadReferenceEqualization\":true,");
            var context=new AssemblyLoadContext("audition-preview",true);
            var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(auditionOracle));
            var legacy=assembly.GetType("SoundstageIR.Core.ProjectIO")!.GetMethod("Deserialize")!.Invoke(null,[json]);
            var previous=assembly.GetType("SoundstageIR.Core.Generator")!.GetMethod("Generate")!.Invoke(null,[legacy,null,CancellationToken.None])!;
            var kernels=(double[][])previous.GetType().GetProperty("Kernels")!.GetValue(previous)!;
            check(current.Kernels.Zip(kernels).All(v=>v.First.SequenceEqual(v.Second)),"Formal fixed processing is sample-identical to the accepted preview with both operations enabled");
            context.Unload();
        }
        check(Math.Abs(current.ReflectionPercentAfterEq-on.ReflectionEnergyPercent)<.05,"Full favorite retains final wet percentage");
        metrics.Add(new{name=favorite.Name,current.Seconds,current.Duration,current.ReflectionPercentAfterEq,current.WetBalance,current.ProjectionErrorDb,current.EqReports});
        foreach(double percent in new[]{0.0,100})
        {
            var endpoint=ProjectIO.Clone(on);endpoint.Sources=endpoint.Sources.Take(1).ToList();foreach(var point in endpoint.Sources[0].Left.Decay)point.Y=.06;endpoint.ReflectionEnergyPercent=percent;
            var result=Generator.Generate(endpoint);check(Math.Abs(result.ReflectionPercentAfterEq-percent)<1e-5&&result.HeadEq.Length>1,"Reference works at dry/wet endpoint "+percent);
        }
        foreach(string name in new[]{"自由场","悠长大厅"})
        {
            var project=Presets.BuiltIn.Single(v=>v.Name==name).Create();
            var result=Generator.Generate(project);
            check(result.HeadEq.Length>1&&result.Kernels.All(h=>h.All(double.IsFinite)),"Complete preset generates finite calibrated output: "+name);
            check(Math.Abs(result.ReflectionPercentAfterEq-project.ReflectionEnergyPercent)<.05,"Complete preset retains requested final wet percentage: "+name);
            check(result.ZeroSample==current.ZeroSample,"Longer tail adds no direct waiting time: "+name);
            metrics.Add(new{name,result.Seconds,result.Duration,result.ReflectionPercentAfterEq,result.ProjectionErrorDb});
        }
        foreach(bool bypass in new[]{true,false})
        {
            var project=Presets.BuiltIn.Single(v=>v.Name=="自由场").Create();project.Equalize=!bypass;
            if(!bypass)project.HeadModel=HeadModelKind.Sphere;
            var result=Generator.Generate(project);
            check(result.HeadEq.Length==1&&!result.EqReports.Any(r=>r.Stage.Contains("人头 EQ")),bypass?"Global EQ bypass also bypasses head correction":"Spherical head remains unchanged");
        }
        var rawHead=Presets.BuiltIn.Single(v=>v.Name=="自由场").Create();rawHead.FabianCtfCompensation=false;
        check(Generator.Generate(rawHead).Kernels.All(h=>h.All(double.IsFinite)),"Head correction also accepts FABIAN without common-transfer compensation");
        File.WriteAllText(Path.Combine(folder,"head-reference-metrics.json"),ProjectIO.Serialize(metrics));
    }
}
