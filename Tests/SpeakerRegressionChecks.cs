using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerRegressionChecks
{
    public static void Run(Action<bool,string> check,string oracle)
    {
        var context=new AssemblyLoadContext("previous-local",true);var assembly=context.LoadFromAssemblyPath(oracle);
        var p=Presets.BuiltIn.Single(x=>x.Name=="宽阔监听").Create();
        var oldProject=assembly.GetType("SoundstageIR.Core.ProjectIO")!.GetMethod("Deserialize")!.Invoke(null,[ProjectIO.Serialize(p)]);
        var method=assembly.GetType("SoundstageIR.Core.Generator")!.GetMethod("Generate")!;
        var old=method.Invoke(null,method.GetParameters().Length==3?[oldProject,null,CancellationToken.None]:[oldProject,null,CancellationToken.None,null])!;
        var kernels=(double[][])old.GetType().GetProperty("Kernels")!.GetValue(old)!;var current=Generator.Generate(p);
        check(current.Kernels.Zip(kernels).All(x=>x.First.SequenceEqual(x.Second)),"Headphone Wide monitor remains sample-identical to previous published executable");
        var simple=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name=="紧凑监听"),SpeakerMode.SimpleReverb);
        var oldSimple=JsonSerializer.Deserialize(ProjectIO.Serialize(simple),assembly.GetType("SoundstageIR.Core.Speakers.SpeakerProject")!)!;
        var oldResult=assembly.GetType("SoundstageIR.Core.Speakers.SpeakerGenerator")!.GetMethod("Generate")!.Invoke(null,[oldSimple,null,CancellationToken.None])!;
        var drives=(double[][])oldResult.GetType().GetProperty("Drive")!.GetValue(oldResult)!;var now=SpeakerGenerator.Generate(simple);
        check(now.Drive.Zip(drives).All(x=>x.First.SequenceEqual(x.Second)),"Simple reverb remains sample-identical to previous published executable");
        var complex=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name=="宽阔监听"),SpeakerMode.SpatialField);
        complex.InverseLowHz=20;complex.InverseHighHz=20000;
        var oldComplex=JsonSerializer.Deserialize(ProjectIO.Serialize(complex),assembly.GetType("SoundstageIR.Core.Speakers.SpeakerProject")!)!;
        var oldComplexResult=assembly.GetType("SoundstageIR.Core.Speakers.SpeakerGenerator")!.GetMethod("Generate")!.Invoke(null,[oldComplex,null,CancellationToken.None])!;
        var oldDrive=(double[][])oldComplexResult.GetType().GetProperty("Drive")!.GetValue(oldComplexResult)!;
        var complexNow=SpeakerGenerator.Generate(complex);
        check(complexNow.Drive.Zip(oldDrive).All(x=>x.First.SequenceEqual(x.Second)),"Complex output remains sample-identical after inverse diagnostics");
        double gain=Math.Pow(10,12.0/20),expected=20*Math.Log10((1/(1+1/(4*gain*gain)))/(1+1e-8));
        var flat=SpeakerTransform.Design([[1],[0],[0],[1]],[[1],[0],[0],[1]],48000,12).Response!;
        check(flat.MagnitudeDb[0].All(v=>Math.Abs(v-expected)<1e-10),"Displayed inverse coefficient equals the analytic regularized identity inverse");
        check(flat.PhaseDegrees[0].All(v=>v==0)&&flat.GroupDelayMs[0].All(v=>v==0),"Flat inverse has zero phase and group delay");
        context.Unload();
    }
}
