using SoundstageIR.Core;
using System.Numerics;
public static class WetBalanceChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var rows=new List<object>();
        foreach(int sr in new[]{44100,48000,96000})
        {
            var p=Presets.BuiltIn.Single(x=>x.Name=="宽阔监听").Create();p.SampleRate=sr;p.ReflectionEnergyPercent=65;
            p.Sources=p.Sources.Take(1).ToList();p.StrictMirror=sr==44100;p.CenterEqStrengthPercent=sr==96000?60:0;
            var r=Generator.Generate(p);
            check(r.WetBalance.Converged&&Math.Abs(r.ReflectionPercentAfterEq-65)<.02,"Final wet target with actual EQ and bandpass "+sr);
            check(Math.Abs(r.WetBalance.PredictedPercent-r.ReflectionPercentAfterEq)<.02,"Spectral prediction matches finite rendered kernels "+sr);
            check(Math.Abs(r.ReflectionPercentBeforeEq-65)>.1,"Pre-EQ ratio really precompensates spectral bias "+sr);
            Console.WriteLine($"MEASURE sr={sr}, projection={r.ProjectionErrorDb:R}, discarded={r.DiscardedEnergyDb:R}, before={r.ReflectionPercentBeforeEq:R}, after={r.ReflectionPercentAfterEq:R}, gain={r.WetBalance.GainDb:R}, passes={r.WetBalance.Evaluations}"); check(double.IsFinite(r.ProjectionErrorDb)&&r.Kernels.All(h=>h.All(double.IsFinite))&&(r.ProjectionErrorDb<=.1||r.Warnings.Any(w=>w.Contains("有限长度合成"))),"Finite target projection is measured and any support residual reported "+sr);
            if(p.StrictMirror)check(r.Kernels[0].SequenceEqual(r.Kernels[3])&&r.Kernels[1].SequenceEqual(r.Kernels[2]),"Scalar wet correction preserves exact mirror");
            var bypass=ProjectIO.Clone(p);bypass.Equalize=false;var b=Generator.Generate(bypass);
            check(Math.Abs(b.ReflectionPercentAfterEq-65)<.02,"EQ bypass still accounts for final bandpass "+sr);
            check(SpectralTestReference.SameSources(r,b),"All reflection samples differ ONLY by a single shared gain; phase, spectral shape and envelope preserved "+sr);
            var recomposed=Generator.NewPaths(1);Generator.Accumulate(recomposed,r.DirectPaths);foreach(var c in r.Contributions)Generator.Accumulate(recomposed,c.EarPaths);Generator.Pad(recomposed);
            check(recomposed.Zip(r.Raw).All(v=>v.First.Zip(v.Second).All(x=>Math.Abs(x.First-x.Second)<1e-10)),"Raw analysis and source plots include actual scalar correction "+sr);
            var output=Exporter.Export(r,folder);var wave=Exporter.ReadWave(TestFiles.Get(output,"Matrix_PATHS_LL_RL_LR_RR.wav"));
            var measured=EnergyBalance.Percent(EnergyBalance.Energy(r.FinalDirectPaths,sr),EnergyBalance.Energy(EnergyBalance.Subtract(wave.Channels,r.FinalDirectPaths),sr));
            check(Math.Abs(measured-65)<.02,"Reread float32 WAV meets final component ratio "+sr);
            check(File.ReadAllText(TestFiles.Get(output,"metadata.json")).Contains("WetBalance"),"Export records scalar correction and convergence "+sr);
            rows.Add(new{sr,target=65,r.ReflectionPercentBeforeEq,r.ReflectionPercentAfterEq,r.WetBalance,r.Seconds,r.EqResidualDb});
        }
        var favorite=Presets.BuiltIn.Single(x=>x.Name=="宽阔监听").Create();var one=Generator.Generate(favorite);var two=Generator.Generate(favorite);
        check(one.Kernels.Zip(two.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Solver and final output remain deterministic");
        check(Math.Abs(one.ReflectionPercentAfterEq-favorite.ReflectionEnergyPercent)<.02,"Full recommended preset reaches final target");
        var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
        try{WetBalanceSolver.Solve(favorite,one.DirectPaths,EnergyBalance.Subtract(one.Raw,one.DirectPaths),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
        check(cancelled,"Precompensation observes cancellation");
        foreach(double target in new[]{0.0,100})
        {
            var p=ProjectIO.Clone(favorite);p.ReflectionEnergyPercent=target;var r=Generator.Generate(p);
            check(Math.Abs(r.ReflectionPercentAfterEq-target)<1e-5&&r.WetBalance.Evaluations==0,"Pure component endpoint needs no solver "+target);
        }
        foreach(var name in new[]{"自由场","悠长大厅"})
        {
            var p=Presets.BuiltIn.Single(x=>x.Name==name).Create();var r=Generator.Generate(p);
            double target=name=="自由场"?0:p.ReflectionEnergyPercent;
            check(Math.Abs(r.ReflectionPercentAfterEq-target)<.02,"Full preset final fraction "+name);
            rows.Add(new{name,target,r.ReflectionPercentBeforeEq,r.ReflectionPercentAfterEq,r.WetBalance,r.Seconds,r.Duration,r.EqResidualDb});
        }
        File.WriteAllText(Path.Combine(folder,"wet-balance-metrics.json"),ProjectIO.Serialize(rows));
    }
}
