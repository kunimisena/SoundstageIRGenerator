using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public partial class SourceEditorWindow:Window
{
    public MainViewModel VM {get;}
    bool loaded,syncing,envelopeZoom;
    public SourceEditorWindow(MainViewModel vm, bool showAnalysis=true)
    {
        VM=vm;InitializeComponent();DataContext=VM;ResizePreviewHost.Install(this);loaded=true; if(!showAnalysis){AnalysisPanel.Visibility=Visibility.Collapsed;((Grid)AnalysisPanel.Parent).RowDefinitions[1].Height=new GridLength(0);}
        Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);
        var view=CollectionViewSource.GetDefaultView(VM.Sources);
        if(view.GroupDescriptions.Count==0)view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReflectionPair.Group)));
        VM.VisualChanged+=Refresh;
        Directions.SelectSource+=id=>{VM.Selected=VM.Sources.FirstOrDefault(s=>s.Id==id);SourceList.SelectedItem=VM.Selected;};
        foreach(var curve in new[]{EnergyCurve,DecayCurve,ShapeCurve})curve.Changed+=()=>{VM.Commit();KnotGrid.Items.Refresh();};
        SourceLayout.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Dispatcher.BeginInvoke(()=>VM.PendingChanged())));
        SourceList.SelectedItem=VM.Selected;Refresh();
        Closing+=(_,e)=>{if(VM.Busy||!CommitEdits())e.Cancel=true;};
        Closed+=(_,_)=>VM.VisualChanged-=Refresh;
    }
    public bool HasPendingEdits=>EditorInput.HasDirty(SourceLayout)||AnalysisPanel.HasPendingEdits;
    public bool CommitEdits()
    {
        EditorInput.Commit(SourceLayout);KnotGrid.CommitEdit(DataGridEditingUnit.Cell,true);KnotGrid.CommitEdit(DataGridEditingUnit.Row,true);
        if(EditorInput.HasErrors(SourceLayout)){VM.SetStatus(SoundstageIR.Core.TextCatalog.T("TB469D01528"));return false;}
        if(!AnalysisPanel.CommitEdits())return false;
        VM.Commit();VM.PendingChanged();return true;
    }
    void OpenSourceActions(object sender,RoutedEventArgs e)
    {
        if(sender is Button {ContextMenu:{} menu} button){menu.DataContext=VM;menu.PlacementTarget=button;menu.IsOpen=true;}
    }
    void Finish(object sender,RoutedEventArgs e){if(!VM.Busy&&CommitEdits())Close();}
    void Refresh()
    {
        if(!loaded)return;
        Directions.Project=VM.P;Directions.Selected=VM.Selected?.Id;Directions.InvalidateVisual();
        var e=VM.Editing;
        EnergyCurve.Points=e?.Energy;EnergyCurve.MinY=Math.Min(-30,Math.Floor((e?.Energy.Where(k=>double.IsFinite(k.Y)).Select(k=>k.Y).DefaultIfEmpty(-30).Min()??-30)/10)*10);EnergyCurve.MaxY=Math.Max(10,Math.Ceiling((e?.Energy.Where(k=>double.IsFinite(k.Y)).Select(k=>k.Y).DefaultIfEmpty(10).Max()??10)/10)*10);
        DecayCurve.Points=e?.Decay;DecayCurve.MinY=EditingLimits.MinRt;DecayCurve.MaxY=EditingLimits.MaxRt;DecayCurve.LogY=true;DecayCurve.Unit="s";
        ShapeCurve.Points=e?.Envelope;ShapeCurve.LogX=false;ShapeCurve.MinY=Math.Min(-80,ShapeCurve.Points?.Min(k=>k.Y)-3??-80);ShapeCurve.MaxY=Math.Max(12,ShapeCurve.Points?.Max(k=>k.Y)+3??12);ShapeCurve.XUnit=SoundstageIR.Core.TextCatalog.T("T4CBF266ED0");ShapeCurve.FullEnvelope=true;
        if(e!=null)
        {
            ShapeCurve.DisplayX=e.EnvelopeDistance;
            ShapeCurve.ModelX=e.EnvelopeCoordinate;
            ShapeCurve.MinX=e.FirstReflectionExtraPath;
            ShapeCurve.MaxX=envelopeZoom?ShapeCurve.MinX+Math.Max(1,e.MixingPath*1.4):e.EnvelopeDistance(1);
            ShapeCurve.Marker=e.FirstReflectionExtraPath+e.MixingPath;
        }
        EnvelopeHint.Text=SoundstageIR.Core.TextCatalog.T("T5CC7096F35");
        foreach(var curve in new[]{EnergyCurve,DecayCurve,ShapeCurve}){curve.Editable=VM.CanEditExcitation&&VM.Ready;curve.InvalidateVisual();}
        UpdateTable();
        if(!syncing&&SourceList.SelectedItem!=VM.Selected){syncing=true;SourceList.SelectedItem=VM.Selected;syncing=false;}

    }
    void Edited(object sender,RoutedEventArgs e)
    {
        if(!loaded)return;
        if(sender is TextBox box)EditorInput.Update(box);
        VM.Commit();
        // Refresh display labels and grouping without changing the selection set.
        var ids=SourceList.SelectedItems.Cast<ReflectionPair>().Select(s=>s.Id).ToHashSet();syncing=true;
        CollectionViewSource.GetDefaultView(VM.Sources).Refresh();foreach(var s in VM.Sources.Where(s=>ids.Contains(s.Id)))SourceList.SelectedItems.Add(s);syncing=false;
    }
    void SourceSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!loaded||syncing)return;syncing=true;VM.Selection=SourceList.SelectedItems.Cast<ReflectionPair>().ToList();
        VM.Selected=(e.AddedItems.Count>0?e.AddedItems[0]:SourceList.SelectedItem) as ReflectionPair;syncing=false;
    }
    void SelectGroup(object sender,RoutedEventArgs e)
    {
        if(sender is not Button {DataContext:CollectionViewGroup group})return;
        syncing=true;SourceList.SelectedItems.Clear();foreach(var item in group.Items)SourceList.SelectedItems.Add(item);VM.Selection=SourceList.SelectedItems.Cast<ReflectionPair>().ToList();VM.Selected=VM.Selection.FirstOrDefault();syncing=false;
    }

    void ResetView(object sender,RoutedEventArgs e)=>Directions.ResetView();
    void EnvelopeFull(object sender,RoutedEventArgs e){envelopeZoom=false;Refresh();}
    void EnvelopeEarly(object sender,RoutedEventArgs e){envelopeZoom=true;Refresh();}
    void CurveTableChanged(object sender,SelectionChangedEventArgs e){if(loaded)UpdateTable();}
    void UpdateTable(){var e=VM.Editing;KnotGrid.ItemsSource=e==null?null:CurveTableKind.SelectedIndex==1?e.Decay:CurveTableKind.SelectedIndex==2?(IEnumerable)e.Envelope.Select(k=>new EnvelopeRow(e,k)).ToList():e.Energy;}
    void KnotEdited(object sender,DataGridCellEditEndingEventArgs e)=>Dispatcher.BeginInvoke(()=>VM.Commit(),DispatcherPriority.Background);
}
