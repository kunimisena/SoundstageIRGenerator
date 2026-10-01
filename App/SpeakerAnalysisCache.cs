using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public sealed class SpeakerAnalysisCache
{
    SpeakerResult? current;
    readonly Dictionary<(Guid?,int,int,bool,bool),Task<PlotData>> cache=[];
    readonly SemaphoreSlim worker=new(1,1);
    CancellationTokenSource pending=new();int builds;
    public int BuildCount=>builds;
    public void Clear(){pending.Cancel();current=null;cache.Clear();}
    (Guid?,int,int,bool,bool)? pendingKey;
    public Task<PlotData> GetAsync(SpeakerResult r,Guid? source,int subject,int kind,bool smooth,bool bandpass)
    {
        if(!ReferenceEquals(current,r)){Clear();current=r;pendingKey=null;}
        var key=(subject is 4 or 5?source:null,subject,kind,kind==0&&smooth,kind==0&&bandpass);
        if(pendingKey!=key)
        {
            pending.Cancel();
            // Cancellation completes asynchronously. Remove abandoned entries immediately,
            // otherwise returning quickly to a key can reuse its not-yet-cancelled task.
            foreach(var old in cache.Where(x=>!x.Value.IsCompletedSuccessfully).Select(x=>x.Key).ToArray())cache.Remove(old);
            pending.Dispose();pending=new();pendingKey=key;
        }
        if(!cache.TryGetValue(key,out var task)||task.IsFaulted||task.IsCanceled)
        {
            foreach(var old in cache.Where(x=>x.Value.IsCompleted).Select(x=>x.Key).Take(Math.Max(0,cache.Count-15)).ToArray())cache.Remove(old);
            cache[key]=task=BuildAsync(r,source,subject,kind,smooth,bandpass,pending.Token);
        }
        return AnalysisCache.Localized(task);
    }
    async Task<PlotData> BuildAsync(SpeakerResult r,Guid? source,int subject,int kind,bool smooth,bool bandpass,CancellationToken token)
    {
        await Task.Delay(60,token);await worker.WaitAsync(token);try{return await Task.Run(()=>{token.ThrowIfCancellationRequested();Interlocked.Increment(ref builds);return Build(r,source,subject,kind,smooth,bandpass);},token);}finally{worker.Release();}
    }
    public static PlotData Build(SpeakerResult r,Guid? source,int subject,int kind,bool smooth,bool bandpass)
    {
        if(subject==13)return InversePlot(r,kind,smooth,bandpass);
        var original=r.TargetAnalysis??r.ForAudio();double[][] paths;string[] names;double zero=r.ZeroSample;
        string T(string key)=>TextCatalog.T(key);
        string[] driveNames=[T("Speaker.PathLL"),T("Speaker.PathRL"),T("Speaker.PathLR"),T("Speaker.PathRR")];
        switch(subject)
        {
            case 14: paths=r.BandResult?.PredictedDry??original.FinalDirectPaths;names=[T("T9D3F49D4CD"),T("TA2E15EF348"),T("TB1B8C7B14B"),T("T39DA9A8DC4")];break;
            case 15: paths=r.BandResult?.PredictedWet??original.FinalReflectionPaths;names=[T("T9D3F49D4CD"),T("TA2E15EF348"),T("TB1B8C7B14B"),T("T39DA9A8DC4")];break;
            case 16: paths=[r.BandResult?.CommonEq??[1.0]];names=[T("Speaker.BandEq")];zero=0;break;
            case 0: paths=r.Drive;names=driveNames;break;
            case 1: paths=[Dsp.Sum(r.Drive[0],r.Drive[1]),Dsp.Sum(r.Drive[2],r.Drive[3])];names=[T("Speaker.FinalL"),T("Speaker.FinalR")];break;
            case 7: paths=r.Target;names=[T("T9D3F49D4CD"),T("TA2E15EF348"),T("TB1B8C7B14B"),T("T39DA9A8DC4")];zero=r.ZeroSample-r.LatencySamples;break;
            case 8: paths=r.Predicted;names=[T("T9D3F49D4CD"),T("TA2E15EF348"),T("TB1B8C7B14B"),T("T39DA9A8DC4")];break;
            case 9: paths=[Dsp.Sum(r.Predicted[0],r.Predicted[1]),Dsp.Sum(r.Predicted[2],r.Predicted[3])];names=[T("Speaker.EarL"),T("Speaker.EarR")];break;
            case 10: paths=r.Playback;names=[T("T9D3F49D4CD"),T("TA2E15EF348"),T("TB1B8C7B14B"),T("T39DA9A8DC4")];zero=r.PlaybackAnalysis?.ZeroSample??0;break;
            case 11: return Analysis.Build(r.PlaybackAnalysis!,source,6,kind,smooth,bandpass);
            case 12:
                // Match the common causal modelling delay before comparing phase or IRs.
                double[] Shift(double[] h){var y=new double[h.Length+r.LatencySamples];h.CopyTo(y,r.LatencySamples);return y;}
                paths=[Shift(Dsp.Sum(r.Target[0],r.Target[1])),Dsp.Sum(r.Predicted[0],r.Predicted[1]),Shift(Dsp.Sum(r.Target[2],r.Target[3])),Dsp.Sum(r.Predicted[2],r.Predicted[3])];
                names=[T("Speaker.TargetL"),T("Speaker.EarL"),T("Speaker.TargetR"),T("Speaker.EarR")];break;
            default:
                if(r.Project.Mode==SpeakerMode.SimpleReverb)
                {
                    if(subject==2){paths=[Dsp.Sum(original.Raw[0],original.Raw[1]),Dsp.Sum(original.Raw[2],original.Raw[3])];names=[T("Speaker.BeforeL"),T("Speaker.BeforeR")];}
                    else if(subject==3){paths=[original.DirectPaths[0],original.DirectPaths[3]];names=[driveNames[0],driveNames[3]];}
                    else if(subject==6){paths=[original.EarEq[0],original.EarEq[1],original.Bandpass];names=[T("Speaker.EqL"),T("Speaker.EqR"),T("T324E6B243C")];zero=0;}
                    else return Analysis.Build(original,source,subject,kind,smooth,bandpass);
                    break;
                }
                return Analysis.Build(original,source,subject,kind,smooth,bandpass);
        }
        if(r.Project.Mode==SpeakerMode.SimpleReverb&&subject==0){paths=[r.Drive[0],r.Drive[3]];names=[driveNames[0],driveNames[3]];}
        return Analysis.Build(original,source,subject,kind,smooth,bandpass,paths,names,zero);
    }
    static PlotData InversePlot(SpeakerResult r,int kind,bool smooth,bool bandpass)
    {
        var response=r.InverseResponse??throw new InvalidOperationException("Inverse response is unavailable");
        if(kind is 1 or 2)kind=0;
        var values=kind==3?response.PhaseDegrees:kind==4?response.GroupDelayMs:response.MagnitudeDb;
        var indices=Enumerable.Range(0,response.Frequencies.Length).Where(i=>bandpass||response.Frequencies[i]>=20&&response.Frequencies[i]<=20000).ToArray();
        string[] keys=["Speaker.KLL","Speaker.KLR","Speaker.KRL","Speaker.KRR"];
        string[] colors=["#52DBBF","#EAB66E","#7BAAFF","#D9A4ED"];
        var x=indices.Select(i=>response.Frequencies[i]).ToArray();var lines=new List<PlotLine>();
        for(int c=0;c<4;c++)
        {
            var y=indices.Select(i=>values[c][i]).ToArray();
            if(kind==0&&smooth)y=Dsp.SmoothPower(x,y.Select(v=>Math.Pow(10,v/10)).ToArray(),r.Project.Field.Smooth1).Select(Dsp.Db).ToArray();
            lines.Add(new(TextCatalog.T(keys[c]),x,y,colors[c]));
        }
        return new(TextCatalog.T("Speaker.InversePlot"),"Hz",kind==3?TextCatalog.T("TE24A9725DF"):kind==4?"ms":"dB",true,lines,kind==0?-60:null,kind==0?20:null);
    }

}
