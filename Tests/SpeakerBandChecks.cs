using System.Numerics;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerBandChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name=="紧凑监听"),SpeakerMode.SpatialField);
        check(p.InverseLowHz==200&&p.InverseHighHz==10000,"New presets default to 200–10000 Hz");
        check(new SpeakerProject().InverseLowHz==200&&new SpeakerProject().InverseHighHz==10000,"One uniform model default; no legacy migration branch");
        p.NoiseId=Guid.Parse("23a30b40-0997-4b01-b67d-0c862561abcd");p.Field.ReflectionEnergyPercent=16;
        p.OutsideBand!.Low.Decay.ForEach(k=>k.Y=.15);p.OutsideBand.High.Decay.ForEach(k=>k.Y=.15);
        foreach(int sr in new[]{44100,48000,96000})foreach(var (lo,hi) in new[]{(20d,20000d),(200d,10000d),(20d,10000d),(200d,20000d),(1000d,1001d)})
        {
            double error=0;for(int j=0;j<=1000;j++){var w=SpeakerBands.Weights(j*(sr*.5)/1000,sr,lo,hi);error=Math.Max(error,Math.Abs((w.Low+w.Mid+w.High).Magnitude-1));}
            check(error<1e-10,$"Three-way crossover coherent reconstruction {sr}/{lo}/{hi}");
            double powerError=0,phaseError=0,joinError=0;
            for(int j=0;j<=2000;j++)
            {
                var dry=SpeakerBands.Weights(j*(sr*.5)/2000,sr,lo,hi);var wet=SpeakerBands.ReverbWeights(dry);
                powerError=Math.Max(powerError,Math.Abs(Dsp.Power(wet.Low)+Dsp.Power(wet.Mid)+Dsp.Power(wet.High)-1));
                var outsidePhase=wet.Low*Complex.Conjugate(dry.Low);var midPhase=wet.Mid*Complex.Conjugate(dry.Mid);var highPhase=wet.High*Complex.Conjugate(dry.High);
                phaseError=Math.Max(phaseError,Math.Max(Math.Abs(highPhase.Imaginary),Math.Max(Math.Abs(outsidePhase.Imaginary),Math.Abs(midPhase.Imaginary))));
            }
            foreach(double fc in new[]{lo,hi})
            {
                double Energy(double f){var w=SpeakerBands.ReverbWeights(SpeakerBands.Weights(f,sr,lo,hi));return 4*Dsp.Power(w.Low)+Dsp.Power(w.Mid)+.25*Dsp.Power(w.High);}
                joinError=Math.Max(joinError,Math.Abs(10*Math.Log10(Energy(fc*(1+1e-6))/Energy(fc*(1-1e-6)))));
            }
            check(powerError<1e-10,$"Independent reverb power has no crossover dip {sr}/{lo}/{hi}");
            check(phaseError<1e-10&&joinError<.001,$"Wet blend keeps phases and joins unequal band energies continuously {sr}/{lo}/{hi}");
        }
        var curves=p.OutsideParameters();
        check(curves.Low.Energy.Concat(curves.Low.Decay).All(k=>k.X<=200)&&curves.High.Energy.Concat(curves.High.Decay).All(k=>k.X>=10000),"Stored curves have no inverse-band control points");
        var isolated=ProjectIO.Clone(p);string highOriginal=ProjectIO.Serialize(isolated.OutsideBand!.High);
        double lowEdge=isolated.OutsideBand.Low.Rt(200);
        isolated.InverseLowHz=1000;
        check(isolated.OutsideBand.Low.Rt(1000)==lowEdge&&ProjectIO.Serialize(isolated.OutsideBand.High)==highOriginal,"Expanding low branch extends its edge and leaves high branch intact");
        isolated.InverseLowHz=20;isolated.InverseHighHz=20000;isolated.Validate();
        check(isolated.OutsideBand.Low.Energy.Count==1&&isolated.OutsideBand.High.Decay.Count==1,"Disabled branches retain only one endpoint");
        isolated.InverseLowHz=200;isolated.InverseHighHz=10000;isolated.Validate();
        check(isolated.OutsideBand.Low.Decay.Count==2&&isolated.OutsideBand.High.Energy.Count==2,"Reopened branches use only their own endpoint continuation");
        var independent=ProjectIO.Clone(p);independent.OutsideBand!.High.Decay.ForEach(k=>k.Y=20);independent.OutsideBand.High.Energy.ForEach(k=>k.Y=-50);
        var lowKernel=NoiseKernel.Generate(curves.Low,48000,p.Field.Seed,p.NoiseId,40);
        check(lowKernel.SequenceEqual(NoiseKernel.Generate(independent.OutsideBand.Low,48000,p.Field.Seed,p.NoiseId,40)),"High-side energy and decay cannot alter low-side noise, duration or phase");
        var a=SpeakerGenerator.Generate(p);var b=SpeakerGenerator.Generate(p);
        check(a.BandResult!=null&&a.Drive.Zip(b.Drive).All(v=>v.First.SequenceEqual(v.Second)),"Hybrid output is deterministic");
        check(Math.Abs(a.WetPercent-16)<.05,"Final predicted ear wet fraction is 16 percent");
        check(a.Drive.SelectMany(v=>v).All(double.IsFinite),"Hybrid kernels finite");
        var band=a.BandResult!;
        check(Math.Abs(a.WetPercent-EnergyBalance.Percent(EnergyBalance.Energy(band.PredictedDry,48000),EnergyBalance.Energy(band.PredictedWet,48000)))<1e-10,"Displayed fraction comes from actual predicted components");
        check(band.Predicted.Zip(band.PredictedDry).Zip(band.PredictedWet).All(v=>v.First.First.Zip(v.First.Second).Zip(v.Second).All(s=>Math.Abs(s.First.First-s.First.Second-s.Second)<1e-12)),"Direct and wet sum to actual predicted result");
        check(a.InverseProjectionDb< -50,"Causal projection does not discard material component energy");
        Console.WriteLine($"BAND wet={a.WetPercent:F6}, error={a.RelativeErrorDb:F3}, delay={a.LatencySamples/48.0:F2} ms, projection={a.InverseProjectionDb:F2}, duration={a.Duration:F2}");
        string export=SpeakerExporter.Export(a,folder);var json=Directory.GetFiles(export,"*speaker-project.json").Single();var load=SpeakerProject.Load(json);
        check(ProjectIO.Serialize(load)==ProjectIO.Serialize(a.Project),"Crossovers and both curves round-trip through project JSON");
        for(int c=0;c<4;c++)check(Exporter.ReadWave(Directory.GetFiles(export,"*"+SpeakerExporter.Routes[c]+".wav").Single()).Channels[0].SequenceEqual(a.Drive[c]),"Exported float32 path equals preview "+c);
        var changed=ProjectIO.Clone(p);changed.OutsideBand!.Low.Energy= [new(20,-18),new(80,-18),new(200,0)];
        var lower=SpeakerGenerator.Generate(changed);
        double Ratio(SpeakerResult r){var z=r.BandResult!;double Power(double[][] h)=>h.Sum(c=>Dsp.PowerAt(c,48000,[30,40,60,80]).Sum());return Power(z.PredictedWet)/Power(z.PredictedDry);}
        check(Ratio(lower)<Ratio(a)*.4,"Lower outside-band bass curve reduces final local wet/dry power ratio");
        check(Math.Abs(lower.WetPercent-16)<.05,"Spectrum redistribution preserves total wet allocation");
        foreach(var (sr,wet,enabled,lo,hi) in new[]{(44100,0d,true,20d,10000d),(96000,100d,true,200d,20000d),(48000,16d,false,200d,10000d)})
        {
            var q=ProjectIO.Clone(p);q.Field.SampleRate=sr;q.Field.ReflectionEnergyPercent=wet;q.Field.Direct.Enabled=enabled;q.InverseLowHz=lo;q.InverseHighHz=hi;
            var r=SpeakerGenerator.Generate(q);double expected=enabled?wet:100;
            check(Math.Abs(r.WetPercent-expected)<.05,$"Dry/wet endpoint {sr}/{wet}/{enabled}");
            check(r.Drive.SelectMany(v=>v).All(double.IsFinite),"Endpoint finite "+sr);
            if(wet==0)check(new[]{60d,1000,15000}.All(f=>r.Drive.Sum(h=>Dsp.PowerAt(h,sr,[f])[0])>1e-8),"Zero wet keeps direct playback in every band");
        }
        var full=ProjectIO.Clone(p);full.InverseLowHz=20;full.InverseHighHz=20000;var old=SpeakerGenerator.Generate(full);
        foreach(var e in new[]{full.OutsideBand!.Low,full.OutsideBand.High}){e.Energy.ForEach(k=>k.Y=-100);e.Decay.ForEach(k=>k.Y=8);}var unused=SpeakerGenerator.Generate(full);
        check(old.BandResult==null&&old.Drive.Zip(unused.Drive).All(v=>v.First.SequenceEqual(v.Second)),"20–20000 Hz bypasses all outside settings exactly");
        bool canceled=false;try{SpeakerGenerator.Generate(p,null,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}check(canceled,"Hybrid cancellation");
        bool rejected=false;p.InverseLowHz=p.InverseHighHz;try{p.Validate();}catch(ArgumentException){rejected=true;}check(rejected,"Invalid overlapping crossover order rejected");
    }
}
