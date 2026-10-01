using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerCascadeChecks
{
    public static void Run(Action<bool,string> check,string folder)
    {
        var p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name=="自由场"),SpeakerMode.SpatialField);
        check(p.AirAbsorption,"Actual-speaker air absorption defaults on");
        var dry=ProjectIO.Clone(p);dry.AirAbsorption=false;
        var dryNear=SpeakerPlayback.Calibrated(dry);dry.LeftSpeaker.Distance=dry.RightSpeaker.Distance=4;
        var dryFar=SpeakerPlayback.Calibrated(dry);
        check(dryNear.Kernels.Zip(dryFar.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Mirrored distance has no centre-response effect with air off");
        var wet=ProjectIO.Clone(dry);wet.AirAbsorption=true;wet.Field.Equalize=false;
        var wetRaw=SpeakerPlayback.Build(wet);var dryRaw=SpeakerPlayback.Build(dry);
        var wetHigh=Dsp.PowerAt(wetRaw[0],48000,new[]{16000.0})[0];var dryHigh=Dsp.PowerAt(dryRaw[0],48000,new[]{16000.0})[0];
        check(wetHigh<dryHigh*.99,"Actual-speaker air absorption attenuates raw high frequencies");
        var json=System.Text.Json.JsonSerializer.Deserialize<SpeakerProject>(ProjectIO.Serialize(dry),ProjectIO.Options)!;
        check(!json.AirAbsorption,"Actual-speaker air switch survives configuration round trip");
        var ordinaryPre=SpeakerRiskCheck.Precheck(p);Console.WriteLine($"Freefield probe {ordinaryPre.RequiredGainDb:F2} dB, flags {ordinaryPre.Issues.Count}");
        check(!ordinaryPre.Issues.Any(x=>x.Code=="demand"),"Matching free-field directions do not trigger demand warning");
        var narrow=ProjectIO.Clone(p);narrow.LeftSpeaker.Azimuth=-10;narrow.RightSpeaker.Azimuth=10;narrow.Field.Direct.Angle=90;
        var narrowPre=SpeakerRiskCheck.Precheck(narrow);Console.WriteLine($"10 to 90 degree probe {narrowPre.RequiredGainDb:F2} dB @ {narrowPre.CriticalHz:0} Hz");
        check(narrowPre.HasRisk&&narrowPre.RequiredGainDb>ordinaryPre.RequiredGainDb+6,"Narrow playback to wide target is flagged before synthesis");
        var near=ProjectIO.Clone(p);near.LeftSpeaker.Distance=.25;check(SpeakerRiskCheck.Precheck(near).Issues.Any(x=>x.Code=="near"),"Near-field approximation is highlighted");
        var same=ProjectIO.Clone(p);same.RightSpeaker=ProjectIO.Clone(same.LeftSpeaker);check(SpeakerRiskCheck.Precheck(same).Issues.Any(x=>x.Code=="rank"),"Coincident geometry is highlighted");
        var f=SpeakerPlayback.Calibrated(p);var ordinary=Generator.Generate(SpeakerPlayback.FreeFieldProject(p));
        check(f.Kernels.Zip(ordinary.Kernels).All(x=>x.First.SequenceEqual(x.Second)),"F exactly equals original calibrated headphone free field");
        check(f.HeadEq.Length>1&&f.EarEq[0].Length>1,"Playback includes head-power and smooth EQ");
        p.Field=SpeakerPlayback.FreeFieldProject(p);
        var identity=SpeakerGenerator.Generate(p);
        Console.WriteLine($"Identity residual {identity.RelativeErrorDb:F2} dB, delay {identity.LatencySamples} samples");
        check(identity.RelativeErrorDb< -110,"Identical F and T give delayed identity without coloration");
        check(identity.Drive[1].Max(Math.Abs)<1e-10&&identity.Drive[2].Max(Math.Abs)<1e-10,"Identity cross paths vanish");
        check(Math.Abs(identity.Drive[0][identity.LatencySamples]-1)<1e-8,"Identity direct path is unity");
        check(identity.Checks?.PositionSpreadDb is {} spread&&double.IsFinite(spread),"Fixed-pose standard deviation is finite");
        check(SpeakerRiskCheck.Spread(new double[][][]{[[0,0],[0,0]],[[0,0],[0,0]]})==0,"Identical pose levels have zero standard deviation");
        check(Math.Abs(SpeakerRiskCheck.Spread(new double[][][]{[[-1],[-1]],[[1],[1]]})-1)<1e-12,"Fixed-pose statistic matches independently known standard deviation");
        var exaggerated=identity with{RelativeErrorDb=-3,MaximumDriveGainDb=25,InverseProjectionDb=-20};
        var flagged=SpeakerRiskCheck.Final(exaggerated);check(new[]{"residual","projection"}.All(code=>flagged.Issues.Any(x=>x.Code==code)),"Final measured failure thresholds raise distinct warnings");
        var loud=identity with{Drive=identity.Drive.Select(h=>h.Select(v=>v*16).ToArray()).ToArray()};
        check(SpeakerRiskCheck.Final(loud).Issues.Any(x=>x.Code=="drive"),"Actual matrix-gain warning includes differential input");
        var cancelling=identity with{Playback=[[1],[.95],[.95],[1]],Drive=[[10.2564102564],[-9.7435897436],[-9.7435897436],[10.2564102564]]};
        check(SpeakerRiskCheck.Final(cancelling).Issues.Any(x=>x.Code=="cancellation"),"Strong destructive interference raises cancellation warning");
        p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name=="宽阔监听"),SpeakerMode.SpatialField);
        var result=SpeakerGenerator.Generate(p);var target=Generator.Generate(p.Field);
        check(result.Target.Zip(target.Kernels).All(x=>x.First.SequenceEqual(x.Second)),"T exactly equals original headphone Wide monitor");
        Console.WriteLine($"Wide residual {result.RelativeErrorDb:F2} dB; delay {result.LatencySamples/48.0:F2} ms; projection {result.InverseProjectionDb:F2} dB");
        var rows=new List<string>{"Hz,LeftTargetDb,LeftCascadeDb,RightTargetDb,RightCascadeDb"};
        var grid=Dsp.Frequencies;var curves=new List<double[]>();
        for(int ear=0;ear<2;ear++)
        {
            var t=Dsp.SmoothedDbAt(Dsp.Sum(result.Target[ear*2],result.Target[ear*2+1]),48000,12);
            var a=Dsp.SmoothedDbAt(Dsp.Sum(result.Predicted[ear*2],result.Predicted[ear*2+1]),48000,12);
            double rms=Math.Sqrt(t.Zip(a).Average(x=>Math.Pow(x.First-x.Second,2)));
            Console.WriteLine($"Ear {ear} smooth RMS {rms:F3} dB, maximum {t.Zip(a).Max(x=>Math.Abs(x.First-x.Second)):F3} dB");
            check(rms<1,"Cascade smooth tonal error below 1 dB RMS");curves.Add(t);curves.Add(a);
        }
        for(int i=0;i<grid.Length;i++)rows.Add(string.Join(",",new[]{grid[i],curves[0][i],curves[1][i],curves[2][i],curves[3][i]}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
        File.WriteAllLines(Path.Combine(folder,"wide-cascade.csv"),rows);
        check(result.Drive.All(h=>h.All(double.IsFinite)),"Finite float32 exported transform");
        check(result.InverseProjectionDb< -55,"Causal projection preserves solved transform");
        Console.WriteLine($"Position spread {result.Checks?.PositionSpreadDb:F2} dB; risks: {result.Checks?.Text}");
        string exported=SpeakerExporter.Export(result,folder);
        var fRead=Generator.RouteNames.Select(route=>Exporter.ReadWave(Path.Combine(exported,"FreeField_Reference",route+".wav")).Channels[0]).ToArray();
        var cRead=SpeakerExporter.Routes.Select(route=>Exporter.ReadWave(Directory.GetFiles(exported,"*"+route+".wav").Single()).Channels[0]).ToArray();
        double reread=SpeakerGenerator.RelativeError(SpeakerInverse.Multiply(fRead,cRead),result.Predicted,0,48000);
        check(reread< -110,"Reimported F and C WAVs reproduce the plotted cascade");
        var cascade=File.ReadAllLines(Directory.GetFiles(exported,"*Cascade_Test.txt").Single());check(cascade[^2].Contains("Equalizer_APO")&&cascade[^1].Contains("FreeField.txt"),"APO cascade order is C then F");
        check(File.ReadAllText(Directory.GetFiles(exported,"*metadata.json").Single()).Contains("PositionSpreadDb"),"Diagnostics persist in exported metadata");
    }
}
