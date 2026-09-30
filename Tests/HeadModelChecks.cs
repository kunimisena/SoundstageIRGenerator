using System.Numerics;
using StatisticalField.Core;

public static class HeadModelChecks
{
    static Complex At(double[] h,double f,int sr)
    {Complex z=0;for(int n=0;n<h.Length;n++)z+=h[n]*Complex.FromPolarCoordinates(1,-2*Math.PI*f*n/sr);return z;}
    static Complex At(EarTransfer h,double f,int sr)=>At(h.Impulse,f,sr)*Complex.FromPolarCoordinates(1,-2*Math.PI*f*h.Delay);
    public static void Run(Action<bool,string> check,string folder)
    {
        check(FabianData.DirectionCount==11950,"FABIAN embedded source direction count");
        var sphere=Presets.BuiltIn[0].Create();sphere.HeadModel=HeadModelKind.Sphere;sphere.Sources=sphere.Sources.Take(1).ToList();sphere.Seed=20260920;
        var p=ProjectIO.Clone(sphere);p.HeadModel=HeadModelKind.Fabian;p.FabianCtfCompensation=true;
        check(ProjectIO.Deserialize(ProjectIO.Serialize(p)).HeadModel==HeadModelKind.Fabian,"FABIAN selection JSON roundtrip");
        check(ProjectIO.Deserialize(ProjectIO.Serialize(p)).FabianCtfCompensation,"CTF selection JSON roundtrip");
        double maxAngle=0;
        for(int i=0;i<500;i++)maxAngle=Math.Max(maxAngle,FabianData.AngularError(-180*(i*.61803398875%1),Math.Asin(2*(i+.5)/500-1)*180/Math.PI));
        check(maxAngle<1.6,$"Dense-grid angular error {maxAngle:F3} degrees");
        foreach(int sr in new[]{44100,48000,96000})
        {
            var pp=ProjectIO.Clone(p);pp.SampleRate=sr;var model=new HeadRenderer(pp);
            foreach(var (az,el) in new[]{(-30.0,0.0),(-140.0,0.0),(-45.0,60.0),(-80.0,-80.0),(0.0,90.0)})
            {
                var l=model.At(az,el,0);var r=model.At(-az,el,1);
                check(l.Impulse.SequenceEqual(r.Impulse)&&l.Delay==r.Delay,$"Physical head mirror {sr}/{az}/{el}");
                check(l.Impulse.All(double.IsFinite),$"Finite full-sphere response {sr}/{az}/{el}");
                var native=FabianData.At(az,el,0,44100,true);
                foreach(double f in new[]{100.0,800,4000,10000,20000})
                {
                    var a=At(native,f,44100);var b=At(l,f,sr);
                    check((a-b).Magnitude/Math.Max(.02,a.Magnitude)<.003,$"Resampling preserves COMPLEX transfer {sr}/{az}/{el}/{f}");
                }
            }
            var same=model.At(-30,0,0);var opposite=model.At(-30,0,1);
            check(Generator.FirstPeak(same.Impulse)<Generator.FirstPeak(opposite.Impulse),$"Left speaker reaches left ear first {sr}");
            var raw=FabianData.At(-30,0,0,sr,false);var crossRaw=FabianData.At(-30,0,1,sr,false);
            foreach(double f in new[]{100.0,800,8000,18000})
            {
                var ratio=At(same,f,sr)/At(raw,f,sr);var cross=At(opposite,f,sr)/At(crossRaw,f,sr);
                check((ratio-cross).Magnitude<.0001,$"CTF is common to ears, preserving binaural ratio {sr}/{f}");
            }
            pp.StrictMirror=true;var strict=Generator.Generate(pp);
            check(strict.Raw[0].SequenceEqual(strict.Raw[3])&&strict.Raw[1].SequenceEqual(strict.Raw[2]),$"Strict raw mirror {sr}");
            check(strict.DirectPaths[0].SequenceEqual(strict.DirectPaths[3])&&strict.DirectPaths[1].SequenceEqual(strict.DirectPaths[2]),$"Direct mirror before final forced symmetry {sr}");
            var c=strict.Contributions.Single().EarPaths;
            check(c[0].Zip(c[3]).All(v=>Math.Abs(v.First-v.Second)<1e-12)&&c[1].Zip(c[2]).All(v=>Math.Abs(v.First-v.Second)<1e-12),$"Reflection routing mirror before final symmetry {sr}");
            check(Math.Abs(Generator.FirstPeak(strict.DirectPaths[0])-strict.ZeroSample)<=1,$"Shared direct peak time origin {sr}");
            check(strict.ZeroSample/sr<.004,$"FABIAN lead-in below 4 ms {sr}");
            check(strict.Kernels.All(x=>x.All(double.IsFinite)),$"Finite generated final FABIAN kernels {sr}");
            var dir=Exporter.Export(strict,Path.Combine(folder,"fabian-wav"));var wav=Exporter.ReadWave(TestFiles.Get(dir,"Matrix_PATHS_LL_RL_LR_RR.wav"));
            check(wav.Channels.Zip(strict.Kernels).All(v=>v.First.SequenceEqual(v.Second)),$"Exact exported samples and routing {sr}");
            check(File.Exists(Path.Combine(dir,"FABIAN-NOTICE.txt")),$"Export includes attribution {sr}");
            if(sr==48000)
            {
                var noeq=ProjectIO.Clone(pp);noeq.Equalize=false;var dry=Generator.Generate(noeq);
                check(dry.Raw.Zip(strict.Raw).All(v=>v.First.SequenceEqual(v.Second)),"EQ bypass leaves head and random kernels unchanged");
                for(int ch=0;ch<4;ch++)foreach(double f in new[]{103.0,801,7523})
                {
                    var expected=At(strict.Raw[ch],f,sr)*At(strict.EarEq[ch/2],f,sr)*At(strict.SecondEq,f,sr)*At(strict.Bandpass,f,sr)*Math.Pow(10,strict.CommonGainDb/20);
                    check((expected-At(strict.Kernels[ch],f,sr)).Magnitude<1e-5*Math.Max(1,expected.Magnitude),$"FABIAN actual exported EQ chain {ch}/{f}");
                }
                var old=Generator.Generate(sphere);
                check(old.Contributions[0].LeftInputKernel.SequenceEqual(strict.Contributions[0].LeftInputKernel),"Head switch preserves source random realization");
                check(!old.DirectPaths[0].SequenceEqual(strict.DirectPaths[0]),"Head switch actually changes direct response");
                noeq.StrictMirror=false;var independent=Generator.Generate(noeq);
                check(!independent.Raw[0].SequenceEqual(independent.Raw[3]),"Non-strict reflection randomness remains independent");
                check(independent.DirectPaths.Zip(dry.DirectPaths).All(v=>v.First.SequenceEqual(v.Second)),"Random mirror mode does not change physical head or direct sound");
                noeq.Equalize=true;noeq.CenterEqStrengthPercent=100;var two=Generator.Generate(noeq);
                var left=Dsp.Convolve(Dsp.Sum(two.Raw[0],two.Raw[1]),two.EarEq[0]);var right=Dsp.Convolve(Dsp.Sum(two.Raw[2],two.Raw[3]),two.EarEq[1]);
                var expectedEq=Dsp.DesignEq(Dsp.Sum(left,right,.5),sr,noeq.Smooth2);
                check(expectedEq.Zip(two.SecondEq).Max(v=>Math.Abs(v.First-v.Second))<1e-10,"FABIAN two-stage EQ uses corrected complex ear sum");
                var apo=ApoExporter.Export(two,Path.Combine(folder,"fabian-apo"));
                check(File.Exists(Path.Combine(apo,"FABIAN-NOTICE.txt")),"APO export includes head attribution");
                for(int route=0;route<4;route++)check(Exporter.ReadWave(TestFiles.Get(apo,Generator.RouteNames[route]+".wav")).Channels[0].SequenceEqual(two.Kernels[route]),"FABIAN APO path "+route);
                foreach(var k in noeq.Sources[0].Left.Decay)k.Y=3.2;noeq.Equalize=false;
                var longTail=Generator.Generate(noeq);check(longTail.ZeroSample==independent.ZeroSample,"Longer reflection tail does not add head latency");
                ProjectIO.Save(pp,Path.Combine(folder,"fabian-project.json"));
                File.WriteAllText(Path.Combine(folder,"fabian-metrics.json"),ProjectIO.Serialize(new {maxAngularErrorDegrees=maxAngle,zeroSamples=strict.ZeroSample,zeroMs=1000*strict.ZeroSample/sr,strict.Seconds,strict.EqResidualDb,strict.ReflectionPercentBeforeEq,strict.ReflectionPercentAfterEq}));
            }
        }
    }
}
