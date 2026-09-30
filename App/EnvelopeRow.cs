using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public sealed class EnvelopeRow(Excitation excitation,Knot knot)
{
    public double X
    {
        get=>excitation.EnvelopeDistance(knot.X);
        set
        {
            if(knot.X is -1 or 0 or 1)return; // Timing parameters control anchors.
            knot.X=excitation.EnvelopeCoordinate(value);
        }
    }
    public double Y {get=>knot.Y;set{knot.Y=knot.X==1?-60:EditingLimits.Clamp(value,-120,24);}}
}
