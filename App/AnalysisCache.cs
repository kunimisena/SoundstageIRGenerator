using SoundstageIR.Core;
namespace SoundstageIRGenerator;
// One cache per project result, shared by the main view and the source editor.
// Layout, focus and property notifications reuse completed analysis; FFT work is serialized.
public sealed class AnalysisCache
{
    GenerationResult? result;
    readonly Dictionary<(Guid? Source,int Subject,int Kind,bool Smooth,bool Bandpass),Task<PlotData>> entries=[];
    readonly SemaphoreSlim worker=new(1,1);
    CancellationTokenSource lifetime=new();
    int builds;
    public int BuildCount=>Volatile.Read(ref builds);
    public void Clear()
    {
        lifetime.Cancel();lifetime.Dispose();lifetime=new();result=null;entries.Clear();
    }
    public Task<PlotData> GetAsync(GenerationResult input,Guid? source,int subject,int kind,bool smooth,bool bandpass=false)
    {
        if(!ReferenceEquals(result,input)){Clear();result=input;}
        var key=(subject is 4 or 5?source:null,subject,kind,kind==0&&smooth,kind==0&&bandpass);
        if(entries.TryGetValue(key,out var cached)&&!cached.IsFaulted&&!cached.IsCanceled)return Localized(cached);
        foreach(var old in entries.Where(e=>e.Value.IsCompleted).Select(e=>e.Key).Take(Math.Max(0,entries.Count-15)).ToArray())entries.Remove(old);
        return Localized(entries[key]=Build(input,key,lifetime.Token));
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlotData,Dictionary<string,PlotData>> localizedPlots=new();
    internal static async Task<PlotData> Localized(Task<PlotData> task)
    {
        var data=await task;
        var variants=localizedPlots.GetOrCreateValue(data);string language=TextCatalog.Language;
        if(variants.TryGetValue(language,out var existing))return existing;
        string title=TextCatalog.Diagnostic(data.Title),unit=TextCatalog.Diagnostic(data.YUnit);
        if(title==data.Title&&unit==data.YUnit&&data.Lines.All(l=>TextCatalog.Diagnostic(l.Name)==l.Name))return data;
        return variants[language]=data with {Title=title,YUnit=unit,Lines=data.Lines.Select(l=>l with{Name=TextCatalog.Diagnostic(l.Name)}).ToList()};
    }
    async Task<PlotData> Build(GenerationResult input,(Guid? Source,int Subject,int Kind,bool Smooth,bool Bandpass) key,CancellationToken token)
    {
        await worker.WaitAsync(token);
        try
        {
            return await Task.Run(()=>{token.ThrowIfCancellationRequested();Interlocked.Increment(ref builds);return Analysis.Build(input,key.Source,key.Subject,key.Kind,key.Smooth,key.Bandpass);},token);
        }
        finally{worker.Release();}
    }
}
