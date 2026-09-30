using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace StatisticalFieldStudio;
public partial class MainWindow:Window
{
    public MainViewModel VM {get;}=new();
    bool loaded,navigating;
    bool? stackedLayout;
    double settingsRatio=.6,stackedSettingsRatio=.5;
    SourceEditorWindow? sourceWindow;
    public MainWindow()
    {
        InitializeComponent();DataContext=VM;ResizePreviewHost.Install(this);
        VM.CommitTemplateEdits=CommitConfiguration;
        VM.HasTemplateEdits=()=>EditorInput.HasDirty(SettingsBody)||AnalysisArea.HasPendingEdits||(sourceWindow?.HasPendingEdits??false);
        VM.RequestTemplate=card=>{var dialog=new TemplateDialog(card){Owner=this};return dialog.ShowDialog()==true?dialog.Selection:null;};
        VM.PresetApplied+=()=>{ShowConfiguration();SettingsScroll.ScrollToTop();};
        SettingsBody.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Dispatcher.BeginInvoke(()=>VM.PendingChanged())));
    }
    void OpenGuide(object sender,RoutedEventArgs e)
    {
        string guide=Path.Combine(VM.Root,"docs","guide.html");
        if(!File.Exists(guide)){MessageBox.Show(this,"未找到随程序附带的 docs/guide.html。请保留完整发布文件夹。","使用指南");return;}
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(guide){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"打开指南失败");}
    }
    void WindowLoaded(object sender,RoutedEventArgs e)
    {
        if(loaded)return;loaded=true;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);
        ConfigurationLayout.SizeChanged+=(_,_)=>FitViewport();FitViewport();
    }
    void Edited(object sender,RoutedEventArgs e)
    {
        if(!loaded)return;
        if(sender is TextBox box)EditorInput.Update(box);
        VM.Commit();VM.PendingChanged();
    }
    void ShowConfiguration(){Pages.SelectedItem=ConfigurationPage;FitViewport();}
    void RememberSplit()
    {
        if(stackedLayout==false)
            settingsRatio=SettingsColumn.Width.Value/(SettingsColumn.Width.Value+AnalysisColumn.Width.Value);
        if(stackedLayout==true)
            stackedSettingsRatio=SettingsRow.Height.Value/(SettingsRow.Height.Value+AnalysisRow.Height.Value);
    }
    void SplitCompleted(object sender,System.Windows.Controls.Primitives.DragCompletedEventArgs e)=>RememberSplit();
    void FitViewport()
    {
        if(AnalysisArea==null||Content is ResizePreviewHost {IsReflowDeferred:true})return;
        bool stacked=ConfigurationLayout.ActualWidth<1020;
        if(stackedLayout==stacked)return;
        RememberSplit();stackedLayout=stacked;
        SettingsColumn.MinWidth=stacked?0:380;AnalysisColumn.MinWidth=stacked?0:360;
        SettingsRow.MinHeight=stacked?110:0;AnalysisRow.MinHeight=stacked?400:0;
        SettingsColumn.Width=new GridLength(stacked?1:settingsRatio,GridUnitType.Star);
        AnalysisColumn.Width=stacked?new GridLength(0):new GridLength(1-settingsRatio,GridUnitType.Star);
        SplitterColumn.Width=new GridLength(stacked?0:12);
        SettingsRow.Height=new GridLength(stacked?stackedSettingsRatio:1,GridUnitType.Star);
        SplitterRow.Height=new GridLength(stacked?12:0);
        AnalysisRow.Height=stacked?new GridLength(1-stackedSettingsRatio,GridUnitType.Star):new GridLength(0);
        Grid.SetRow(AnalysisArea,stacked?2:0);Grid.SetColumn(AnalysisArea,stacked?0:2);
        Grid.SetRow(ConfigurationSplitter,stacked?1:0);Grid.SetColumn(ConfigurationSplitter,stacked?0:1);
        ConfigurationSplitter.ResizeDirection=stacked?GridResizeDirection.Rows:GridResizeDirection.Columns;
        ConfigurationSplitter.Cursor=stacked?System.Windows.Input.Cursors.SizeNS:System.Windows.Input.Cursors.SizeWE;
        AnalysisArea.Margin=stacked?new Thickness(0,8,0,0):new Thickness(8,0,0,0);
    }
    void WorkflowChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!loaded||navigating||!ReferenceEquals(e.Source,Pages))return;
        if(e.RemovedItems.Contains(ConfigurationPage)&&!CommitConfiguration())
        {navigating=true;Pages.SelectedItem=ConfigurationPage;navigating=false;return;}
        if(Pages.SelectedItem==ConfigurationPage)FitViewport();
    }
    async void EditCurrentTemplate(object sender,RoutedEventArgs e)
    {
        if(!CommitConfiguration())return;
        var dialog=new TemplateDialog(VM.P){Owner=this};
        if(dialog.ShowDialog()==true&&dialog.Selection is {} choice)await VM.ApplyTemplateAsync(choice);
    }
    void OpenAdvanced(object sender,RoutedEventArgs e)
    {
        if(!CommitConfiguration())return;
        using var scope=new SourceWindowScope(this);
        sourceWindow!.ShowDialog();
    }
    // Same lifetime is exercised offscreen without opening or focusing a window.
    internal sealed class SourceWindowScope:IDisposable
    {
        readonly MainWindow main;
        public SourceEditorWindow Window=>main.sourceWindow!;
        public SourceWindowScope(MainWindow main){this.main=main;main.sourceWindow=new SourceEditorWindow(main.VM);if(main.IsVisible)main.sourceWindow.Owner=main;}
        public void Dispose(){main.sourceWindow!.Close();main.sourceWindow=null;main.VM.PendingChanged();}
    }
    internal bool CommitConfiguration()
    {
        EditorInput.Commit(SettingsBody);
        if(EditorInput.HasErrors(SettingsBody)){VM.SetStatus("请检查红框内的数值。");return false;}
        if(!AnalysisArea.CommitEdits()||sourceWindow?.CommitEdits()==false)return false;
        VM.Commit();VM.PendingChanged();return true;
    }
    void AudioDragOver(object sender,DragEventArgs e){e.Effects=VM.Ready&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    void AudioDrop(object sender,DragEventArgs e){if(VM.Ready&&e.Data.GetData(DataFormats.FileDrop) is string[] {Length:>0} files)VM.AudioInput=files[0];e.Handled=true;}
}
