using System.Windows;
using System.Windows.Controls;
namespace SoundstageIRGenerator;
public partial class ResultPanel:UserControl
{
    MainViewModel? vm;
    int version;
    bool configuringSubjects;bool? subjectMode;
    static readonly int[] SubjectGroups=[1,1,3,3,4,4,3,3,0,0,2,2,0,1,0,0,1];
    static readonly int[] GroupDefaults=[12,13,10,7,4];
    public ResultPanel()
    {
        InitializeComponent();DataContextChanged+=(_,_)=>Connect();Loaded+=(_,_)=>Connect();Unloaded+=(_,_)=>Disconnect();
        GenerationSettings.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Dispatcher.BeginInvoke(()=>vm?.PendingChanged())));
    }
    public bool ShowProjectSettings
    {
        get=>GenerationSettings.Visibility==Visibility.Visible;
        set=>GenerationSettings.Visibility=HistoryButtons.Visibility=value?Visibility.Visible:Visibility.Collapsed;
    }
    public bool HasPendingEdits=>ShowProjectSettings&&EditorInput.HasDirty(GenerationSettings);
    public bool CommitEdits()
    {
        EditorInput.Commit(GenerationSettings);
        if(!EditorInput.HasErrors(GenerationSettings))return true;
        (vm??DataContext as MainViewModel)?.SetStatus(SoundstageIR.Core.TextCatalog.T("TB469D01528"));return false;
    }
    void GenerationEdited(object sender,RoutedEventArgs e)
    {
        if(vm==null||vm.Localizing)return;
        if(CommitEdits())vm.Commit();
        vm.PendingChanged();
    }
    void Connect()
    {
        var next=DataContext as MainViewModel;if(ReferenceEquals(next,vm))return;
        Disconnect();vm=next;if(vm!=null){vm.VisualChanged+=Refresh;ConfigureSubjects();Refresh();}
    }
    void Disconnect(){if(vm!=null)vm.VisualChanged-=Refresh;vm=null;version++;}
    void ConfigureSubjects()
    {
        if(configuringSubjects||vm?.IsSpeaker!=true)return;
        string[] keys=["Speaker.FinalPaths","Speaker.FinalSum","Speaker.Before","TD49407A0B6","T53566E1F1D","T7A65EFD9DF","TAAF8562D88","Speaker.TargetPaths","Speaker.PredictedPaths","Speaker.PredictedSum","Speaker.PlaybackPaths","Speaker.PlaybackEq","Speaker.Comparison","Speaker.InversePlot","Speaker.BandDry","Speaker.BandWet","Speaker.BandEq"];
        string[] spatialKeys=["Speaker.ShortDrive","Speaker.ShortDriveSum","Speaker.ShortRaw","Speaker.ShortDirect","Speaker.ShortSource","Speaker.ShortSourceEars","Speaker.ShortEq","Speaker.ShortTarget","Speaker.ShortCascade","Speaker.ShortCascadeSum","Speaker.ShortPlayback","Speaker.ShortEq","Speaker.ShortComparison","Speaker.InversePlot","Speaker.BandDry","Speaker.BandWet","Speaker.BandEq"];
        bool simple=vm.SimpleSpeaker;int selected=PlotSubject.Items.Count==7?(simple?1:12):PlotSubject.SelectedIndex;
        if(PlotSubject.Items.Count==keys.Length&&subjectMode==simple)return;
        configuringSubjects=true;
        try
        {
            subjectMode=simple;PlotSubject.Items.Clear();
            for(int i=0;i<keys.Length;i++)
            {
                var item=new ComboBoxItem();item.SetResourceReference(ContentControl.ContentProperty,simple?keys[i]:spatialKeys[i]);
                if(i==13)item.SetResourceReference(ToolTipProperty,"Speaker.InversePlotHint");
                PlotSubject.Items.Add(item);
            }
            PlotSubject.SelectedIndex=selected<0||selected>=keys.Length||(simple&&(selected is 4 or 5||selected>=7))?1:selected;
            if(!simple)PlotGroup.SelectedIndex=SubjectGroups[PlotSubject.SelectedIndex];
            FilterSubjects();
        }
        finally{configuringSubjects=false;}
    }
    void FilterSubjects()
    {
        if(vm==null)return;
        for(int i=0;i<PlotSubject.Items.Count;i++)
            ((ComboBoxItem)PlotSubject.Items[i]).Visibility=(vm.SimpleSpeaker?(i is not (4 or 5)&&i<7):(i<14||vm.SpeakerResult?.BandResult!=null)&&SubjectGroups[i]==Math.Max(0,PlotGroup.SelectedIndex))?Visibility.Visible:Visibility.Collapsed;
        bool inverse=vm.SpatialSpeaker&&PlotSubject.SelectedIndex==13;
        ((ComboBoxItem)PlotKind.Items[1]).IsEnabled=!inverse;((ComboBoxItem)PlotKind.Items[2]).IsEnabled=!inverse;
        if(inverse&&PlotKind.SelectedIndex is 1 or 2)PlotKind.SelectedIndex=0;
    }
    void GroupChanged(object sender,RoutedEventArgs e)
    {
        if(vm?.SpatialSpeaker!=true||configuringSubjects)return;
        configuringSubjects=true;
        try{PlotSubject.SelectedIndex=GroupDefaults[Math.Max(0,PlotGroup.SelectedIndex)];FilterSubjects();}
        finally{configuringSubjects=false;}
        _=RefreshAsync();
    }
    void Refresh(){ConfigureSubjects();if(vm?.IsSpeaker==true){configuringSubjects=true;try{if(PlotSubject.SelectedIndex>=14&&vm.SpeakerResult?.BandResult==null)PlotSubject.SelectedIndex=12;FilterSubjects();}finally{configuringSubjects=false;}}_=RefreshAsync();}
    void PlotChanged(object sender,RoutedEventArgs e)
    {
        if(vm==null||configuringSubjects)return;
        if(vm.SpatialSpeaker&&PlotSubject.SelectedIndex>=0)
        {
            configuringSubjects=true;
            try{PlotGroup.SelectedIndex=SubjectGroups[PlotSubject.SelectedIndex];FilterSubjects();}
            finally{configuringSubjects=false;}
        }
        Refresh();
    }
    public async Task RefreshAsync()
    {
        int request=++version;var model=vm??DataContext as MainViewModel;var result=model?.Result;
        if(result==null){ResultPlot.Data=null;return;}
        int subject=Math.Max(0,PlotSubject.SelectedIndex),kind=Math.Max(0,PlotKind.SelectedIndex);
        PlotBandpass.IsEnabled=kind==0;
        try
        {
            var data=await model!.GetAnalysisAsync(subject,kind,PlotSmooth.IsChecked==true,PlotBandpass.IsChecked==true);
            if(request==version&&!ReferenceEquals(ResultPlot.Data,data))ResultPlot.Data=data;
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(request==version)model!.SetStatus(SoundstageIR.Core.TextCatalog.T("T5EA1E81727")+ex.Message);}
    }
    void ResetPlotView(object sender,RoutedEventArgs e)=>ResultPlot.ResetView();
    void ShowAllCurves(object sender,RoutedEventArgs e)=>ResultPlot.ShowAll();
}
