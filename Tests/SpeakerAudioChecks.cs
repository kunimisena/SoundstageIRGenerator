using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
static class SpeakerAudioChecks
{
    public static async Task Run(Action<bool,string> check,string folder)
    {
        Directory.CreateDirectory(folder);int sr=48000,n=sr*2;
        double[][] input=[Enumerable.Range(0,n).Select(i=>.1*Math.Sin(i*2*Math.PI*440/sr)).ToArray(),Enumerable.Range(0,n).Select(i=>.05*Math.Sin(i*2*Math.PI*733/sr)).ToArray()];
        string source=Path.Combine(folder,"test-source.wav");Exporter.WriteWave(source,input,sr);
        var p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(v=>v.Name=="自由场"),SpeakerMode.SimpleReverb);
        var result=SpeakerGenerator.Generate(p);
        foreach(var mode in new[]{AudioLevelMode.Bypass,AudioLevelMode.LimitOnly,AudioLevelMode.NormalizeLoudness})
        {
            var audio=await AudioRenderer.RenderAsync(result.ForAudio(),new(source,folder,AudioFormat.Wav,mode));
            check(File.Exists(audio.File),"Speaker song output "+mode);
            check(audio.OutputSamples==n+result.Drive[0].Length-1,"Speaker song retains full tail "+mode);
            check(SpeakerProject.Load(Path.Combine(audio.Folder,"project.json")).Mode==p.Mode,"Song saves speaker project "+mode);
            check(File.ReadAllText(Path.Combine(audio.Folder,"render.json")).Contains("LeftSpeaker"),"Song metadata uses speaker routes "+mode);
            if(mode==AudioLevelMode.Bypass)
            {
                var actual=Exporter.ReadWave(audio.File);var expected=Dsp.Convolve(Exporter.ReadWave(source).Channels[0],result.Drive[0]);
                check(actual.Channels[0].Zip(expected).Max(v=>Math.Abs(v.First-v.Second))<2e-6,"Offline output matches direct numerical convolution");
            }
            else check(audio.After?.TruePeakDbTp<=0,"Speaker song limiter ceiling "+mode);
        }
    }
}
