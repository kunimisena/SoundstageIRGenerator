using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public partial class SpeakerBandEditor:UserControl
{
    MainViewModel? vm;OutsideReverb? edit;string stamp="";bool syncingSliders,draggingSlider;
    public SpeakerBandEditor(){InitializeComponent();DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();Energy.Changed+=Changed;Decay.Changed+=Changed;}
    void Connect(){if(vm!=null)vm.VisualChanged-=Refresh;vm=DataContext as MainViewModel;if(vm!=null)vm.VisualChanged+=Refresh;Refresh();}
    void Refresh()
    {
        if(vm?.SpatialSpeaker!=true||vm.Speaker is not {} p)return;
        syncingSliders=true;
        try{LowSlider.Value=Math.Log10(Math.Clamp(p.InverseLowHz,20,20000)/20);HighSlider.Value=Math.Log10(Math.Clamp(p.InverseHighHz,20,20000)/20);}
        finally{syncingSliders=false;}
        var e=p.OutsideParameters();string next=ProjectIO.Serialize(e);
        if(next!=stamp||edit==null){edit=ProjectIO.Clone(e);stamp=next;}
        Energy.Points=edit.Low.Energy.Concat(edit.High.Energy).ToList();Decay.Points=edit.Low.Decay.Concat(edit.High.Decay).ToList();
        Energy.MinY=Math.Min(-30,Energy.Points.Min(k=>k.Y)-3);Energy.MaxY=Math.Max(12,Energy.Points.Max(k=>k.Y)+3);
        Decay.LogY=true;Decay.MinY=EditingLimits.MinRt;Decay.MaxY=EditingLimits.MaxRt;Decay.Unit="s";
        foreach(var c in new[]{Energy,Decay}){c.LockedMinX=p.InverseLowHz;c.LockedMaxX=p.InverseHighHz;c.LockedLabel=TextCatalog.T("Speaker.BandLocked");c.InvalidateVisual();}
    }
    void Changed()
    {
        if(vm?.Speaker is not {} p||edit==null)return;
        // Only the two curves are editable; retain the template's timing and envelope.
        var e=ProjectIO.Clone(p.OutsideParameters());
        List<Knot> Side(List<Knot> points,bool high)=>points.Where(k=>high?k.X>=p.InverseHighHz:k.X<=p.InverseLowHz).Select(k=>new Knot(k.X,k.Y)).ToList();
        e.Low.Energy=Side(Energy.Points!,false);e.High.Energy=Side(Energy.Points!,true);
        e.Low.Decay=Side(Decay.Points!,false);e.High.Decay=Side(Decay.Points!,true);
        p.OutsideBand=e;vm.Commit();Refresh();
    }
    void CrossoverStarted(object sender,DragStartedEventArgs args)=>draggingSlider=true;
    void CrossoverCompleted(object sender,DragCompletedEventArgs args){draggingSlider=false;Edited(sender,args);}
    void CrossoverMoved(object sender,RoutedPropertyChangedEventArgs<double> args)
    {
        if(syncingSliders||vm?.SpatialSpeaker!=true||vm.Localizing||vm.Speaker is not {} p)return;
        bool low=ReferenceEquals(sender,LowSlider);
        double min=low?20:Math.Max(20,Math.Floor(p.InverseLowHz)+1);
        double max=low?Math.Min(20000,Math.Ceiling(p.InverseHighHz)-1):20000;
        if(min>max)return;
        double hz=Math.Clamp(Math.Round(20*Math.Pow(10,args.NewValue)),min,max);
        syncingSliders=true;try{((Slider)sender).Value=Math.Log10(hz/20);}finally{syncingSliders=false;}
        // Keep text input pending during a drag: one undo step and one precheck on release.
        var box=low?Low:High;box.SetCurrentValue(TextBox.TextProperty,hz.ToString("G",CultureInfo.CurrentCulture));
        foreach(var c in new[]{Energy,Decay}){c.LockedMinX=low?hz:p.InverseLowHz;c.LockedMaxX=low?p.InverseHighHz:hz;c.InvalidateVisual();}
        if(draggingSlider)vm.PendingChanged();else Edited(sender,args);
    }
    void Edited(object sender,RoutedEventArgs args)
    {if(vm==null||vm.Localizing)return;EditorInput.Commit(this);if(EditorInput.HasErrors(this))return;vm.Commit();vm.PendingChanged();Refresh();}
}
