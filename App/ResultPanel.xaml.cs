using System.Windows;
using System.Windows.Controls;
namespace StatisticalFieldStudio;
public partial class ResultPanel:UserControl
{
    MainViewModel? vm;
    int version;
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
        (vm??DataContext as MainViewModel)?.SetStatus("请检查红框内的数值。");return false;
    }
    void GenerationEdited(object sender,RoutedEventArgs e)
    {
        if(vm==null)return;
        if(CommitEdits())vm.Commit();
        vm.PendingChanged();
    }
    void Connect()
    {
        var next=DataContext as MainViewModel;if(ReferenceEquals(next,vm))return;
        Disconnect();vm=next;if(vm!=null){vm.VisualChanged+=Refresh;Refresh();}
    }
    void Disconnect(){if(vm!=null)vm.VisualChanged-=Refresh;vm=null;version++;}
    void Refresh()=>_=RefreshAsync();
    void PlotChanged(object sender,RoutedEventArgs e){if(vm!=null)Refresh();}
    public async Task RefreshAsync()
    {
        int request=++version;var model=vm??DataContext as MainViewModel;var result=model?.Result;
        if(result==null){ResultPlot.Data=null;return;}
        int subject=Math.Max(0,PlotSubject.SelectedIndex),kind=Math.Max(0,PlotKind.SelectedIndex);
        try
        {
            var data=await model!.AnalysisCache.GetAsync(result,model.Selected?.Id,subject,kind,PlotSmooth.IsChecked==true);
            if(request==version&&!ReferenceEquals(ResultPlot.Data,data))ResultPlot.Data=data;
        }
        catch(Exception ex){if(request==version)model!.SetStatus("分析失败："+ex.Message);}
    }
    void ResetPlotView(object sender,RoutedEventArgs e)=>ResultPlot.ResetView();
    void ShowAllCurves(object sender,RoutedEventArgs e)=>ResultPlot.ShowAll();
}
