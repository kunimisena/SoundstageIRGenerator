using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoundstageIR.Core;
namespace SoundstageIRGenerator;

internal static class LanguageChecks
{
    public static async Task<int> Run(string folder)
    {
        Directory.CreateDirectory(folder);var checks=new List<string>();MainWindow? window=null;
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);}
        IEnumerable<T> All<T>(DependencyObject item) where T:DependencyObject
        {if(item is T t)yield return t;for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(item,i)))yield return child;}
        async Task Layout(FrameworkElement view,int width=1280,int height=820,double scale=1)
        {
            view.LayoutTransform=new ScaleTransform(scale,scale);view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
            await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
        }
        void Capture(FrameworkElement view,string name,int width=1280,int height=820)
        {
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle(Paint.Paper,null,new Rect(0,0,width,height));bitmap.Render(background);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));encoder.Save(file);
        }
        try
        {
            PlotRenderChecks.CheckMagnitudeRange();Check(true,"Magnitude axis expands, follows visible curves and resets without rebuilding geometry");
            foreach(var e in TextCatalog.Entries.Values)
            {
                if(string.IsNullOrWhiteSpace(e.En)||Regex.IsMatch(e.En,"[\\u4e00-\\u9fff]"))throw new Exception("Incomplete English resource: "+e.Zh);
                var indices=Regex.Matches(e.Zh,@"\{(\d+)").Select(m=>int.Parse(m.Groups[1].Value)).ToArray();
                if(indices.Length>0){var args=Enumerable.Repeat<object>(1.25,indices.Max()+1).ToArray();_=string.Format(CultureInfo.InvariantCulture,e.Zh,args);_=string.Format(CultureInfo.InvariantCulture,e.En,args);}
            }
            Check(true,"All bilingual resources have English text and valid composite formats");
            Check(TextCatalog.ForSystem(CultureInfo.GetCultureInfo("zh-TW"))=="zh-CN"&&TextCatalog.ForSystem(CultureInfo.GetCultureInfo("de-DE"))=="en","System language mapping");
            var savedUiCulture=CultureInfo.CurrentUICulture;
            string automaticRoot=Path.Combine(folder,"automatic-language"),automaticFile=Path.Combine(automaticRoot,"settings","language.json");
            if(File.Exists(automaticFile))File.Delete(automaticFile);
            try
            {
                foreach(string culture in new[]{"zh-CN","en-US"})
                {
                    CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);UiLanguage.Initialize(automaticRoot);
                    var modeDialog=new SpeakerModeDialog();
                    Check(TextCatalog.Language==TextCatalog.ForSystem(CultureInfo.CurrentUICulture),"Fresh speaker dialog follows system UI language: "+culture);
                    Check(!File.Exists(automaticFile),"Opening mode dialog does not create a manual language preference: "+culture);modeDialog.Close();
                }
                UiLanguage.Select("zh-CN");UiLanguage.Initialize(automaticRoot);
                Check(TextCatalog.Language=="zh-CN","Explicit language preference overrides English system UI language");
            }
            finally{CultureInfo.CurrentUICulture=savedUiCulture;UiLanguage.Initialize(App.TestRoot!);}
            UiLanguage.Select("en");window=new MainWindow();var vm=window.VM;var view=(FrameworkElement)window.Content;var pages=(TabControl)window.FindName("Pages");
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            string original=ProjectIO.Serialize(vm.P);var originalCulture=CultureInfo.CurrentCulture.Name;var selected=vm.Selected;
            vm.AudioLevelModeIndex=1;
            foreach(string language in new[]{"en","zh-CN","en"})
            {
                vm.LanguageIndex=language=="en"?1:0;await Layout(view);
                Check(ProjectIO.Serialize(vm.P)==original,"Language leaves project JSON unchanged: "+language);
                Check(ReferenceEquals(selected,vm.Selected)&&vm.AudioLevelModeIndex==1,"Language retains source selection and audio mode: "+language);
                Check(CultureInfo.CurrentCulture.Name==originalCulture,"Language does not change numeric culture: "+language);
                Check(((TabItem)pages.Items[0]).Header?.ToString()==(language=="en"?"Templates":"模板选择"),"Navigation updates immediately: "+language);
                Check(vm.PresetCards.Any(c=>c.Name==(language=="en"?"Wide Monitor":"宽阔监听")),"Built-in card names update: "+language);
                Check(!window.IsVisible,"No desktop window was shown");
                Check(((ComboBox)window.FindName("HeadModelSelector")).SelectedIndex==(int)vm.P.HeadModel,"Head-model selection remains visible: "+language);
            }
            UiLanguage.Initialize(App.TestRoot!);Check(TextCatalog.English,"Language preference survives reload");
            Check(TextCatalog.Diagnostic("生成 用户自定义源 · 1/4")=="Generating 用户自定义源 · 1/4","Diagnostic preserves user source name");
            Check(!Regex.IsMatch(TextCatalog.Diagnostic(HeadRenderer.Description(vm.P)),"[\\u4e00-\\u9fff]"),"Head-model description translates completely");
            Check(TextCatalog.Diagnostic("一级左耳 EQ：校正后平滑响应相对目标的 RMS 偏差 0.25 dB。").StartsWith("Stage 1 · Left-ear EQ:"),"Nested EQ diagnostics translate");
            Check(!Regex.IsMatch(TextCatalog.Diagnostic("能量曲线 横坐标须在 20 至 20000 之间。"),"[\\u4e00-\\u9fff]"),"Compound curve validation labels translate");
            foreach(var preset in Presets.All)
            {
                var card=vm.PresetCards.Single(c=>c.BuiltIn==preset);var dialog=new TemplateDialog(card);
                await Layout((FrameworkElement)dialog.Content,1040,780);
                var choice=dialog.CreateSelection(false);
                Check(choice!=null&&ProjectIO.Serialize(choice.Project)==ProjectIO.Serialize(PresetPresentation.Create(preset)),"English template confirmation retains all acoustic parameters: "+preset.Name);
                if(preset.Name=="宽阔监听")Capture((FrameworkElement)dialog.Content,"template-en",1040,780);
                dialog.Close();
            }
            for(int page=0;page<3;page++)
            {
                pages.SelectedIndex=page;
                foreach(double scale in new[]{1d,1.5,2d})
                {
                    int width=Math.Max(1280,(int)(800*scale)),height=Math.Max(820,(int)(640*scale));
                    await Layout(view,width,height,scale);
                    Check(view.IsMeasureValid&&view.IsArrangeValid,"English page layout "+page+" at "+scale);
                    if(scale==1)Capture(view,"page-en-"+page);
                    if(scale==2&&page==1)Capture(view,"configuration-en-200percent",width,height);
                }
            }
            pages.SelectedIndex=1;await Layout(view,800,640);Capture(view,"configuration-en-small",800,640);
            // Source editor is constructed and rendered offscreen, never shown.
            using(var scope=new MainWindow.SourceWindowScope(window))
            {await Layout((FrameworkElement)scope.Window.Content,1200,820);Capture((FrameworkElement)scope.Window.Content,"sources-en",1200,820);Check(!scope.Window.IsVisible,"Detailed editor remains offscreen");}
            // Decimal editing in template fields retains the literal decimal point until commit.
            var decimalDialog=new TemplateDialog(vm.PresetCards.Single(c=>c.BuiltIn.Name=="宽阔监听"));
            decimalDialog.Editor.FirstPathEditor.Text="0.";Check(decimalDialog.Editor.FirstPathEditor.Text=="0.","Decimal point remains editable");
            decimalDialog.Editor.FirstPathEditor.Text="0.385";var decimalChoice=decimalDialog.CreateSelection(false);Check(decimalChoice!=null,"Decimal parameter commits in English");decimalDialog.Close();
            vm.P.Name="用户的 Name α";vm.Commit();string named=ProjectIO.Serialize(vm.P);
            bool generatedOk=await vm.GenerateAsync();var result=vm.Result;Check(generatedOk&&result!=null,"English generation succeeds");string generated=ProjectIO.Serialize(vm.P);
            var panel=(ResultPanel)window.FindName("AnalysisArea");await panel.RefreshAsync();int builds=vm.AnalysisCache.BuildCount;
            var plot=(PlotView)panel.FindName("ResultPlot");
            await Layout(view);plot.ToggleLine(plot.Data!.Lines[0].Name);plot.Zoom(.7);bool zoomed=plot.IsZoomed;
            Capture(view,"generated-en");
            Check(!Regex.IsMatch(vm.Metrics,"[\\u4e00-\\u9fff]"),"Generated English metrics contain no untranslated labels");
            vm.LanguageIndex=0;await panel.RefreshAsync();
            Check(ReferenceEquals(result,vm.Result)&&!vm.Stale&&ProjectIO.Serialize(vm.P)==generated,"Switch after generation retains exact result and clean state");
            Check(vm.AnalysisCache.BuildCount==builds,"Relabeling plots does not repeat FFT analysis");
            Check(plot.IsZoomed==zoomed&&!plot.IsLineVisible(plot.Data!.Lines[0].Name),"Language retains plot zoom and hidden curves");
            var labeled=plot.Data;await panel.RefreshAsync();Check(ReferenceEquals(labeled,plot.Data),"Repeated refresh reuses relabeled plot geometry");
            await Layout(view);Capture(view,"generated-zh");
            Check(vm.P.Name=="用户的 Name α","User name is never translated");
            var chinese=await Task.Run(()=>Generator.Generate(ProjectIO.Clone(vm.P)));
            Check(result!.Kernels.Zip(chinese.Kernels).All(p=>p.First.SequenceEqual(p.Second)),"Chinese and English generation is sample-identical on all four paths");
            TextCatalog.SetLanguage("en");string exported=Exporter.Export(result,Path.Combine(folder,"exports"));
            foreach(var route in Generator.RouteNames)
            {
                string file=Directory.GetFiles(exported,"*"+route+".wav").Single();var wav=Exporter.ReadWave(file);int index=Array.IndexOf(Generator.RouteNames,route);
                Check(wav.Channels[0].SequenceEqual(result.Kernels[index].Select(v=>(double)(float)v)),"English WAV export matches preview: "+route);
            }
            var notes=File.ReadAllText(Directory.GetFiles(exported,"*_README.txt").Single());
            Check(notes.Contains("Headphone spatial-audio kernels")&&notes.Contains("Head model: FABIAN"),"English export instructions");
            var reloaded=ProjectIO.Load(Directory.GetFiles(exported,"*project.json").Single());Check(ProjectIO.Serialize(reloaded)==generated,"Cross-language JSON round trip");
            Check(ProjectIO.Serialize(ProjectIO.Load(Directory.GetFiles(exported,"*project.json").Single()))==generated,"Saved names remain unchanged across languages");
            Check(File.ReadAllText(Directory.GetFiles(exported,"*_Equalizer_APO*.txt").Single()).Contains("Copy: L=LL+RL R=LR+RR"),"APO routing remains intact");
            File.WriteAllLines(Path.Combine(folder,"language-results.txt"),checks.Append($"PASS {checks.Count} assertions"));window.Close();return 0;
        }
        catch(Exception ex){File.WriteAllLines(Path.Combine(folder,"language-results.txt"),checks.Append("FAIL "+ex));window?.Close();return 1;}
    }
}
