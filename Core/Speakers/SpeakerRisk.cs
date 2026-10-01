using System.Numerics;
namespace SoundstageIR.Core.Speakers;
public sealed record SpeakerRisk(string Code,string Zh,string En)
{ public string Text=>TextCatalog.English?En:Zh; }
public sealed record SpeakerRiskReport(List<SpeakerRisk> Issues,double RequiredGainDb,double CriticalHz,
    double? ToneRmsDb=null,double? DriveGainDb=null,double? CancellationDb=null,double? PositionSpreadDb=null)
{
    public bool HasRisk=>Issues.Count>0;
    public string Text=>string.Join("\n",Issues.Select(x=>"● "+x.Text));
}
public static class SpeakerRiskCheck
{
    public static readonly double[] Frequencies=Enumerable.Range(0,161).Select(i=>20*Math.Pow(1000,i/160.0)).ToArray();
    // A directional unit-source probe, before random reverberation. It estimates geometry
    // demand, not the full target's final drive or perceptual success.
    public static SpeakerRiskReport Precheck(SpeakerProject p,CancellationToken ct=default)
    {
        p.Validate();var issues=new List<SpeakerRisk>();if(p.Mode!=SpeakerMode.SpatialField)return new(issues,0,0);
        var g=Geometry(p,0,0,0,ct,true);var head=new HeadRenderer(p.Field);
        var directions=HeadReferenceEq.Directions(p.Field);double worst=0,hz=20;
        Complex[] Probe(double az,double el,int ear)=>p.Field.HeadModel==HeadModelKind.Fabian
            ?Transfer(FabianData.MeasuredAt(az,el,ear,FabianData.NativeRate,p.Field.FabianCtfCompensation),FabianData.NativeRate)
            :Transfer(head.At(az,el,ear),p.Field.SampleRate);
        var probes=directions.Select(v=>new[]{Probe(v.Az,v.El,0),Probe(v.Az,v.El,1)}).ToArray();
        for(int k=0;k<Frequencies.Length;k++)
        {
            ct.ThrowIfCancellationRequested();var a=g[0][k];var b=g[1][k];var c=g[2][k];var d=g[3][k];var det=a*d-b*c;
            double norm=(Dsp.Power(a)+Dsp.Power(b)+Dsp.Power(c)+Dsp.Power(d))*.5;
            foreach(var probe in probes)
            {
                var l=probe[0][k];var r=probe[1][k];double reference=Dsp.Power(l)+Dsp.Power(r);
                double numerator=Dsp.Power(d*l-b*r)+Dsp.Power(a*r-c*l);
                double ratio=numerator*norm/Math.Max(1e-300,Dsp.Power(det)*reference);
                double db=Math.Clamp(Dsp.Db(ratio),-200,200);if(db>worst){worst=db;hz=Frequencies[k];}
            }
        }
        bool same=Enumerable.Range(0,g[0].Length).All(k=>(g[0][k]-g[1][k]).Magnitude<1e-10&&(g[2][k]-g[3][k]).Magnitude<1e-10);
        if(same)issues.Add(new("rank","两只实际音箱的模型路径相同，无法独立控制双耳差异。","Both actual-speaker model paths coincide; independent binaural control is unavailable."));
        if(worst>p.MaximumInverseGainDb)issues.Add(new("demand",$"方向探针在 {hz:0} Hz 需要约 {worst:0.0} dB 相对驱动增益，超过当前求逆预算；该频段会保留还原误差。",$"A directional probe requires about {worst:0.0} dB relative drive at {hz:0} Hz, beyond the inverse budget; reconstruction errors will remain in this region."));
        if(Math.Min(p.LeftSpeaker.Distance,p.RightSpeaker.Distance)<.44)issues.Add(new("near", "音箱距离小于 0.44 m：近场差异明显，当前远场人头模型未包含近场修正。","Speaker distance is below 0.44 m: near-field differences are significant; the far-field head model does not include near-field corrections."));
        return new(issues,worst,hz);
    }
    public static SpeakerRiskReport Final(SpeakerResult result,CancellationToken ct=default)
    {
        var p=result.Project;var pre=Precheck(p,ct);var issues=new List<SpeakerRisk>();
        // Keep structural/model warnings; replace the directional estimate by actual-drive checks.
        issues.AddRange(pre.Issues.Where(x=>x.Code!="demand"));
        if(result.RelativeErrorDb> -15)issues.Add(new("residual",$"串联相对还原误差为 {result.RelativeErrorDb:0.0} dB，目标双耳响应未被充分还原。",$"Cascade relative reconstruction error is {result.RelativeErrorDb:0.0} dB; the target binaural response is not closely reproduced."));
        if(result.InverseProjectionDb> -50)issues.Add(new("projection",$"因果化残差为 {result.InverseProjectionDb:0.0} dB，有限卷积核与频域解存在明显差异。",$"Causal projection residual is {result.InverseProjectionDb:0.0} dB; the finite kernel differs appreciably from the frequency-domain solution."));
        double sum=0;int count=0;
        for(int ear=0;ear<2;ear++)
        {
            ct.ThrowIfCancellationRequested();var target=Dsp.SmoothedDbAt(Dsp.Sum(result.Target[ear*2],result.Target[ear*2+1]),result.SampleRate,p.Field.Smooth1);
            var actual=Dsp.SmoothedDbAt(Dsp.Sum(result.Predicted[ear*2],result.Predicted[ear*2+1]),result.SampleRate,p.Field.Smooth1);
            foreach(var pair in target.Zip(actual)){sum+=Math.Pow(pair.First-pair.Second,2);count++;}
        }
        double tone=Math.Sqrt(sum/Math.Max(count,1));
        if(tone>1)issues.Add(new("tone",$"串联平滑频响相对目标的 RMS 偏差 {tone:0.00} dB。",$"Cascade smoothed-response RMS deviation from target is {tone:0.00} dB."));
        var nominal=Geometry(p,0,0,0,ct);var f=Evaluate(result.Playback,result.SampleRate,ct);var drive=Evaluate(result.Drive,result.SampleRate,ct);
        double maxDrive=0;var uncancelled=new double[Frequencies.Length];var combined=new double[Frequencies.Length];
        for(int k=0;k<Frequencies.Length;k++)
        {
            double u=Dsp.Power(drive[0][k])+Dsp.Power(drive[2][k]),v=Dsp.Power(drive[1][k])+Dsp.Power(drive[3][k]);
            var z=Complex.Conjugate(drive[0][k])*drive[1][k]+Complex.Conjugate(drive[2][k])*drive[3][k];
            maxDrive=Math.Max(maxDrive,Dsp.Db((u+v+Math.Sqrt((u-v)*(u-v)+4*Dsp.Power(z)))*.5));
            for(int ear=0;ear<2;ear++)for(int input=0;input<2;input++)
            {
                var l=f[ear*2][k]*drive[input][k];var r=f[ear*2+1][k]*drive[2+input][k];
                uncancelled[k]+=Dsp.Power(l)+Dsp.Power(r);combined[k]+=Dsp.Power(l+r);
            }
        }
        double cancel=0,cancelHz=20;var un=Dsp.SmoothPower(Frequencies,uncancelled,3);var co=Dsp.SmoothPower(Frequencies,combined,3);
        for(int k=0;k<un.Length;k++){double db=Dsp.Db(un[k]/Math.Max(1e-300,co[k]));if(db>cancel){cancel=db;cancelHz=Frequencies[k];}}
        if(maxDrive>18)issues.Add(new("drive",$"实际输出矩阵最大增益约 {maxDrive:0.0} dB；包括同相与差分输入，驱动需求较高。",$"Actual output matrix gain reaches about {maxDrive:0.0} dB, including common and differential inputs; drive demand is high."));
        if(cancel>12)issues.Add(new("cancellation",$"约 {cancelHz:0} Hz 的相消代价达到 {cancel:0.0} dB：两音箱单独贡献的功率和远大于合成结果。",$"Cancellation cost reaches {cancel:0.0} dB around {cancelHz:0} Hz: summed individual speaker contributions greatly exceed their combined result."));
        const double delta=.05;
        var poseLevels=new List<double[][]>();
        double[][] Level(Complex[][] response)=>Enumerable.Range(0,2).Select(ear=>Dsp.SmoothPower(Frequencies,
            Enumerable.Range(0,Frequencies.Length).Select(k=>Dsp.Power(response[ear*2][k])+Dsp.Power(response[ear*2+1][k])).ToArray(),3).Select(Dsp.Db).ToArray()).ToArray();
        var centreResponse=Enumerable.Range(0,4).Select(c=>Enumerable.Range(0,Frequencies.Length).Select(k=>f[(c/2)*2][k]*drive[c%2][k]+f[(c/2)*2+1][k]*drive[2+c%2][k]).ToArray()).ToArray();
        poseLevels.Add(Level(centreResponse));
        foreach(var (x,y,yaw) in new[]{(delta,0d,0d),(-delta,0d,0d),(0d,delta,0d),(0d,-delta,0d),(0d,0d,5d),(0d,0d,-5d)})
        {
            if(x==0&&y==0&&yaw==0)continue;ct.ThrowIfCancellationRequested();var moved=Geometry(p,x,y,yaw,ct);
            var poseResponse=Enumerable.Range(0,4).Select(_=>new Complex[Frequencies.Length]).ToArray();
            for(int k=0;k<Frequencies.Length;k++)
            {
                // Infer and FREEZE the centre's per-ear calibration. Never re-EQ or
                // re-invert after moving the head: all poses use the same C and EQ.
                for(int ear=0;ear<2;ear++)
                {
                    int a=ear*2;var q=(f[a][k]*Complex.Conjugate(nominal[a][k])+f[a+1][k]*Complex.Conjugate(nominal[a+1][k]))/Math.Max(1e-300,Dsp.Power(nominal[a][k])+Dsp.Power(nominal[a+1][k]));
                    for(int input=0;input<2;input++)
                    {
                        poseResponse[a+input][k]=q*(moved[a][k]*drive[input][k]+moved[a+1][k]*drive[2+input][k]);
                    }
                }
            }
            poseLevels.Add(Level(poseResponse));
        }
        double spread=Spread(poseLevels);
        if(spread>3)issues.Add(new("movement",$"固定位置采样的频响标准差为 {spread:0.00} dB，位置变化较明显（中心及 ±5 cm／±5°）。",$"Frequency-response standard deviation across fixed poses is {spread:0.00} dB; position changes are substantial (centre and ±5 cm / ±5°)."));
        return new(issues,pre.RequiredGainDb,pre.CriticalHz,tone,maxDrive,cancel,spread);
    }
    // Population standard deviation across seven fixed poses, then RMS across ears
    // and logarithmically spaced frequencies. Levels use incoherent L/R excitation.
    public static double Spread(IReadOnlyList<double[][]> poses)
    {
        if(poses.Count==0)return 0;double sum=0;int count=0;
        for(int ear=0;ear<poses[0].Length;ear++)for(int k=0;k<poses[0][ear].Length;k++)
        {double mean=poses.Average(p=>p[ear][k]);sum+=poses.Average(p=>Math.Pow(p[ear][k]-mean,2));count++;}
        return Math.Sqrt(sum/Math.Max(1,count));
    }
    public static Complex[][] Evaluate(double[][] paths,int sr,CancellationToken ct=default)
    {
        int n=Dsp.Pow2(Math.Max(65536,paths.Max(h=>h.Length)));var result=new Complex[paths.Length][];
        for(int c=0;c<paths.Length;c++){ct.ThrowIfCancellationRequested();var fft=Dsp.Spectrum(paths[c],n);result[c]=Frequencies.Select(f=>Sample(fft,f*n/sr)).ToArray();}return result;
    }
    static Complex Sample(Complex[] h,double bin){int lo=Math.Clamp((int)bin,0,h.Length/2-1);double u=bin-lo;return h[lo]*(1-u)+h[lo+1]*u;}
    static Complex[] Transfer(EarTransfer q,int sr)
    {
        int n=Dsp.Pow2(Math.Max(65536,q.Impulse.Length));var h=Dsp.Spectrum(q.Impulse,n);
        return Frequencies.Select(f=>Sample(h,f*n/sr)*Complex.FromPolarCoordinates(1,-2*Math.PI*f*q.Delay)).ToArray();
    }
    // x right, y forward; yaw rotates head right. Keep the centre's time origin fixed.
    static Complex[][] Geometry(SpeakerProject p,double x,double y,double yaw,CancellationToken ct,bool fast=false)
    {
        var head=new HeadRenderer(new Project{SampleRate=p.Field.SampleRate,HeadModel=HeadModelKind.Fabian,FabianCtfCompensation=p.Field.FabianCtfCompensation});
        var result=new Complex[4][];var positions=new[]{p.LeftSpeaker,p.RightSpeaker};double nearest=positions.Min(s=>s.Distance);
        for(int input=0;input<2;input++)
        {
            var pos=positions[input];double az=pos.Azimuth*Math.PI/180,el=pos.Elevation*Math.PI/180;
            double xx=pos.Distance*Math.Cos(el)*Math.Sin(az)-x,yy=pos.Distance*Math.Cos(el)*Math.Cos(az)-y,zz=pos.Distance*Math.Sin(el);
            double distance=Math.Max(.09,Math.Sqrt(xx*xx+yy*yy+zz*zz));double a=Math.Atan2(xx,yy)*180/Math.PI-yaw;a=(a+540)%360-180;double e=Math.Asin(Math.Clamp(zz/distance,-1,1))*180/Math.PI;
            var air=Transfer(new EarTransfer(SpeakerPlayback.Air(p,distance),0),p.Field.SampleRate);
            for(int ear=0;ear<2;ear++)
            {
                ct.ThrowIfCancellationRequested();var h=fast?Transfer(FabianData.MeasuredAt(a,e,ear,FabianData.NativeRate,p.Field.FabianCtfCompensation),FabianData.NativeRate):Transfer(head.At(a,e,ear),p.Field.SampleRate);
                for(int k=0;k<h.Length;k++)h[k]*=air[k]/distance*Complex.FromPolarCoordinates(1,-2*Math.PI*Frequencies[k]*(distance-nearest)/HeadModel.C);
                result[ear*2+input]=h;
            }
        }
        return result;
    }
}
