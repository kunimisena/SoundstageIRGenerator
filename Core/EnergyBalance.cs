namespace StatisticalField.Core;
// Reference: independent equal-power inputs, flat PSD over 20 Hz--20 kHz,
// summed across both ears. Component energies exclude dry/wet cross terms.
public static class EnergyBalance
{
    public static double Energy(double[][] paths,int sr)=>paths.Sum(h=>Energy(h,sr));
    public static double Energy(double[] h,int sr)
    {
        var a=Dsp.Spectrum(h,Dsp.Pow2(Math.Max(65536,h.Length)));double df=(double)sr/a.Length,sum=0;
        int first=Math.Max(1,(int)Math.Floor(20/df-.5)),last=Math.Min(a.Length/2,(int)Math.Ceiling(20000/df+.5));
        for(int k=first;k<=last;k++)
        {double overlap=Math.Max(0,Math.Min((k+.5)*df,20000)-Math.Max((k-.5)*df,20))/df;sum+=Dsp.Power(a[k])*overlap;}
        return 2*sum/a.Length;
    }
    public static double Percent(double dry,double wet)=>100*wet/Math.Max(1e-30,dry+wet);
    public static double[][] Subtract(double[][] total,double[][] dry)
    {
        var r=new double[4][];
        for(int c=0;c<4;c++){r[c]=new double[Math.Max(total[c].Length,dry[c].Length)];for(int i=0;i<r[c].Length;i++)r[c][i]=(i<total[c].Length?total[c][i]:0)-(i<dry[c].Length?dry[c][i]:0);}
        return r;
    }
    public static double Mix(double[][] dry,double[][] wet,double percent,int sr)
    {
        double d=Energy(dry,sr),r=Energy(wet,sr);if(r<1e-30)return 1;
        double factor;
        if(d<1e-30)factor=1/Math.Sqrt(r);
        else if(percent>=100){factor=Math.Sqrt(d/r);foreach(var h in dry)Array.Clear(h);}
        else factor=percent<=0?0:Math.Sqrt(d/r*percent/(100-percent));
        foreach(var h in wet)Generator.Scale(h,factor);return factor;
    }
}
