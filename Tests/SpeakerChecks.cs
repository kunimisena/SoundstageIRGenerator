using System.Numerics;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        foreach(double f in new[]{0.0,20,100,800,8000,20000})foreach(double angle in new[]{-1.0,0,1})
        {
            check((SphereRange.Ratio(f,.0875,1.7,angle)-Complex.One).Magnitude<1e-12,"Sphere reference identity");
            check(double.IsFinite(SphereRange.Ratio(f,.0875,.3,angle).Magnitude),"Sphere near-field finite");
        }
        double Static(double r,double c){double total=1,prev=1,poly=c;for(int n=1;n<90;n++){double p=n==1?c:((2*n-1)*c*poly-(n-1)*prev)/n;if(n>=2){prev=poly;poly=p;}total+=(2.0*n+1)/(n+1)*Math.Pow(.0875/r,n)*p;}return total;}
        foreach(double angle in new[]{-1.0,0,1})check(Math.Abs(SphereRange.Ratio(.1,.0875,.3,angle).Magnitude-Static(.3,angle)/Static(1.7,angle))<1e-5,"Sphere matches independent static-limit expansion");
        var nearLeft=SphereRange.Ratio(100,.0875,.3,1);var nearRight=SphereRange.Ratio(100,.0875,.3,-1);
        check(nearLeft.Magnitude>nearRight.Magnitude,"Near-field increases ipsilateral low-frequency level");
        foreach(double gain in new[]{1.0,4,16})
        {
            var q=SpeakerInverse.Solve(1,1,1,1,gain);check(q.All(v=>double.IsFinite(v.Magnitude)),"Singular playback remains finite");
            for(int k=0;k<20;k++){double v=k*.1;var d=SpeakerInverse.Solve(v,0,0,v,gain);check(d[0].Magnitude<=gain+1e-10,"Regularized inverse singular gain bound");}
        }
        var p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(v=>v.Name=="自由场"),SpeakerMode.SpatialField);p.Field.Equalize=false;p.MaximumInverseGainDb=18;p.InverseLowHz=20;p.InverseHighHz=20000;
        var g=SpeakerPlayback.Build(p);check(g[0].SequenceEqual(g[3])&&g[1].SequenceEqual(g[2]),"Playback exact mirror");
        var inverse=SpeakerInverse.Design(g,48000,18,suppressNarrowNotches:false);var reconstructed=SpeakerInverse.Multiply(g,SpeakerInverse.Multiply(inverse.Kernels,g));
        double identity=SpeakerGenerator.RelativeError(reconstructed,g,inverse.Delay,48000);
        Console.WriteLine($"Identity residual {identity:F2} dB, delay {1000.0*inverse.Delay/48000:F2} ms, projection {inverse.ProjectionErrorDb:F2} dB");
        check(identity<-20,"G inverse G recovers ordinary speaker response");check(inverse.ProjectionErrorDb<-50,"Causal inverse projection residual");
        var narrow=Dsp.MinimumPhase(f=>1-.98*Math.Exp(-.5*Math.Pow((f-5000)/30,2)),48000,4096);
        double[][] synthetic=[[1],[0],[0],narrow];
        var rawInverse=SpeakerInverse.Design(synthetic,48000,24,suppressNarrowNotches:false);
        var smoothInverse=SpeakerInverse.Design(synthetic,48000,24);
        double[] local=Enumerable.Range(0,401).Select(i=>4800.0+i).ToArray();
        double rawBoost=Dsp.Db(Dsp.PowerAt(rawInverse.Kernels[3],48000,local).Max()),protectedBoost=Dsp.Db(Dsp.PowerAt(smoothInverse.Kernels[3],48000,local).Max());
        Console.WriteLine($"Narrow-notch inverse peak: {rawBoost:F2} -> {protectedBoost:F2} dB");
        check(rawBoost-protectedBoost>10&&protectedBoost<3,"Narrow head notch does not create a high-Q inverse boost");
        var result=SpeakerGenerator.Generate(p);check(result.Drive.All(h=>h.All(double.IsFinite)),"Complex exported kernels finite");
        check(result.Drive[0].SequenceEqual(result.Drive[3])&&result.Drive[1].SequenceEqual(result.Drive[2]),"Symmetric target and playback preserve drive mirror");
        Console.WriteLine($"Target reconstruction residual {result.RelativeErrorDb:F2} dB");
        var moved=ProjectIO.Clone(p);moved.LeftSpeaker.Distance=.8;moved.RightSpeaker.Distance=1.1;moved.LeftSpeaker.Elevation=10;
        var mg=SpeakerPlayback.Build(moved);check(!mg[0].SequenceEqual(g[0]),"Actual distance and elevation affect playback");
        check(Peak(mg[1])>Peak(mg[0]),"Longer right path arrives later in left ear");
        var shifted=ProjectIO.Clone(p);shifted.LeftSpeaker.Azimuth+=2;shifted.RightSpeaker.Azimuth+=2;var sg=SpeakerPlayback.Calibrated(shifted).Kernels;double off=SpeakerGenerator.RelativeError(SpeakerInverse.Multiply(sg,result.Drive),result.Target,result.LatencySamples,48000);Console.WriteLine($"Head orientation mismatch residual {off:F2} dB");check(off>result.RelativeErrorDb+3,"Independent head-angle mismatch worsens reconstruction");
        var simple=SpeakerProject.FromPreset(Presets.BuiltIn.Single(v=>v.Name=="紧凑监听"),SpeakerMode.SimpleReverb);simple.Left.Decay.ForEach(k=>k.Y=.08);
        var a=SpeakerGenerator.Generate(simple);var b=SpeakerGenerator.Generate(simple);
        check(a.Drive.Zip(b.Drive).All(v=>v.First.SequenceEqual(v.Second)),"Simple deterministic seed");check(a.Drive[1].All(v=>v==0)&&a.Drive[2].All(v=>v==0),"Simple exactly two diagonal routes");
        check(!a.Drive[0].SequenceEqual(a.Drive[3]),"Linked parameters retain independent random realizations");
        check(Math.Abs(a.WetPercent-simple.Field.ReflectionEnergyPercent)<.2,"Simple post-EQ wet energy preserved");
        foreach(var r in new[]{result,a})foreach(var lang in new[]{"zh-CN","en"})
        {
            TextCatalog.SetLanguage(lang);string output=SpeakerExporter.Export(r,folder);var config=Directory.GetFiles(output,"*Equalizer_APO*.txt").Single();
            var txt=File.ReadAllText(config);check(txt.Contains("LeftSpeaker.wav")&&txt.Contains("RightSpeaker.wav"),"APO speaker routes named clearly");
            foreach(int route in r.Project.Mode==SpeakerMode.SimpleReverb?new[]{0,3}:new[]{0,1,2,3}){var wav=Exporter.ReadWave(Directory.GetFiles(output,"*"+SpeakerExporter.Routes[route]+".wav").Single());check(wav.Channels[0].SequenceEqual(r.Drive[route]),"Float WAV equals plotted/exported samples");}
            var loaded=SpeakerProject.Load(Directory.GetFiles(output,"*speaker-project.json").Single());check(ProjectIO.Serialize(loaded)==ProjectIO.Serialize(r.Project),"Speaker project roundtrip");
        }
        foreach(int sr in new[]{44100,96000})
        {
            var s=ProjectIO.Clone(simple);s.Field.SampleRate=sr;s.Field.Equalize=false;s.Field.ReflectionEnergyPercent=0;var r=SpeakerGenerator.Generate(s);check(r.SampleRate==sr&&r.Drive[0].All(double.IsFinite),"Simple alternate sample rate "+sr);
        }
        var cancel=new CancellationToken(true);bool stopped=false;try{SpeakerGenerator.Generate(simple,null,cancel);}catch(OperationCanceledException){stopped=true;}check(stopped,"Speaker cancellation");
        static int Peak(double[] h)=>Array.IndexOf(h,h.MaxBy(Math.Abs));
    }
}
