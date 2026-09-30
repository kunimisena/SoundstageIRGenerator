using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public partial class ReflectionBatchEditor:UserControl
{
    public MainViewModel? ViewModel {get;set;}
    public ReflectionEditSession? Session {get;private set;}
    public bool HasChanges => Session?.HasChanges==true || HasDirtyInput(EditorBody);
    static bool HasDirtyInput(DependencyObject item)
    {
        if(item is TextBox box&&box.GetBindingExpression(TextBox.TextProperty)?.IsDirty==true)return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(HasDirtyInput(VisualTreeHelper.GetChild(item,i)))return true;
        return false;
    }
    bool early,updatingRtScale;
    List<Knot>? rtScaleBase;
    public ReflectionBatchEditor(){InitializeComponent();AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Dispatcher.BeginInvoke(()=>ViewModel?.PendingChanged())));foreach(var c in new[]{SharedEnergy,SharedRt,SharedEnvelope})c.Changed+=()=>{RefreshCurves();Message.Text="曲线已修改。";ViewModel?.PendingChanged();};}
    public void RefreshState()
    {
        bool ready=ViewModel?.Ready==true,current=Session!=null&&ViewModel!=null&&Session.IsCurrent(ViewModel.TemplateProject);
        if(ready&&!current){LoadScope();return;}
        EditorBody.IsEnabled=ready&&Session!=null;
    }
    public void LoadScope()
    {
        try
        {
            var vm=ViewModel??throw new InvalidOperationException("编辑器尚未连接项目。");
            if(!vm.Ready)return;
            var upstream=vm.TemplateProject;var targets=upstream.Sources;
            if(targets.Count==0){Session=null;DataContext=null;EditorBody.IsEnabled=false;foreach(var curve in new[]{SharedEnergy,SharedRt,SharedEnvelope}){curve.Points=null;curve.InvalidateVisual();}ScopeLabel.Text=upstream.TemplateName;Message.Text=upstream.ReflectionEnergyPercent==0?"仅生成经过人头的音箱直达声。":"在“方向分布”中创建一组反射源，或进入详细配置逐个添加。";return;}
            Session=new(upstream,targets.Select(s=>s.Id));rtScaleBase=null;DataContext=Session;
            ScopeLabel.Text=$"{Session.Count} 个可编辑源 · 右半边自动镜像";
            Message.Text="确认后将按这些整体参数创建反射源。";RefreshCurves();RefreshState();
        }
        catch(Exception ex){Session=null;DataContext=null;Message.Text=ex.Message;EditorBody.IsEnabled=false;}
    }
    public bool ApplyChanges()
    {
        try
        {
            CommitInputs(this);PointTable.CommitEdit(DataGridEditingUnit.Cell,true);PointTable.CommitEdit(DataGridEditingUnit.Row,true);
            if(HasErrors(this))throw new ArgumentException("请检查红框内的数值。");
            if(Session==null)return true;
            if(ViewModel==null)throw new InvalidOperationException("编辑器尚未连接项目。");
            if(!Session.HasChanges)return true;
            ViewModel.ApplyReflectionEdit(Session);
            var feedback=ViewModel.Status;LoadScope();Message.Text=feedback;return true;
        }
        catch(Exception ex){Message.Text=ex.Message;return false;}
    }
    static bool HasErrors(DependencyObject item)
    {if(Validation.GetHasError(item))return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)if(HasErrors(VisualTreeHelper.GetChild(item,i)))return true;return false;}
    static void CommitInputs(DependencyObject item)
    {
        if(item is TextBox box)EditorInput.Update(box);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)CommitInputs(VisualTreeHelper.GetChild(item,i));
    }
    public void RefreshCurves()
    {
        if(Session==null)return;var e=Session.Reference;
        try{if(EditingLimits.Normalize(e)>0)Message.Text="越界数值已钳位；其余修改保留。";e.Validate();}
        catch(ArgumentException ex)
        {
            Message.Text=ex.Message;
            foreach(var curve in new[]{SharedEnergy,SharedRt,SharedEnvelope}){curve.Points=null;curve.InvalidateVisual();}
            return;
        }
        if(!updatingRtScale)
        {
            double factor=Math.Pow(2,RtScaleSlider.Value);
            if(rtScaleBase==null||rtScaleBase.Count!=e.Decay.Count||rtScaleBase.Zip(e.Decay).Any(v=>v.First.X!=v.Second.X||Math.Abs(v.First.Y*factor-v.Second.Y)>1e-12))
            {
                updatingRtScale=true;rtScaleBase=e.Decay.Select(k=>new Knot(k.X,k.Y)).ToList();
                RtScaleSlider.Minimum=-20;RtScaleSlider.Maximum=20;RtScaleSlider.Value=0;
                RtScaleSlider.Minimum=-8;
                RtScaleSlider.Maximum=8;
                updatingRtScale=false;
            }
        }
        RtScaleLabel.Text=$"整体 ×{Math.Pow(2,RtScaleSlider.Value):0.00} · 1 kHz {e.Rt(1000):0.###} s";
        var frame=e.Clone();
        SharedEnergy.Points=e.Energy;SharedEnergy.MinY=Math.Min(-30,e.Energy.Min(k=>k.Y)-3);SharedEnergy.MaxY=Math.Max(12,e.Energy.Max(k=>k.Y)+3);
        SharedRt.Points=e.Decay;SharedRt.MinY=EditingLimits.MinRt;SharedRt.MaxY=EditingLimits.MaxRt;SharedRt.LogY=true;SharedRt.Unit="s";
        SharedEnvelope.Points=e.Envelope;SharedEnvelope.LogX=false;SharedEnvelope.FullEnvelope=true;SharedEnvelope.MinY=Math.Min(-80,e.Envelope!.Min(k=>k.Y)-3);SharedEnvelope.MaxY=Math.Max(12,e.Envelope!.Max(k=>k.Y)+3);
        SharedEnvelope.XUnit="参考额外路程 m";SharedEnvelope.DisplayX=frame.EnvelopeDistance;SharedEnvelope.ModelX=frame.EnvelopeCoordinate;
        SharedEnvelope.MinX=e.FirstReflectionExtraPath;SharedEnvelope.MaxX=early?SharedEnvelope.MinX+Math.Max(1,e.MixingPath*1.4):e.EnvelopeDistance(1);
        SharedEnvelope.Marker=e.FirstReflectionExtraPath+e.MixingPath;
        foreach(var curve in new[]{SharedEnergy,SharedRt,SharedEnvelope})curve.InvalidateVisual();
        PointTable.ItemsSource=CurveChoice.SelectedIndex==1?e.Decay:CurveChoice.SelectedIndex==2?(IEnumerable)e.Envelope!.Select(k=>new EnvelopeRow(e,k)).ToList():e.Energy;
    }
    void RtScaleChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(updatingRtScale||Session==null||rtScaleBase==null)return;
        updatingRtScale=true;
        try
        {
            double factor=Math.Pow(2,RtScaleSlider.Value);
            Session.Reference.Decay=rtScaleBase.Select(k=>new Knot(k.X,Math.Clamp(k.Y*factor,EditingLimits.MinRt,EditingLimits.MaxRt))).ToList();
            RefreshCurves();Message.Text="衰减时间已整体缩放。";ViewModel?.PendingChanged();
        }
        finally{updatingRtScale=false;}
    }
    void ResetRtScale(object sender,RoutedEventArgs e)=>RtScaleSlider.Value=0;
    void ScalarChanged(object sender,RoutedEventArgs e){if(sender is TextBox b)EditorInput.Update(b);if(!HasErrors(this))RefreshCurves();if(sender is TextBox input)input.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();}

    void ZoomEarly(object sender,RoutedEventArgs e){early=true;RefreshCurves();}
    void ZoomFull(object sender,RoutedEventArgs e){early=false;RefreshCurves();}
    void TableChanged(object sender,SelectionChangedEventArgs e){if(Session!=null)RefreshCurves();}
    void TableEdited(object sender,DataGridCellEditEndingEventArgs e)=>Dispatcher.BeginInvoke(()=>{RefreshCurves();},DispatcherPriority.Background);
}
