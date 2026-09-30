using SoundstageIR.Core;
public static class EqAccuracyChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var rows=new List<string>{"case,sampleRate,oldRmsDb,newRmsDb,eqTaps,designErrorDb,maxBoostDb,minGainDb"};
        foreach(int sr in new[]{44100,48000,96000})
        {
            foreach(double scale in new[]{1e-12,1e12})
            {
                var design=EqDesigner.Design([scale],sr,24);
                check(Math.Abs(design.Impulse[0]*scale-1)<1e-9,"Uncapped uniform correction "+scale+" at "+sr);
            }
            foreach(var name in new[]{"broad boost and cut","low frequency notch"})
            {
                double Db(double f)=>name=="broad boost and cut"?-28*Math.Exp(-.5*Math.Pow(Math.Log2(Math.Max(f,1)/130)/.6,2))+26*Math.Exp(-.5*Math.Pow(Math.Log2(Math.Max(f,1)/4300)/.6,2)):-32*Math.Exp(-.5*Math.Pow(Math.Log2(Math.Max(f,1)/32)/.16,2));
                var response=Dsp.MinimumPhase(f=>Math.Pow(10,Db(f)/20),sr,(int)(32768.0*sr/48000));
                var desired=Dsp.SmoothedDbAt(response,sr,24);
                double Legacy(double f)
                {
                    double x=Math.Log(Math.Clamp(f,20,20000)/20)/Math.Log(1000)*(desired.Length-1);int j=Math.Min(desired.Length-2,(int)x);
                    return Math.Pow(10,(Math.Clamp(-desired[j],-18,6)*(1-(x-j))+Math.Clamp(-desired[j+1],-18,6)*(x-j))/20);
                }
                var old=Dsp.MinimumPhase(Legacy,sr,4096*sr/48000);
                double oldError=Math.Sqrt(Dsp.SmoothedDbAt(Dsp.Convolve(response,old),sr,24).Select(v=>v*v).Average());
                var eq=EqDesigner.Design(response,sr,24);var rpt=eq.Report;
                rows.Add($"{name},{sr},{oldError:R},{rpt.ResponseResidualDb:R},{rpt.Taps},{rpt.DesignErrorDb:R},{rpt.MaximumBoostDb:R},{rpt.MaximumCutDb:R}");
                Console.WriteLine(rows[^1]);
                check(rpt.MaximumBoostDb>6,"Boost exceeds former 6dB ceiling "+name+"/"+sr);
                if(name=="broad boost and cut")check(rpt.MaximumCutDb< -18,"Cut exceeds former -18dB ceiling "+sr);
                check(rpt.DesignErrorDb<.1,"Actual EQ follows uncapped target "+name+"/"+sr);
                check(rpt.ResponseResidualDb<.6&&rpt.ResponseResidualDb<oldError*.2,"Smoothed correction removes former clipped residual "+name+"/"+sr);
                if(name=="low frequency notch")check(rpt.Taps>4096*sr/48000,"EQ support extends for low-frequency correction "+sr);
            }
        }
        File.WriteAllLines(Path.Combine(folder,"eq-accuracy.csv"),rows);
        foreach(double ratio in new[]{0.0,16,100})
        {
            var p=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听").Create();p.ReflectionEnergyPercent=ratio;
            var result=Generator.Generate(p);check(result.Kernels.All(h=>h.All(double.IsFinite)),"Finite calibrated kernels at wet "+ratio);
            check(result.OutputReferenceDb==p.OutputDb&&result.EqReports.Count(e=>e.Taps>1)==2,"EQ preserves the requested output reference at wet "+ratio);
            string path=Exporter.Export(result,folder);var wave=Exporter.ReadWave(TestFiles.Get(path,"Matrix_PATHS_LL_RL_LR_RR.wav"));
            check(wave.Channels.Zip(result.Kernels).All(c=>c.First.SequenceEqual(c.Second)),"Calibrated WAV matches preview at wet "+ratio);
            double reference=Dsp.SmoothedDbAt(Dsp.Sum(wave.Channels[0],wave.Channels[1]),p.SampleRate,p.Smooth1).Concat(Dsp.SmoothedDbAt(Dsp.Sum(wave.Channels[2],wave.Channels[3]),p.SampleRate,p.Smooth1)).Average();
            check(Math.Abs(reference-p.OutputDb)<1e-5,"WAV retains calibration despite positive frequency peaks at wet "+ratio);
            check(result.MaxBinGainDb>0,"Peak diagnostic does not impose attenuation at wet "+ratio);
            Console.WriteLine($"OUTPUT wet={ratio}, reference={reference:R} dB, commonGain={result.CommonGainDb:R} dB, peak={result.MaxBinGainDb:R} dB");
        }
        var paths=new[]{new[]{1.0,-.5},new[]{.5,.2},new[]{.5,.2},new[]{1.0,-.5}};
        var original=paths.Select(h=>h.ToArray()).ToArray();
        var output=OutputGain.Apply(paths,6);double gain=Math.Pow(10,6.0/20);
        check(output.CommonGainDb==6&&output.FinalPeakDb>6,"Requested common gain is preserved above 0 dB peaks");
        check(paths.Zip(original).All(pair=>pair.First.Zip(pair.Second).All(v=>v.First==(double)(float)(v.Second*gain))),"Only the requested scalar is applied to all four paths");
        var requested=Presets.BuiltIn.Single(p=>p.Name=="宽阔监听").Create();requested.OutputDb=6;
        var louder=Generator.Generate(requested);
        double measured=Dsp.SmoothedDbAt(Dsp.Sum(louder.Kernels[0],louder.Kernels[1]),requested.SampleRate,requested.Smooth1).Concat(Dsp.SmoothedDbAt(Dsp.Sum(louder.Kernels[2],louder.Kernels[3]),requested.SampleRate,requested.Smooth1)).Average();
        check(Math.Abs(measured-6)<1e-5,"Output gain control is respected by generation");
        bool rejected=false;try{EqDesigner.Design([0.0],48000,24);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Silent reference is reported rather than inverted through a hidden dB floor");
        rejected=false;try{OutputGain.Apply([new[]{double.NaN},new[]{1.0},new[]{1.0},new[]{1.0}],0);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Nonfinite export is rejected");
    }
}
