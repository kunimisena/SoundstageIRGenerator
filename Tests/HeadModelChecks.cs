using System.Numerics;
using SoundstageIR.Core;

public static class HeadModelChecks
{
    static Complex At(double[] h,double f,int sr)
    {Complex z=0;for(int n=0;n<h.Length;n++)z+=h[n]*Complex.FromPolarCoordinates(1,-2*Math.PI*f*n/sr);return z;}
    static Complex At(EarTransfer h,double f,int sr)=>At(h.Impulse,f,sr)*Complex.FromPolarCoordinates(1,-2*Math.PI*f*h.Delay);
    static void CheckMeasuredMirror(Action<bool,string> check)
    {
        // Read the embedded original samples independently of the rendering method.
        using var stream=typeof(FabianData).Assembly.GetManifestResourceStream("SoundstageIR.Core.FABIAN.bin")!;
        using var reader=new BinaryReader(stream);reader.ReadBytes(8);int sr=reader.ReadInt32(),count=reader.ReadInt32(),taps=reader.ReadInt32(),ctf=reader.ReadInt32();reader.ReadBytes(ctf*4);
        var originals=new List<(double Az,double El,double[] Left)>();
        for(int i=0;i<count;i++)
        {
            double az=reader.ReadSingle(),el=reader.ReadSingle();var left=new double[taps];
            for(int j=0;j<taps;j++)left[j]=reader.ReadSingle();reader.ReadBytes(taps*4);originals.Add((az,el,left));
        }
        double[] Original(double appAz,double el,int ear)
        {
            double az=ear==0?-appAz:appAz;
            if(Math.Abs(Math.Sin(appAz*Math.PI/180)*Math.Cos(el*Math.PI/180))<1e-9)az=Math.Abs(appAz);
            double a=az*Math.PI/180,e=el*Math.PI/180;
            return originals.MaxBy(d=>Math.Sin(e)*Math.Sin(d.El*Math.PI/180)+Math.Cos(e)*Math.Cos(d.El*Math.PI/180)*Math.Cos(a-d.Az*Math.PI/180)).Left;
        }
        foreach(var (az,el) in new[]{(-60.0,0.0),(60.0,0.0),(0.0,0.0),(180.0,0.0),(-30.0,20.0),(30.0,20.0),(-45.0,60.0),(0.0,90.0)})
            for(int ear=0;ear<2;ear++)
            {
                var rendered=FabianData.MeasuredAt(az,el,ear,sr,false);var original=Original(az,el,ear);
                check(rendered.Impulse.SequenceEqual(original)&&rendered.Delay==0,$"Native single-ear mirror preserves every measured sample and time reference {az}/{el}/{ear}");
            }
        var same=FabianData.MeasuredAt(-60,0,0,sr,false);double db=20*Math.Log10(At(same,8265,sr).Magnitude);
        check(db>-.3&&db<0,"8.265 kHz retains original near-unity response instead of the averaging-induced -14.75 dB notch");
        var frontLeft=FabianData.MeasuredAt(0,0,0,sr,false);var frontRight=FabianData.MeasuredAt(0,0,1,sr,false);
        check(frontLeft.Impulse.SequenceEqual(frontRight.Impulse),"Median response is copied from one ear without averaging");
        same.Impulse[0]+=1;
        check(FabianData.MeasuredAt(-60,0,0,sr,false).Impulse.SequenceEqual(Original(-60,0,0)),"Returned IR cannot modify embedded reference data");
    }
    public static void Run(Action<bool,string> check,string folder)
    {
        CheckMeasuredMirror(check);
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
                var native=FabianData.MeasuredAt(az,el,0,44100,true);
                foreach(double f in new[]{100.0,800,4000,10000,20000})
                {
                    var a=At(native,f,44100);var b=At(l,f,sr);
                    check(Math.Abs(20*Math.Log10(a.Magnitude/b.Magnitude))<.15,$"Extended head preserves in-band measured magnitude {sr}/{az}/{el}/{f}");
                }
            }
            var same=model.At(-30,0,0);var opposite=model.At(-30,0,1);
            check(Generator.FirstPeak(same.Impulse)<Generator.FirstPeak(opposite.Impulse),$"Left speaker reaches left ear first {sr}");
            var raw=FabianData.At(-30,0,0,sr,false);var crossRaw=FabianData.At(-30,0,1,sr,false);
            foreach(double f in new[]{100.0,800,8000,18000})
            {
                var ratio=At(same,f,sr)/At(raw,f,sr);var cross=At(opposite,f,sr)/At(crossRaw,f,sr);
                check(Math.Abs(20*Math.Log10(ratio.Magnitude/cross.Magnitude))<.15,$"CTF retains the common in-band magnitude ratio {sr}/{f}");
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
                check(SpectralTestReference.SameSources(dry,strict),"EQ bypass leaves head and random kernels unchanged");
                for(int ch=0;ch<4;ch++)foreach(double f in new[]{103.0,801,7523})
                {
                    check(SpectralTestReference.Route(strict,ch,f),$"FABIAN actual exported EQ chain {ch}/{f}");
                }
                var old=Generator.Generate(sphere);
                check(old.Contributions[0].LeftInputKernel.SequenceEqual(strict.Contributions[0].LeftInputKernel),"Head switch preserves source random realization");
                check(!old.DirectPaths[0].SequenceEqual(strict.DirectPaths[0]),"Head switch actually changes direct response");
                noeq.StrictMirror=false;var independent=Generator.Generate(noeq);
                check(!independent.Raw[0].SequenceEqual(independent.Raw[3]),"Non-strict reflection randomness remains independent");
                check(independent.DirectPaths.Zip(dry.DirectPaths).All(v=>v.First.SequenceEqual(v.Second)),"Random mirror mode does not change physical head or direct sound");
                noeq.Equalize=true;noeq.CenterEqStrengthPercent=100;var two=Generator.Generate(noeq);
                var expectedEq=SpectralTestReference.SecondEq(two);
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
