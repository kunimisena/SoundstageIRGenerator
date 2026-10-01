using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public sealed partial class MainViewModel
{
    CancellationTokenSource? precheckCancellation;
    readonly SemaphoreSlim precheckWorker=new(1,1);
    string? precheckKey;bool checkingSpeaker;string? precheckError;SpeakerRiskReport? precheck;
    public bool SpeakerPrecheckRisk=>SpatialSpeaker&&(precheckError!=null||precheck?.HasRisk==true);
    public string SpeakerPrecheckText=>!SpatialSpeaker?"":checkingSpeaker?TextCatalog.T("Speaker.Checking"):
        precheckError??(precheck==null?TextCatalog.T("Speaker.CheckPending"):
        (precheck.HasRisk?precheck.Text:TextCatalog.T("Speaker.CheckOkay"))+"\n"+
        (TextCatalog.English?$"Directional probe demand: {precheck.RequiredGainDb:0.0} dB @ {precheck.CriticalHz:0} Hz":$"方向探针需求：{precheck.RequiredGainDb:0.0} dB @ {precheck.CriticalHz:0} Hz"));
    public bool SpeakerResultRisk=>SpatialSpeaker&&(speakerFailure!=null||SpeakerResult?.Checks?.HasRisk==true);
    string? speakerFailure;
    public string SpeakerResultRiskText=>speakerFailure??(SpeakerResult?.Checks is {} r?(Stale?TextCatalog.T("Speaker.CheckOld")+"\n":"")+r.Text:"");
    public string SpeakerCheckSummary=>SpeakerResult?.Checks is {} r
        ?(TextCatalog.English?$"Smoothed response RMS: {r.ToneRmsDb:0.00} dB · Position variation (SD): {r.PositionSpreadDb:0.00} dB\nMatrix gain: {r.DriveGainDb:0.0} dB · Cancellation cost: {r.CancellationDb:0.0} dB":$"平滑频响 RMS：{r.ToneRmsDb:0.00} dB · 位置变化参考（标准差）：{r.PositionSpreadDb:0.00} dB\n矩阵增益：{r.DriveGainDb:0.0} dB · 相消代价：{r.CancellationDb:0.0} dB"):"";
    void CheckNotify(){Notify(nameof(SpeakerPrecheckRisk));Notify(nameof(SpeakerPrecheckText));Notify(nameof(SpeakerResultRisk));Notify(nameof(SpeakerResultRiskText));Notify(nameof(SpeakerCheckSummary));}
    public void StopSpeakerPrecheck(){precheckCancellation?.Cancel();if(checkingSpeaker)precheckKey=null;}
    Task precheckTask=Task.CompletedTask;
    public Task UpdateSpeakerPrecheckAsync(bool immediate=false)
    {
        CheckNotify();if(!SpatialSpeaker){StopSpeakerPrecheck();return Task.CompletedTask;}
        Speaker!.Field=P;var input=ProjectIO.Clone(Speaker);input.Field.Name="";input.Field.TemplateName="";
        string key=ProjectIO.Serialize(input);if(key==precheckKey)return precheckTask;precheckKey=key;
        precheckCancellation?.Cancel();precheckCancellation?.Dispose();var source=precheckCancellation=new();var token=source.Token;
        return precheckTask=RunPrecheck(input,source,immediate);
    }
    async Task RunPrecheck(SpeakerProject input,CancellationTokenSource source,bool immediate)
    {
        var token=source.Token;
        checkingSpeaker=true;precheckError=null;precheck=null;CheckNotify();
        try
        {
            if(!immediate)await Task.Delay(400,token);
            await precheckWorker.WaitAsync(token);
            try{var report=await Task.Run(()=>SpeakerRiskCheck.Precheck(input,token),token);if(token.IsCancellationRequested)return;precheck=report;}
            finally{precheckWorker.Release();}
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!token.IsCancellationRequested)precheckError=ex.Message;}
        finally{if(ReferenceEquals(source,precheckCancellation)){checkingSpeaker=false;CheckNotify();}}
    }
}
