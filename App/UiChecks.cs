using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;
public partial class MainWindow
{
    // Offscreen checks never show or focus a desktop window.
    public async Task<int> RunHeadlessChecks(string folder)
    {
        Directory.CreateDirectory(folder);var checks=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new Exception(name);checks.Add("PASS "+name);}
        var client=(FrameworkElement)Content;
        async Task Layout(FrameworkElement target,double width=1280,double height=820,double scale=1)
        {
            target.LayoutTransform=new ScaleTransform(scale,scale);
            target.Measure(new Size(width,height));target.Arrange(new Rect(0,0,width,height));target.UpdateLayout();
            if(ReferenceEquals(target,client)){FitViewport();target.Measure(new Size(width,height));target.Arrange(new Rect(0,0,width,height));target.UpdateLayout();}
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        }
        void Capture(FrameworkElement target,string name,double width=1280,double height=820)
        {
            var bmp=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();
            using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle(Paint.Paper,null,new Rect(0,0,width,height));bmp.Render(backdrop);bmp.Render(target);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,name));encoder.Save(file);
        }
        IEnumerable<T> All<T>(DependencyObject item) where T:DependencyObject
        {
            if(item is T t)yield return t;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(item,i)))yield return child;
        }
        try
        {
            WindowLoaded(this,new RoutedEventArgs());await Layout(client);
            string initialAudioPath=VM.SelectedFfmpegPath;
            string beforeTools=ProjectIO.Serialize(VM.P);
            VM.SetAudioToolsPath(Path.Combine(folder,"missing tools","ffmpeg.exe"));
            Check(!VM.AudioToolsAvailable&&VM.AudioToolStatus.Contains("找不到"),"Invalid selected FFmpeg has visible error");
            Check(!VM.RenderAudioCommand.CanExecute(null),"Missing tools block song processing");
            var reopenedAudio=new MainViewModel();
            Check(reopenedAudio.SelectedFfmpegPath==VM.SelectedFfmpegPath&&!reopenedAudio.AudioToolsAvailable,"Local tool selection persists on reopen without fallback");
            Check(ProjectIO.Serialize(VM.P)==beforeTools,"Tool selection leaves sound-field parameters unchanged");
            VM.SetAudioToolsPath(initialAudioPath);
            Check(FfmpegPathBox.IsReadOnly&&FfmpegPathBox.GetBindingExpression(TextBox.TextProperty)!=null,"Selected tool path remains selectable and bound");

            Check(!IsVisible,"Main window stays invisible");
            Check(Pages.Items.Count==3&&Content is ResizePreviewHost host&&ReferenceEquals(host.Page,Pages),"Three pages with no external action bar");
            Check(VM.P.Name=="宽阔监听"&&VM.P.CenterEqStrengthPercent==0&&VM.P.Smooth1==12&&VM.P.Smooth2==3,"Recommended wide monitor preserves 1/12 per-ear smoothing and zero center EQ");
            Check(VM.PresetCards.Count==11,"Ten presets plus blank");Capture(client,"templates.png");
            foreach(var preset in Presets.All)
            {
                var dialog=new TemplateDialog(VM.PresetCards.Single(c=>c.BuiltIn==preset));
                await Layout((FrameworkElement)dialog.Content,1040,780);
                dialog.Editor.FirstPathEditor.RaiseEvent(new RoutedEventArgs(TextBox.LostFocusEvent));
                var selection=dialog.CreateSelection(false);
                Check(selection!=null&&!selection.Generate&&ProjectIO.Serialize(selection.Project)==ProjectIO.Serialize(preset.Create()),"Template no-op preserves every parameter: "+preset.Name);
                var sections=All<Expander>((FrameworkElement)dialog.Content).ToArray();
                Check(sections.Length>=3&&sections.All(e=>e.IsExpanded),"Template sections all open: "+preset.Name);
                if(preset.Name=="宽阔监听")Capture((FrameworkElement)dialog.Content,"template-editor.png",1040,780);
                dialog.Close();
            }
            var numeric=new TemplateDialog(VM.PresetCards.Single(c=>c.Name=="宽阔监听"));
            await Layout((FrameworkElement)numeric.Content,1040,780);
            Check(string.IsNullOrEmpty(numeric.Editor.FirstPathEditor.GetBindingExpression(TextBox.TextProperty)!.ParentBinding.StringFormat),"Numeric editor exposes actual value without display rounding");
            numeric.Editor.FirstPathEditor.Text="0.";Check(numeric.Editor.FirstPathEditor.Text=="0.","Template input keeps decimal while typing");
            numeric.Editor.FirstPathEditor.Text="0.625123456789";var editedTemplate=numeric.CreateSelection(false)!;
            Check(Math.Abs(editedTemplate.Project.TemplateSources.Average(s=>s.Left.FirstReflectionExtraPath)-.625123456789)<1e-12,"Template path edit retains user-entered fine precision");numeric.Close();
            var card=VM.PresetCards.Single(c=>c.Name=="宽阔监听");VM.RequestTemplate=_=>null;VM.PresetCommand.Execute(card);
            Check(Pages.SelectedItem==PresetPage,"Cancel keeps selection page");
            await VM.ApplyTemplateAsync(new(card.BuiltIn!.Create(),false));await Layout(client);
            Check(Pages.SelectedItem==ConfigurationPage&&VM.Result==null,"Confirm enters configuration without generation");
            double leftBefore=SettingsColumn.ActualWidth;
            ConfigurationSplitter.ShowsPreview=false;
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-90,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-90,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
            ConfigurationSplitter.ShowsPreview=true;await Layout(client);
            Check(SettingsColumn.ActualWidth<leftBefore-50,"Splitter drag changes actual settings/plot widths");
            double selectedRatio=SettingsColumn.Width.Value/(SettingsColumn.Width.Value+AnalysisColumn.Width.Value);
            await Layout(client,1500,820);await Layout(client,960,760);
            Check(ConfigurationSplitter.ResizeDirection==GridResizeDirection.Rows&&Grid.GetRow(AnalysisArea)==2,"Narrow layout has a horizontal splitter");
            double topBefore=SettingsRow.ActualHeight;
            ConfigurationSplitter.ShowsPreview=false;
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-0,-30){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
            ConfigurationSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,-30,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
            ConfigurationSplitter.ShowsPreview=true;await Layout(client,960,760);
            Check(SettingsRow.ActualHeight<topBefore-20,"Horizontal splitter changes actual settings/plot heights");
            await Layout(client);
            Check(ConfigurationSplitter.ResizeDirection==GridResizeDirection.Columns&&Math.Abs(SettingsColumn.Width.Value/(SettingsColumn.Width.Value+AnalysisColumn.Width.Value)-selectedRatio)<.001,"Chosen column ratio survives resizing and narrow layout");
            Check(!All<ReflectionBatchEditor>(client).Any(),"Overall template curves only exist in template dialog");
            Check(!VM.CanExport&&!VM.ExportCommand.CanExecute(null)&&VM.ExportState.Contains("尚未生成"),"Missing kernels visibly block export");
            Check(ReferenceEquals(AnalysisArea.GenerateButton.Command,VM.GenerateCommand)&&ReferenceEquals(ExportGenerateButton.Command,VM.GenerateCommand),"Chart and export share generation action");
            double requestedWet=VM.P.ReflectionEnergyPercent;
            DirectSoundToggle.IsChecked=false;await Layout(client);
            Check(!VM.DirectEnabled&&!WetPercentEditor.IsEnabled&&!WetPercentSlider.IsEnabled&&WetPercentEditor.Text=="100"&&WetPercentSlider.Value==100,"Direct-off shows locked 100 percent wet in both controls");
            Check(VM.P.ReflectionEnergyPercent==requestedWet&&VM.Stale,"Direct switch retains requested balance and invalidates kernels");
            VM.ReflectionEnergyPercent=35;
            Check(VM.P.ReflectionEnergyPercent==requestedWet,"Disabled wet control cannot overwrite remembered balance");
            Check(await VM.GenerateAsync()&&Math.Abs(VM.Result!.ReflectionPercentBeforeEq-100)<1e-8,"Direct-off generates pure reflection");
            var wetOnlyFile=Path.Combine(folder,"wet-only.json");ProjectIO.Save(VM.P,wetOnlyFile);
            var wetOnly=ProjectIO.Load(wetOnlyFile);
            Check(!wetOnly.Direct.Enabled&&wetOnly.ReflectionEnergyPercent==requestedWet,"Configuration saves direct-off state and remembered balance");
            var wetTemplate=new TemplateDialog(wetOnly);await Layout((FrameworkElement)wetTemplate.Content,1040,780);
            Check(!wetTemplate.WetPercentEditor.IsEnabled&&wetTemplate.WetPercentEditor.Text=="100"&&ProjectIO.Serialize(wetTemplate.CreateSelection(false)!.Project)==ProjectIO.Serialize(wetOnly),"Template dialog shows pure wet without changing stored parameters");wetTemplate.Close();
            VM.Undo();await Layout(client);
            Check(VM.DirectEnabled&&WetPercentEditor.IsEnabled&&WetPercentSlider.Value==requestedWet,"Undo direct toggle restores wet control and balance");
            VM.Redo();await Layout(client);
            Check(!VM.DirectEnabled&&!WetPercentEditor.IsEnabled&&WetPercentSlider.Value==100,"Redo restores linked pure-wet state");
            VM.SetProject(wetOnly);await Layout(client);DirectSoundToggle.IsChecked=true;await Layout(client);
            Check(WetPercentEditor.IsEnabled&&WetPercentSlider.IsEnabled&&VM.ReflectionEnergyPercent==requestedWet&&WetPercentSlider.Value==requestedWet,"Re-enabling direct after JSON reload restores requested balance");
            Check(await VM.GenerateAsync(),"Generate default kernels");await AnalysisArea.RefreshAsync();await Layout(client);
            Check(VM.CanExport&&VM.ExportState.Contains("可以导出")&&AnalysisArea.ResultPlot.Data!=null,"Fresh kernels displayed and exportable");
            Check(Math.Abs(VM.Result!.ReflectionPercentAfterEq-VM.P.ReflectionEnergyPercent)<.02&&VM.EnergySummary.Contains("目标")&&VM.EnergySummary.Contains("最终"),"Result shows target and measured post-EQ wet balance");
            Check(VM.Metrics.Contains("混响整体预修正"),"Result details expose common wet gain compensation");
            Check(AnalysisArea.PlotBandpass.IsChecked!=true&&AnalysisArea.ResultPlot.Data!.MinY==-60&&AnalysisArea.ResultPlot.Data.Lines[0].X[0]==20,"Default frequency view focuses on audible band");
            var unchangedResult=VM.Result;var standardPlot=AnalysisArea.ResultPlot.Data;
            AnalysisArea.PlotBandpass.IsChecked=true;await AnalysisArea.RefreshAsync();
            Check(AnalysisArea.ResultPlot.Data!.MinY==-100&&AnalysisArea.ResultPlot.Data.Lines[0].X[0]==5,"Bandpass checkbox expands frequency and dB ranges");
            Check(ReferenceEquals(unchangedResult,VM.Result)&&VM.CanExport,"Bandpass display does not invalidate or regenerate kernels");
            AnalysisArea.PlotBandpass.IsChecked=false;await AnalysisArea.RefreshAsync();
            Check(ReferenceEquals(standardPlot,AnalysisArea.ResultPlot.Data),"Returning to normal range reuses cached plot");
            AnalysisArea.PlotKind.SelectedIndex=1;await AnalysisArea.RefreshAsync();
            Check(!AnalysisArea.PlotBandpass.IsEnabled,"Bandpass range applies only to magnitude plots");
            AnalysisArea.PlotKind.SelectedIndex=0;await AnalysisArea.RefreshAsync();
            Check(AnalysisArea.PlotBandpass.IsEnabled,"Magnitude plot restores bandpass control");
            var plotted=AnalysisArea.ResultPlot.Data;int builds=VM.AnalysisCache.BuildCount;
            int visualEvents=0;VM.VisualChanged+=()=>visualEvents++;
            for(int i=0;i<20;i++){VM.Commit();VM.PendingChanged();await AnalysisArea.RefreshAsync();}
            Check(visualEvents==0&&VM.AnalysisCache.BuildCount==builds&&ReferenceEquals(plotted,AnalysisArea.ResultPlot.Data),"No-op focus and refresh reuse FFT and plot data");
            var clock=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++){VM.ExportCommand.CanExecute(null);VM.ApoExportCommand.CanExecute(null);_ = VM.Stale;_ = VM.ExportState;}
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;clock.Stop();
            checks.Add($"MEASURE 1000 command/status queries: {clock.Elapsed.TotalMilliseconds:0.00} ms, {allocated} bytes");
            Check(allocated<1000000,"Command checks do not serialize or traverse editors");
            for(int i=0;i<8;i++)await Layout(client,1240+i*9,820+i*3);
            Check(VM.AnalysisCache.BuildCount==builds,"Resize does not start analysis");
            var plot=AnalysisArea.ResultPlot;plot.ToggleLine(plot.Data!.Lines[0].Name);Check(!plot.IsLineVisible(plot.Data.Lines[0].Name),"Legend toggles");plot.ShowAll();
            await Layout(client);plot.Zoom(.5);Check(plot.IsZoomed,"Zoom");plot.ResetView();Check(!plot.IsZoomed,"Reset");
            var limits=new PlotBounds(0,10,-20,20);Check(limits.Zoom(10,.5,.5).Constrain(limits)==limits,"Zoom bounded");
            WetPercentEditor.Text="17.";await Layout(client);Check(VM.Stale&&!VM.CanExport,"Uncommitted edit invalidates result");
            AnalysisArea.NameEditor.Text="宽阔监听 · 配置验证";AnalysisArea.OutputGainEditor.Text="-1.5";
            Check(CommitConfiguration()&&VM.P.Name=="宽阔监听 · 配置验证"&&VM.P.OutputDb==-1.5,"Generation area commits name and output gain");
            AnalysisArea.SampleRateEditor.SelectedItem=44100;await Layout(client);
            Check(VM.P.SampleRate==44100,"Generation area sample-rate selection updates project");
            AnalysisArea.SampleRateEditor.SelectedItem=48000;
            WetPercentEditor.Text="17.25";AnalysisArea.OutputGainEditor.Text="0";
            Check(await VM.GenerateAsync()&&VM.P.ReflectionEnergyPercent==17.25&&VM.Result!.Project.OutputDb==0&&VM.Result.Project.SampleRate==48000&&VM.Result.Project.Name=="宽阔监听 · 配置验证","Generation commits visible edits including output settings");
            AnalysisArea.OutputGainEditor.Text="invalid";await Layout(client);
            Check(!VM.CanExport&&!await VM.GenerateAsync(),"Invalid generation-area input blocks generation and export");
            AnalysisArea.OutputGainEditor.Text="0";Check(CommitConfiguration()&&VM.CanExport,"Corrected generation-area input restores export readiness");
            WetPercentEditor.Text="invalid";EditorInput.Update(WetPercentEditor);VM.PendingChanged();
            Check(!VM.CanExport&&!await VM.GenerateAsync(),"Invalid input blocks generation and export");
            WetPercentEditor.Text="17.25";EditorInput.Update(WetPercentEditor);VM.PendingChanged();
            Check(VM.CanExport,"Correcting uncommitted invalid text restores current result readiness");
            await AnalysisArea.RefreshAsync();builds=VM.AnalysisCache.BuildCount;
            using(var scope=new SourceWindowScope(this))
            {
                var source=scope.Window;var detail=(FrameworkElement)source.Content;await Layout(detail,1240,850);await source.AnalysisPanel.RefreshAsync();
                Check(All<Expander>(detail).All(e=>e.IsExpanded),"Detailed sections all open");
                Check(!source.IsVisible&&ReferenceEquals(source.VM,VM),"Detailed editor is a separate window sharing the current project");
                Check(VM.AnalysisCache.BuildCount==builds,"Detailed window reuses main FFT cache");
                Check(!All<Button>(detail).Any(b=>Equals(b.Content,"返回配置")),"No embedded return button");
                Check(All<Button>(detail).Any(b=>Equals(b.Content,"完成")),"Detailed window has footer completion");
                string templateBefore=ProjectIO.Serialize(VM.P.TemplateSources);
                VM.P.Sources[0].Left.Decay.ForEach(k=>k.Y*=1.1);VM.Commit();
                Check(ProjectIO.Serialize(VM.P.TemplateSources)==templateBefore,"Source edits do not rewrite template");
                Check(await VM.GenerateAsync(),"Detailed window generation includes source edits");
                await source.AnalysisPanel.RefreshAsync();await Layout(detail,1240,850);Capture(detail,"sources.png",1240,850);
                int samples=source.EnergyCurve.SampleBuildCount;
                for(int i=0;i<8;i++){source.EnergyCurve.InvalidateVisual();await Layout(detail,1240+i*4,850);}
                Check(source.EnergyCurve.SampleBuildCount==samples,"Curve repaint reuses interpolation samples");
                await Layout(detail,800,640);Check(source.SourceProperties.ViewportHeight>100,"Detailed properties scroll at small size");Check(source.SourceList.ActualHeight>85,"Source selection list stays accessible at small size");Capture(detail,"sources-small.png",800,640);
            }
            await Layout(client);
            var current=new TemplateDialog(VM.P);await Layout((FrameworkElement)current.Content,1040,780);
            Check(ProjectIO.Serialize(current.CreateSelection(false)!.Project)==ProjectIO.Serialize(VM.P),"Reopen template without edits preserves detailed edits");
            current.Editor.Session!.Reference.Decay.ForEach(k=>k.Y*=.8);var forward=current.CreateSelection(false)!;
            Check(ProjectIO.Serialize(forward.Project.TemplateSources)==ProjectIO.Serialize(forward.Project.Sources),"Changed template creates downstream sources");current.Close();
            var saved=Path.Combine(folder,"configuration.json");ProjectIO.Save(VM.P,saved);
            Check(ProjectIO.Serialize(ProjectIO.Load(saved))==ProjectIO.Serialize(VM.P),"Configuration JSON retains all edited parameters");
            await VM.ApplyTemplateAsync(new(Presets.Blank.Create(),false));
            var blank=new TemplateDialog(VM.P);await Layout((FrameworkElement)blank.Content,1040,780);
            blank.Model.TemplateCount=6;blank.Model.GenerateTemplate();Check(blank.CreateSelection(false)!.Project.DirectionCount==6,"Blank template supports direction generator");blank.Close();
            await VM.ApplyTemplateAsync(new(card.BuiltIn.Create(),true));await Layout(client);
            Check(VM.Result!=null&&VM.CanExport,"Confirm and generate");
            VM.P.Seed++;VM.Commit();var previous=VM.Result;var running=VM.GenerateAsync();
            Check(VM.Generating&&!VM.CanExport&&VM.ExportState.Contains("正在生成"),"Generation blocks exports with explicit state");VM.Cancel();Check(!await running&&ReferenceEquals(previous,VM.Result)&&VM.Stale,"Canceled generation retains stale result");
            Check(await VM.GenerateAsync(),"Regenerate after cancellation");
            var exported=await VM.ExportAsync();Check(exported!=null,"Export WAV");
            var wave=Exporter.ReadWave(Directory.GetFiles(exported!,"*_Matrix_PATHS_LL_RL_LR_RR.wav").Single());
            Check(wave.Channels.Zip(VM.Result!.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Exported samples equal actual result");
            var apo=await VM.ExportApoAsync(VM.Root);Check(File.ReadAllText(Directory.GetFiles(apo!,"*_APO.txt").Single()).Contains("Copy: L=LL+RL R=LR+RR"),"APO routing unchanged");
            var originalName=VM.P.Name;var sameResult=VM.Result;var kernelIdentity=VM.Result!.Kernels;
            AnalysisArea.NameEditor.Text="命名回归";Check(CommitConfiguration(),"Commit rename without regeneration");await Layout(client);
            Check(VM.CanExport&&ReferenceEquals(VM.Result,sameResult)&&ReferenceEquals(VM.Result!.Kernels,kernelIdentity)&&VM.Result.Project.Name=="命名回归","Rename updates export metadata without regenerating audio");
            Check(VM.LastExport==""&&VM.ExportLabel=="宽阔监听_命名回归"&&VM.SuggestedProjectFileName=="宽阔监听_命名回归.json","Rename clears previous path and updates export/configuration names");
            var renamedWave=await VM.ExportAsync();var renamedApo=await VM.ExportApoAsync(VM.Root);
            Check(Path.GetFileName(renamedWave!).StartsWith(VM.ExportLabel+"_")&&Path.GetFileName(renamedApo!).StartsWith(VM.ExportLabel+"_"),"WAV and APO directories use current configuration name");
            var renamedJson=Directory.GetFiles(renamedApo!,"*_project.json").Single();
            Check(ProjectIO.Load(renamedJson).Name=="命名回归"&&File.ReadAllText(Directory.GetFiles(renamedApo!,"*_APO.txt").Single()).Contains("宽阔监听_命名回归_L_to_LeftEar.wav"),"Exported JSON and APO references use current name");
            VM.Undo();await Layout(client);Check(VM.P.Name==originalName&&VM.CanExport&&VM.Result!.Project.Name==originalName,"Undo rename retains valid kernels and restores metadata");
            VM.Redo();await Layout(client);Check(VM.P.Name=="命名回归"&&VM.CanExport,"Redo rename retains valid kernels");
            await VM.ApplyTemplateAsync(new(Presets.BuiltIn.Single(p=>p.Name=="自由场").Create(),false));await Layout(client);
            Check(VM.LastExport==""&&VM.ExportLabel=="自由场"&&AnalysisArea.NameEditor.Text=="自由场"&&VM.Result==null&&!VM.CanExport,"Applying another template resets visible name and export path");
            Check(await VM.GenerateAsync(),"Generate switched free-field template");
            var switched=await VM.ExportAsync();Check(Path.GetFileName(switched!).StartsWith("自由场_"),"Switched template exports under its own name");
            VM.P.Direct.Angle=45;VM.P.Name="待更新";VM.Commit();Check(!VM.CanExport&&VM.Stale,"Rename never validates changed acoustic parameters");
            VM.Undo();await Layout(client);
            foreach(var mode in new[]{(1280d,820d,1d),(960d,760d,1d),(1280d,1040d,1.5d),(1600d,1400d,2d)})
            foreach(var page in new[]{PresetPage,ConfigurationPage,ExportPage})
            {
                Pages.SelectedItem=page;await Layout(client,mode.Item1,mode.Item2,mode.Item3);
                if(page==ConfigurationPage){Check(AnalysisArea.ResultPlot.ActualHeight>=135,"Plot readable at scale "+mode.Item3);Check(SettingsScroll.ViewportHeight>100,"Settings scroll at scale "+mode.Item3);
                    double plotBottom=AnalysisArea.ResultPlot.TransformToAncestor(AnalysisArea).Transform(new Point(0,AnalysisArea.ResultPlot.ActualHeight)).Y;
                    double hintTop=AnalysisArea.PlotInteractionHint.TransformToAncestor(AnalysisArea).Transform(new Point()).Y;
                    Check(plotBottom<=hintTop+.5,"Plot stays inside its row at scale "+mode.Item3);}
                Capture(client,$"layout-{page.Header}-{mode.Item3}-{mode.Item1}.png",mode.Item1,mode.Item2);
            }
            ShowConfiguration();VM.SetStatus("卷积核已生成。");await Layout(client);await AnalysisArea.RefreshAsync();Capture(client,"configuration.png");
            await VM.ApplyTemplateAsync(new(Presets.BuiltIn.Single(p=>p.Name=="悠长大厅").Create(),true));await AnalysisArea.RefreshAsync();
            Check(VM.Result!.Duration>3,"Performance regression uses a long hall IR");
            foreach(int kind in new[]{0,1,4})
            {
                AnalysisArea.PlotKind.SelectedIndex=kind;await AnalysisArea.RefreshAsync();int count=VM.AnalysisCache.BuildCount;
                for(int i=0;i<12;i++){VM.Commit();await AnalysisArea.RefreshAsync();await Layout(client,1230+i*3,820);}
                Check(VM.AnalysisCache.BuildCount==count,"Long IR resize/focus does not repeat analysis, plot "+kind);
                checks.Add(PlotRenderChecks.Measure(AnalysisArea.ResultPlot.Data!));
                await ResizePreviewChecks.Run(this,Check,checks,kind==1?Capture:null);
                Check(VM.AnalysisCache.BuildCount==count,"Native resize preview does not repeat analysis, plot "+kind);
            }
            Pages.SelectedItem=ExportPage;await Layout(client);
            var exportSections=All<Expander>(client).ToArray();
            Check(exportSections.Length==2&&exportSections.All(e=>e.IsExpanded),"Export sections all open");
            var section=exportSections[0];var header=(System.Windows.Controls.Primitives.ToggleButton)section.Template.FindName("HeaderToggle",section);
            var label=(TextBlock)header.Template.FindName("ActionLabel",header);
            Check(label.Text=="收起","Open section clearly says collapse");
            header.IsChecked=false;await Layout(client);
            Check(!section.IsExpanded&&label.Text=="展开"&&((Border)section.Template.FindName("Body",section)).Visibility==Visibility.Collapsed,"Header toggle collapses content and changes label");
            header.IsChecked=true;await Layout(client);
            Check(section.IsExpanded&&label.Text=="收起","Header toggle reopens content");
            Capture(client,"export.png");
            Check(!IsVisible,"No desktop window shown");
            File.Delete(Path.Combine(folder,"failure.txt"));
            File.WriteAllLines(Path.Combine(folder,"checks.txt"),checks.Append("PASS TOTAL "+checks.Count).Append("Offscreen layout and cache checks only. Live window-drag and physical DPI acceptance remain with the user."));return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(folder,"failure.txt"),string.Join("\n",checks)+"\n"+ex);return 1;}
    }
}
