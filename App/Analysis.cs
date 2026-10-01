using System.Numerics;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public static class Analysis
{
    static readonly string[] Colors=["#52DBBF","#EAB66E","#7BAAFF","#D9A4ED","#FFFFFF","#EF8596"];
    public static PlotData Build(GenerationResult r,Guid? source,int subject,int kind,bool smooth,bool bandpass=false)
    {
        double[][] paths;string[] names;double zero=r.ZeroSample;int sr=r.Project.SampleRate;
        switch(subject)
        {
            case 1:paths=[Dsp.Sum(r.Kernels[0],r.Kernels[1]),Dsp.Sum(r.Kernels[2],r.Kernels[3])];names=["左耳 · L+R","右耳 · L+R"];break;
            case 2:paths=[Dsp.Sum(r.Raw[0],r.Raw[1]),Dsp.Sum(r.Raw[2],r.Raw[3])];names=["EQ 前左耳","EQ 前右耳"];break;
            case 3:paths=r.DirectPaths;names=["L→左耳","R→左耳","L→右耳","R→右耳"];break;
            case 4:
            case 5:
                var c=r.Contributions.FirstOrDefault(c=>c.Id==source);if(c==null)return new("卷积结果中没有所选反射源，请生成卷积核","Hz","dB",true,[]);
                paths=subject==4?[c.LeftInputKernel,c.RightInputKernel]:c.EarPaths;names=subject==4?["L 激励","R 激励"]:["L→左耳","R→左耳","L→右耳","R→右耳"];if(subject==4)zero=0;break;
            case 6:paths=[r.EarEq[0],r.EarEq[1],r.SecondEq,r.Bandpass];names=["一级左耳修正","一级右耳修正","共同第二级修正","带通目标"];zero=0;break;
            default:paths=r.Kernels;names=["L→左耳","R→左耳","L→右耳","R→右耳"];break;
        }
        var lines=new List<PlotLine>();for(int c=0;c<paths.Length;c++)
        {
            var h=paths[c];double[] x,y;
            if(kind==0)
            {
                double low=bandpass?5:20,high=bandpass?Math.Min(22000,sr*.5*.995):20000;
                x=bandpass?Enumerable.Range(0,1601).Select(i=>low*Math.Pow(high/low,i/1600.0)).ToArray():Dsp.Frequencies;
                y=smooth?Dsp.SmoothedDbAt(h,sr,r.Project.Smooth1,x,low,high):Dsp.PowerAt(h,sr,x).Select(Dsp.Db).ToArray();
            }
            else if(kind==1)
            {
                int step=Math.Max(1,h.Length/1800);var xs=new List<double>();var ys=new List<double>();
                for(int i=0;i<h.Length;i+=step){int count=Math.Min(step,h.Length-i),lo=i,hi=i;for(int j=i+1;j<i+count;j++){if(h[j]<h[lo])lo=j;if(h[j]>h[hi])hi=j;}foreach(int j in lo<=hi?new[]{lo,hi}:new[]{hi,lo}){xs.Add((j-zero)*1000/sr);ys.Add(h[j]);}}x=xs.ToArray();y=ys.ToArray();
            }
            else if(kind==2)
            {
                var edc=new double[h.Length];double sum=0;for(int i=h.Length-1;i>=0;i--){sum+=h[i]*h[i];edc[i]=sum;}
                int step=Math.Max(1,h.Length/2000);var index=Enumerable.Range(0,(h.Length+step-1)/step).Select(i=>i*step).ToArray();x=index.Select(i=>(i-zero)*1000/sr).ToArray();y=index.Select(i=>Dsp.Db(edc[i]/Math.Max(1e-30,sum))).ToArray();
            }
            else
            {
                int n=Dsp.Pow2(Math.Max(h.Length,65536));var spec=Dsp.Spectrum(h,n);var phase=new double[n/2+1];double offset=0,prev=0;
                for(int i=0;i<phase.Length;i++){double v=Math.Atan2(spec[i].Imaginary,spec[i].Real);if(i>0){double delta=v-prev;if(delta>Math.PI)offset-=2*Math.PI;else if(delta< -Math.PI)offset+=2*Math.PI;}phase[i]=v+offset+2*Math.PI*i*zero/n;prev=v;}
                x=Dsp.Frequencies;y=x.Select(f=>{int i=Math.Clamp((int)Math.Round(f*n/sr),1,n/2-1);return kind==3?phase[i]*180/Math.PI:-(phase[i+1]-phase[i-1])/(4*Math.PI*sr/n)*1000;}).ToArray();
            }
            lines.Add(new(names[c],x,y,Colors[c]));
        }
        string[] titles=[smooth?"功率平滑幅频（仅显示平滑）":"实际幅频 · 不平滑","脉冲响应 · 共同直达零点","实际核的反向积分能量衰减","展开相位 · 已扣除共同前导","群延迟 · 已扣除共同前导"];
        return new(titles[kind],kind is 1 or 2?"ms":"Hz",kind==1?"幅度":kind==3?"度":kind==4?"ms":"dB",kind is 0 or 3 or 4,lines,kind==0&&bandpass?-100:kind==0&&subject!=6?-60:kind==2?-80:null,kind==0&&subject!=6?20:kind==2?0:null);
    }
}
