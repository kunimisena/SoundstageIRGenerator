using System.Windows;
using System.Windows.Controls;
namespace SoundstageIRGenerator;
// Reflows complete editor groups rather than scaling their fonts and hit targets.
public sealed class AdaptiveGrid:Panel
{
    public static readonly DependencyProperty ItemMinWidthProperty=DependencyProperty.Register(nameof(ItemMinWidth),typeof(double),typeof(AdaptiveGrid),new FrameworkPropertyMetadata(300.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ItemMinWidth {get=>(double)GetValue(ItemMinWidthProperty);set=>SetValue(ItemMinWidthProperty,value);}
    int Columns(double width)=>Math.Max(1,Math.Min(InternalChildren.Count,(int)(Math.Max(1,width)/Math.Max(1,ItemMinWidth))));
    protected override Size MeasureOverride(Size available)
    {
        double width=double.IsFinite(available.Width)?available.Width:Math.Max(ItemMinWidth,ActualWidth);int cols=Columns(width);double height=0,row=0;
        for(int i=0;i<InternalChildren.Count;i++){var child=InternalChildren[i];child.Measure(new Size(width/cols,double.PositiveInfinity));row=Math.Max(row,child.DesiredSize.Height);if(i%cols==cols-1||i==InternalChildren.Count-1){height+=row;row=0;}}
        return new Size(width,height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        int cols=Columns(final.Width);double top=0;
        for(int i=0;i<InternalChildren.Count;i+=cols){double height=0;for(int j=i;j<Math.Min(i+cols,InternalChildren.Count);j++)height=Math.Max(height,InternalChildren[j].DesiredSize.Height);for(int j=i;j<Math.Min(i+cols,InternalChildren.Count);j++)InternalChildren[j].Arrange(new Rect((j-i)*final.Width/cols,top,final.Width/cols,height));top+=height;}
        return final;
    }
}
