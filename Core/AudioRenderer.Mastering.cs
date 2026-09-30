using System.Text.Json;
using System.Globalization;
namespace SoundstageIR.Core;
public static partial class AudioRenderer
{
    // Scan only: the loudnorm output is discarded; normalization is a separate static-gain pass.
    static async Task<LoudnessMeasurement> MeasureAsync(string ffmpeg,string input,int? rawSampleRate,Action<double>? progress,CancellationToken ct)
    {
        var args=new List<string>{"-hide_banner","-v","info","-nostdin"};
        if(rawSampleRate is int sr)args.AddRange(["-f","f32le","-ar",sr.ToString(Invariant),"-ac","2"]);
        args.AddRange(["-i",input,"-map","0:a:0","-af","loudnorm=I=-18:TP=0:LRA=50:print_format=json","-f","null","-","-progress","pipe:1","-nostats"]);
        string log=await RunAsync(ffmpeg,args,progress,ct,diagnostics:true);
        int begin=log.LastIndexOf('{'),end=log.LastIndexOf('}');
        if(begin<0||end<begin)throw new InvalidDataException("响度扫描未返回测量结果。");
        using var json=JsonDocument.Parse(log[begin..(end+1)]);
        double? Read(string key)
        {
            if(!json.RootElement.TryGetProperty(key,out var value))throw new InvalidDataException("响度扫描缺少 "+key);
            return double.TryParse(value.GetString(),NumberStyles.Float,Invariant,out double number)&&double.IsFinite(number)?number:null;
        }
        return new(Read("input_i"),Read("input_tp"),Read("input_lra"));
    }
    static async Task ApplyMasteringAsync(string ffmpeg,string input,string output,int sr,long frames,double gainDb,Action<double>? progress,CancellationToken ct,double ceilingDb=0)
    {
        // alimiter uses the maximum across both channels to drive one lookahead gain envelope.
        // Latency compensation flushes the lookahead buffer without shifting the song.
        string filter="volume="+Math.Pow(10,gainDb/20).ToString("R",Invariant)+":precision=double,"+
            $"aresample={sr*4}:resampler=soxr:precision=28,"+
            "alimiter=limit="+Math.Pow(10,ceilingDb/20).ToString("R",Invariant)+":attack=5:release=50:level=0:latency=1,"+
            $"aresample={sr}:resampler=soxr:precision=28,apad=whole_len={frames},atrim=end_sample={frames}";
        await RunAsync(ffmpeg,["-v","error","-nostdin","-y","-f","f32le","-ar",sr.ToString(Invariant),"-ac","2","-i",input,"-af",filter,"-c:a","pcm_f32le","-f","f32le",output,"-progress","pipe:1","-nostats"],progress,ct);
    }
    static async Task EncodeAsync(string ffmpeg,string input,string output,AudioFormat format,int sr,double gain,Action<double>? progress,CancellationToken ct)
    {
        var args=new List<string>{"-v","error","-nostdin","-y","-f","f32le","-ar",sr.ToString(Invariant),"-ac","2","-i",input,"-af","volume="+gain.ToString("R",Invariant)+":precision=double"};
        switch(format)
        {
            case AudioFormat.Wav:args.AddRange(["-c:a","pcm_f32le"]);break;
            case AudioFormat.Flac:args.AddRange(["-c:a","flac","-sample_fmt","s32","-bits_per_raw_sample","24"]);break;
            case AudioFormat.Aac:args.AddRange(["-c:a","aac","-b:a","320k","-movflags","+faststart"]);break;
        }
        args.AddRange([output,"-progress","pipe:1","-nostats"]);await RunAsync(ffmpeg,args,progress,ct);
    }
}
