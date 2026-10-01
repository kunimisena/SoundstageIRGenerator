using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoundstageIR.Core;
using SoundstageIR.Core.Speakers;
namespace SoundstageIRGenerator;
public static class SpeakerWorkflowChecks
{
    public static async Task<int> Run(string report)
    {
        var checks=new List<string>();var folder=Path.GetDirectoryName(Path.GetFullPath(report))!;Directory.CreateDirectory(folder);
        App.TestRoot=Path.Combine(folder,"workspace");
        try
        {
            foreach(string language in new[]{"zh-CN","en"})
            {
                TextCatalog.SetLanguage(language);UiLanguage.ApplyResources();
                var chooser=new SpeakerModeDialog();var root=(FrameworkElement)chooser.Content;root.Measure(new Size(640,400));root.Arrange(new Rect(0,0,640,400));root.UpdateLayout();
                foreach(var name in new[]{"SimpleButton","SpatialButton"})
                {var b=(Button)chooser.FindName(name);if(b.ContentTemplate!=null||b.Content is not StackPanel)throw new Exception("Mode card content template");checks.Add("PASS "+language+" mode card "+name);}
                SaveImage(root,Path.Combine(folder,"mode-"+language+".png"),640,400);chooser.Close();
                foreach(var mode in Enum.GetValues<SpeakerMode>())
                {var window=new MainWindow(mode);await window.CheckSpeakerWorkflow(folder,language,checks);window.Close();}
            }
            File.WriteAllLines(report,checks.Append($"PASS {checks.Count} assertions"));return 0;
        }
        catch(Exception e){File.WriteAllLines(report,checks.Append(e.ToString()));return 1;}
    }
    public static void SaveImage(FrameworkElement root,string file,int width,int height)
    {var caches=new List<(UIElement,CacheMode)>();void Uncache(DependencyObject v){if(v is UIElement e&&e.CacheMode is {} c){caches.Add((e,c));e.CacheMode=null;}for(int i=0;i<VisualTreeHelper.GetChildrenCount(v);i++)Uncache(VisualTreeHelper.GetChild(v,i));}Uncache(root);root.UpdateLayout();var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);foreach(var (e,c) in caches)e.CacheMode=c;var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);png.Save(stream);}
}
public partial class MainWindow
{
    internal async Task CheckSpeakerWorkflow(string folder,string language,List<string> checks)
    {
        void Check(bool okay,string what){if(!okay)throw new Exception(what);checks.Add("PASS "+language+" "+VM.Speaker!.Mode+" "+what);}
        async Task Layout(int w=1320,int h=880)
        {var root=(FrameworkElement)Content;root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();FitViewport();root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);}
        IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var d in Descendants<T>(child))yield return d;}}
        loaded=true;Pages.SelectedItem=PresetPage;await Layout();
        Check(Pages.Items.Count==3,"Original three workflow pages");
        var cards=Descendants<Button>((DependencyObject)PresetPage.Content).Where(b=>b.Command==VM.PresetCommand).ToArray();
        Check(cards.Length==VM.PresetCards.Count&&cards.Length>8,"All template cards rendered");
        Check(cards.All(b=>b.ContentTemplate==null&&Descendants<TextBlock>(b).Any(t=>t.Text.Length>0)),"Template labels use original card template");
        Check(!Descendants<TextBlock>((DependencyObject)PresetPage.Content).Any(t=>t.Text.Contains("System.Windows.Controls")),"No component type text on template page");
        SpeakerWorkflowChecks.SaveImage((FrameworkElement)Content,Path.Combine(folder,$"templates-{VM.Speaker!.Mode}-{language}.png"),1320,880);
        Pages.SelectedItem=ConfigurationPage;
        foreach(var size in new[]{new Size(800,700),new Size(1320,880),new Size(1920,1080)}){await Layout((int)size.Width,(int)size.Height);Check(AnalysisArea.ActualWidth>0,"Responsive shared layout "+size.Width);}
        var p=SpeakerProject.FromPreset(Presets.BuiltIn.Single(x=>x.Name==(VM.SpatialSpeaker?"宽阔监听":"自由场")),VM.Speaker.Mode);VM.SetSpeakerProject(p);Pages.SelectedItem=ConfigurationPage;await Layout();
        if(VM.SpatialSpeaker)
        {
            Check(SettingsBody.Children.IndexOf(ActualSpeakerControls)<SettingsBody.Children.IndexOf(DirectControls)&&SettingsBody.Children.IndexOf(DirectControls)<SettingsBody.Children.IndexOf(HeadControls),"Actual then virtual then head configuration order");
            Check(SettingsBody.Children.IndexOf(SpeakerBands)==SettingsBody.Children.IndexOf(HeadControls)+1,"Listener-head inverse is directly below head settings");
            Check(!Descendants<TextBox>(ActualSpeakerControls).Any(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="Speaker.MaximumInverseGainDb")&&Descendants<TextBox>(SpeakerBands).Any(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="Speaker.MaximumInverseGainDb"),"Inverse gain belongs to the listener-head inverse panel");
            Check(VM.Speaker!.AirAbsorption,"Actual-speaker air starts enabled");
            Check(VM.Speaker!.InverseLowHz==200&&VM.Speaker.InverseHighHz==10000,"Default inverse crossovers");
            var energy=(CurveEditor)SpeakerBands.FindName("Energy");var decay=(CurveEditor)SpeakerBands.FindName("Decay");
            Check(!energy.CanEditAt(1000)&&energy.CanEditAt(80)&&energy.CanEditAt(20000),"Energy editor locks only the inverse band");
            Check(!decay.CanEditAt(1000)&&decay.CanEditAt(80),"Decay editor shares locked region");
            Check(energy.Points!.All(k=>k.X<=200||k.X>=10000)&&decay.Points!.All(k=>k.X<=200||k.X>=10000),"Editors contain no hidden middle-band knots");
            Check(double.IsNaN(energy.ValueAt(1000))&&double.IsNaN(decay.ValueAt(1000)),"No middle-band curve is interpolated");
            Check(energy.CanEditAt(200)&&energy.CanEditAt(10000),"Crossover endpoint values remain editable");
            double lowDecay=decay.ValueAt(100);var highDecayPoint=decay.Points!.Last();double highDecayBefore=highDecayPoint.Y;highDecayPoint.Y*=2;
            Check(decay.ValueAt(100)==lowDecay,"High-side decay edit cannot bend the low-side interpolation");highDecayPoint.Y=highDecayBefore;
            var lowSlider=(Slider)SpeakerBands.FindName("LowSlider");var highSlider=(Slider)SpeakerBands.FindName("HighSlider");var lowBox=(TextBox)SpeakerBands.FindName("Low");
            lowSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
            lowSlider.Value=Math.Log10(400d/20);
            Check(lowBox.Text=="400"&&energy.LockedMinX==400&&VM.Speaker.InverseLowHz==200,"Drag previews frequency and grey range without repeatedly committing the model");
            lowSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
            Check(VM.Speaker.InverseLowHz==400,"Slider release commits frequency");Check(VM.Speaker.OutsideParameters().Low.Decay.Last().X==400&&decay.Points!.All(k=>k.X<=400||k.X>=10000),"Slider moves the actual curve boundary without adding middle knots");VM.Undo();Check(VM.Speaker.InverseLowHz==200,"One undo restores the whole slider drag");VM.Redo();Check(VM.Speaker.InverseLowHz==400,"Slider redo restores frequency");VM.Undo();
            lowSlider.Value=0;highSlider.Value=3;Check(VM.Speaker.InverseLowHz==20&&VM.Speaker.InverseHighHz==20000,"Sliders can select exact full-band endpoints");VM.Undo();VM.Undo();
            lowBox.Text="200.125";lowBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Check(VM.Speaker.InverseLowHz==200.125&&Math.Abs(lowSlider.Value-Math.Log10(200.125/20))<1e-12,"Typed precision is retained and slider follows it");VM.Undo();
            lowSlider.Value=3;Check(VM.Speaker.InverseLowHz<VM.Speaker.InverseHighHz,"Dragging a crossover cannot cross the other one");VM.Undo();
            double originalEnergy=VM.Speaker.OutsideParameters().Low.Energy[0].Y;
            energy.Points![0].Y+=3;
            typeof(SpeakerBandEditor).GetMethod("Changed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(SpeakerBands,null);
            Check(VM.Speaker.OutsideParameters().Low.Energy[0].Y==originalEnergy+3,"Curve edits reach the persisted project");
            VM.Undo();Check(VM.Speaker.OutsideParameters().Low.Energy[0].Y==originalEnergy,"Band curve undo");VM.Redo();Check(VM.Speaker.OutsideParameters().Low.Energy[0].Y==originalEnergy+3,"Band curve redo");VM.Undo();
            var savedBand=ProjectIO.Clone(VM.Speaker);VM.Speaker.InverseLowHz=20;VM.Speaker.InverseHighHz=20000;VM.Commit();
            Check(!energy.CanEditAt(20)&&!energy.CanEditAt(20000),"Full range locks the whole curve");VM.SetSpeakerProject(savedBand);await Layout();
            var settledPrecheck=VM.UpdateSpeakerPrecheckAsync(true);await settledPrecheck;await Layout();
            SettingsScroll.ScrollToVerticalOffset(SpeakerBands.TranslatePoint(new Point(0,0),SettingsBody).Y);await Layout();
            double scrollBefore=SettingsScroll.VerticalOffset,extentBefore=SettingsScroll.ExtentHeight,bandTopBefore=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y;
            var editedCurve=(CurveEditor)SpeakerBands.FindName("Energy");editedCurve.Points![0].Y+=1;
            typeof(SpeakerBandEditor).GetMethod("Changed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(SpeakerBands,null);
            await Layout();
            checks.Add($"MEASURE curve commit: scroll {SettingsScroll.VerticalOffset-scrollBefore:0.###}, extent {SettingsScroll.ExtentHeight-extentBefore:0.###}, panel top {SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-bandTopBefore:0.###} DIP");
            Check(ReferenceEquals(settledPrecheck,VM.UpdateSpeakerPrecheckAsync()),"Outside-band curve edit does not restart geometry precheck");
            Check(Math.Abs(SettingsScroll.VerticalOffset-scrollBefore)<.1&&Math.Abs(SettingsScroll.ExtentHeight-extentBefore)<.1&&Math.Abs(SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-bandTopBefore)<.1,"Curve commit keeps scroll position and layout height stable");
            VM.Undo();await Layout();
            await VM.UpdateSpeakerPrecheckAsync(true);await Layout();
            double sliderScroll=SettingsScroll.VerticalOffset,sliderExtent=SettingsScroll.ExtentHeight,sliderTop=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y;
            lowSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
            foreach(double hz in new[]{250d,300d,400d}){lowSlider.Value=Math.Log10(hz/20);await Layout();}
            double duringTop=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-sliderTop;
            lowSlider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
            await Layout();
            double pendingTop=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-sliderTop;
            checks.Add($"MEASURE crossover: dragging top {duringTop:0.###}, pending top {pendingTop:0.###}, scroll {SettingsScroll.VerticalOffset-sliderScroll:0.###}, extent {SettingsScroll.ExtentHeight-sliderExtent:0.###} DIP");
            await VM.UpdateSpeakerPrecheckAsync();await Layout();
            double settledTop=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-sliderTop;
            checks.Add($"MEASURE crossover settled: top {settledTop:0.###}, scroll {SettingsScroll.VerticalOffset-sliderScroll:0.###}, extent {SettingsScroll.ExtentHeight-sliderExtent:0.###} DIP");
            Check(Math.Abs(duringTop)<.1&&Math.Abs(pendingTop)<.1&&Math.Abs(settledTop)<.1,"Crossover drag, commit and asynchronous precheck keep editor position stable");
            VM.Undo();await VM.UpdateSpeakerPrecheckAsync();await Layout();
            foreach(var (slider,hz) in new[]{(highSlider,8000d),(lowSlider,20d)})
            {
                double top=SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y,offset=SettingsScroll.VerticalOffset,extent=SettingsScroll.ExtentHeight;
                slider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});slider.Value=Math.Log10(hz/20);await Layout();
                Check(Math.Abs(SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-top)<.1,"Both crossover handles keep position during drag "+hz);
                slider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});await Layout();
                Check(Math.Abs(SettingsScroll.ExtentHeight-extent)<.1&&Math.Abs(SettingsScroll.VerticalOffset-offset)<.1,"Checking status retains scroll extent and offset "+hz);
                await VM.UpdateSpeakerPrecheckAsync();await Layout();
                Check(Math.Abs(SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y-top)<.1&&Math.Abs(SettingsScroll.ExtentHeight-extent)<.1,"Completed crossover precheck retains layout "+hz);
                VM.Undo();await VM.UpdateSpeakerPrecheckAsync();await Layout();
            }
            Check(SpeakerBands.ActualHeight>400&&SpeakerBands.TranslatePoint(new Point(0,0),SettingsScroll).Y<40,"Listener-head inverse panel appears in the settings viewport");
            SpeakerWorkflowChecks.SaveImage((FrameworkElement)Content,Path.Combine(folder,$"bands-{language}.png"),1320,880);
            SettingsScroll.ScrollToTop();await Layout();

            var backup=ProjectIO.Clone(VM.Speaker!);
            VM.Speaker!.RightSpeaker=ProjectIO.Clone(VM.Speaker.LeftSpeaker);VM.Commit();await VM.UpdateSpeakerPrecheckAsync(true);await Layout();
            // Wait for a coalesced automatic request, if it started on Commit.
            for(int retry=0;retry<100&&!VM.SpeakerPrecheckRisk;retry++)await Task.Delay(25);
            Check(VM.SpeakerPrecheckRisk,"Problem geometry sets red precheck state");
            var placement=Descendants<SpeakerPlacementEditor>(SettingsBody).Single();
            var riskText=(TextBlock)placement.FindName("RiskText");await Layout();
            Check(riskText.Foreground is SolidColorBrush brush&&brush.Color==Color.FromRgb(177,45,40),"Precheck warning is visibly red");
            VM.SetSpeakerProject(backup);await Layout();
        }
        Check(SimpleKernels.Visibility==(VM.SimpleSpeaker?Visibility.Visible:Visibility.Collapsed),"Mode-specific kernel editor");
        if(VM.SimpleSpeaker)
        {
            Check(SimpleKernels.CommitEdits(),"Shared curve editor commits");var before=VM.Speaker.Left.Rt(1000);
            VM.Speaker.Left.Decay.ForEach(k=>k.Y*=1.1);VM.Commit();VM.Undo();Check(Math.Abs(VM.Speaker.Left.Rt(1000)-before)<1e-9,"Speaker kernel undo: expected "+before+" actual "+VM.Speaker.Left.Rt(1000)+" status "+VM.Status);VM.Redo();Check(VM.Speaker.Left.Rt(1000)>before,"Speaker kernel redo");VM.Undo();
            var dialog=new SimpleSpeakerTemplateDialog(ProjectIO.Clone(VM.Speaker));var body=(FrameworkElement)dialog.Content;body.Measure(new Size(880,760));body.Arrange(new Rect(0,0,880,760));body.UpdateLayout();Check(dialog.Accept(false)&&dialog.Selection?.Speaker!=null,"Simple template preserves kernel configuration");dialog.Close();
        }
        else
        {
            using var sources=new SourceWindowScope(this);Check(ReferenceEquals(sources.Window.DataContext,VM),"Original advanced source window shares project");
            var dialog=new TemplateDialog(VM.P,allowGenerate:false);var templateRoot=(FrameworkElement)dialog.Content;templateRoot.Measure(new Size(1060,800));templateRoot.Arrange(new Rect(0,0,1060,800));templateRoot.UpdateLayout();
            Check(!Descendants<Button>(templateRoot).Any(b=>Equals(b.Content,TextCatalog.T("T368672781F"))),"Complex template hides confirm-and-generate");
            Check(dialog.CreateSelection(true) is {Generate:false},"Complex template always enters configuration before generation");dialog.Close();
        }
        Check(await VM.GenerateAsync(),"Generate using shared page command");await AnalysisArea.RefreshAsync();await Layout();
        Check(VM.CanExport,"Generated result can export");
        if(VM.SpatialSpeaker)
        {
            Check(VM.SpeakerResult!.Checks!=null,"Actual generated kernels have diagnostics");
            Check(VM.SpeakerResultRisk==VM.SpeakerResult.Checks!.HasRisk,"Result warning banner follows actual checks");
            var actualResult=VM.SpeakerResult;
            var warning=actualResult with{Checks=new(new(){new("test","检查提示示例","Check notification sample")},0,0)};
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.SpeakerResult))!.SetValue(VM,warning);
            await VM.UpdateSpeakerPrecheckAsync();await Layout();
            Check(VM.SpeakerResultRisk&&Descendants<Border>(AnalysisArea).Any(b=>b.Visibility==Visibility.Visible&&b.BorderBrush is SolidColorBrush c&&c.Color==Color.FromRgb(177,45,40)),"Generated risk has visible red result banner");
            Pages.SelectedItem=ExportPage;await Layout();
            Check(Descendants<Border>((DependencyObject)ExportPage.Content).Any(b=>b.Visibility==Visibility.Visible&&b.ActualWidth>0&&b.ActualHeight>0&&b.BorderBrush is SolidColorBrush c&&c.Color==Color.FromRgb(177,45,40)),"Export page exposes generated risk banner");
            SpeakerWorkflowChecks.SaveImage((FrameworkElement)Content,Path.Combine(folder,$"export-risk-{language}.png"),1320,880);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.SpeakerResult))!.SetValue(VM,actualResult);await VM.UpdateSpeakerPrecheckAsync();Pages.SelectedItem=ConfigurationPage;await Layout();
        }
        var combo=(ComboBox)AnalysisArea.FindName("PlotSubject");combo.SelectedIndex=1;await AnalysisArea.RefreshAsync();
        var plot=(PlotView)AnalysisArea.FindName("ResultPlot");Check(plot.Data?.Lines.Count==2,"Final L/R plot has two curves");
        var expected=Dsp.PowerAt(Dsp.Sum(VM.SpeakerResult!.Drive[0],VM.SpeakerResult.Drive[1]),VM.P.SampleRate).Select(Dsp.Db).ToArray();
        Check(expected.Zip(plot.Data!.Lines[0].Y).All(v=>Math.Abs(v.First-v.Second)<1e-9),"Final L response equals exported matrix sum");
        foreach(int kind in Enumerable.Range(0,5)){var data=await VM.GetAnalysisAsync(1,kind,false,false);Check(data.Lines.Count==2&&data.Lines.All(l=>l.Y.All(double.IsFinite)),"Shared analysis kind "+kind);}
        var kindBox=(ComboBox)AnalysisArea.FindName("PlotKind");
        if(VM.SpatialSpeaker)
        {
            var groups=(ComboBox)AnalysisArea.FindName("PlotGroup");
            for(int group=0;group<5;group++)
            {
                groups.SelectedIndex=group;await AnalysisArea.RefreshAsync();await Layout();
                Check(combo.Items.Cast<ComboBoxItem>().Count(x=>x.Visibility==Visibility.Visible) is >=2 and <=5,"Plot category has a short visible list "+group);
            }
            groups.SelectedIndex=1;await AnalysisArea.RefreshAsync();await Layout();
            Check(combo.SelectedIndex==13&&plot.Data!.Lines.Count==4,"Inverse category opens actual inverse operator");
            var response=VM.SpeakerResult!.InverseResponse!;
            var visibleIndices=Enumerable.Range(0,response.Frequencies.Length).Where(i=>response.Frequencies[i]>=20&&response.Frequencies[i]<=20000).ToArray();
            Check(plot.Data!.Lines[0].Y.SequenceEqual(visibleIndices.Select(i=>response.MagnitudeDb[0][i])),"Inverse plot uses recorded solver coefficients");
            Check(!((ComboBoxItem)kindBox.Items[1]).IsEnabled&&!((ComboBoxItem)kindBox.Items[2]).IsEnabled,"Frequency operator does not pretend to be a causal impulse or decay curve");
            SpeakerWorkflowChecks.SaveImage((FrameworkElement)Content,Path.Combine(folder,$"inverse-{language}.png"),1320,880);
            // Exercise actual routed selection events and WPF drawing, including EQ FFTs.
            for(int subject=0;subject<combo.Items.Count;subject++)for(int kind=0;kind<5;kind++)
            {
                combo.SelectedIndex=subject;kindBox.SelectedIndex=kind;
                await AnalysisArea.RefreshAsync();await Layout();
                Check(combo.Items.Count==17,"Stable plot items after selection "+subject+"/"+kind);
                Check(plot.Data!=null&&plot.Data.Lines.All(l=>l.Y.All(double.IsFinite)),"Finite rendered plot "+subject+"/"+kind);
            }
            int builds=VM.SpeakerAnalysis.BuildCount;
            for(int i=0;i<150;i++){combo.SelectedIndex=i%17;kindBox.SelectedIndex=i%5;}
            combo.SelectedIndex=12;kindBox.SelectedIndex=0;await AnalysisArea.RefreshAsync();await Layout();
            Check(VM.SpeakerAnalysis.BuildCount-builds<8,"Rapid plot switches discard obsolete queued analyses");
            Check(plot.Data?.Lines.Count==4,"Target/cascade overlay completes after rapid switching");
            Pages.SelectedItem=PresetPage;await Layout();Pages.SelectedItem=ConfigurationPage;await Layout();
            await AnalysisArea.RefreshAsync();Check(combo.Items.Count==17,"Returning to configuration preserves subject list");
            checks.Add(PlotRenderChecks.Measure(plot.Data!));
        }
        combo.SelectedIndex=1;kindBox.SelectedIndex=0;await AnalysisArea.RefreshAsync();await Layout();
        plot.ToggleLine(plot.Data!.Lines[0].Name);Check(!plot.IsLineVisible(plot.Data.Lines[0].Name),"Legend interaction");plot.ShowAll();plot.Zoom(.8);Check(plot.IsZoomed,"Zoom interaction");plot.ResetView();
        VM.P.Name="Speaker workflow";VM.Commit();Check(VM.CanExport,"Renaming keeps current result");
        var config=Path.Combine(folder,"speaker-"+VM.Speaker.Mode+".json");VM.SaveProjectFile(config);Check(SpeakerProject.Load(config).Mode==VM.Speaker.Mode,"Mode-aware project save");
        VM.ExportParent=Path.Combine(folder,"exports");Check(await VM.ExportAsync() is {} path&&Directory.Exists(path),"Shared export command");
        VM.P.OutputDb+=1;VM.Commit();Check(!VM.CanExport,"Stale result blocks export");VM.Undo();Check(VM.CanExport,"Undo restores matching result");
        foreach(double scale in new[]{1.0,1.5,2.0}){Pages.LayoutTransform=new ScaleTransform(scale,scale);await Layout((int)(1320*scale),(int)(880*scale));Check(AnalysisArea.ActualWidth>0,"DPI layout "+scale);}Pages.LayoutTransform=Transform.Identity;
        await Layout();
        Check(((TextBox)AnalysisArea.FindName("NameEditor")).Text==VM.P.Name,"Configuration name binding visible");
        if(VM.SpatialSpeaker)Check(Descendants<TextBox>(SettingsBody).Any(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path=="Speaker.LeftSpeaker.Distance"&&b.Text=="1.7"),"Actual distance binding visible");
        VM.SetStatus(TextCatalog.T("T2C954ED050"));
        SpeakerWorkflowChecks.SaveImage((FrameworkElement)Content,Path.Combine(folder,$"configuration-{VM.Speaker.Mode}-{language}.png"),1320,880);
        VM.LoadProjectFile(config);Check(!VM.CanExport,"Loaded project awaits generation");
    }
}
