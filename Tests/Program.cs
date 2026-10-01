using SoundstageIR.Core;
using System.Numerics;
using System.Diagnostics;
int passed=0;void Check(bool c,string text){if(!c)throw new Exception("FAIL "+text);passed++;Console.WriteLine("PASS "+text);}
var target=args.FirstOrDefault()??Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../artifacts/tests"));Directory.CreateDirectory(target);
if(args.Contains("--head-bandwidth")){HeadReferenceChecks.BandwidthAudit(Check);File.WriteAllText(Path.Combine(target,"head-bandwidth-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--head-reference")){HeadReferenceChecks.Run(Check,target,args.FirstOrDefault(a=>a.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))??Environment.GetEnvironmentVariable("SIR_AUDITION_ORACLE"));File.WriteAllText(Path.Combine(target,"head-reference-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--audio-tools")){await AudioToolChecks.Run(Check,target,args.Skip(2).FirstOrDefault());File.WriteAllText(Path.Combine(target,"audio-tool-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--final-presets")){FinalPresetChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"final-preset-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--wet-balance")){WetBalanceChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"wet-balance-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--spectral")){SpectralChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"spectral-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--eq-accuracy")){EqAccuracyChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"eq-accuracy-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--energy-only")){EnergyChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"energy-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--release-only")){ReleaseChecks.Run(Check,target,args.FirstOrDefault(a=>a.EndsWith("wide-monitor-before.wav")));File.WriteAllText(Path.Combine(target,"release-results.txt"),$"PASS {passed} assertions");return;}
if(args.Contains("--eq-strength-only")){EqStrengthChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"eq-strength-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--shared-edit")){ReflectionEditChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"shared-edit-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--limit-only")){await LimitOnlyChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"limit-only-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--mastering-only")){await MasteringChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"mastering-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--audio-pipeline")){await AudioPipelineChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"audio-pipeline-results.txt"),$"PASS {passed} assertions\n");return;}
if(args.Contains("--preset-only")){PresetRevisionChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"preset-results.txt"),$"PASS {passed} assertions\\n");return;}
if(args.Contains("--head-only")){HeadModelChecks.Run(Check,target);File.WriteAllText(Path.Combine(target,"head-results.txt"),$"PASS {passed} assertions\n");return;}
WetBalanceChecks.Run(Check,target);
SpectralChecks.Run(Check,target);
FinalPresetChecks.Run(Check,target);
EqStrengthChecks.Run(Check,target);
EqAccuracyChecks.Run(Check,target);
PresetRevisionChecks.Run(Check,target);
HeadModelChecks.Run(Check,target);
var centers=NoiseKernel.Centers(48000);for(int j=0;j<300;j++){double f=20*Math.Pow(1200,j/299.0);double v=Enumerable.Range(0,centers.Length).Sum(b=>Math.Pow(NoiseKernel.Weight(centers,b,f),2));if(Math.Abs(v-1)>1e-10)throw new Exception("Partition");}Check(true,"Power-complementary bands");
foreach(var preset in Presets.BuiltIn){var p=preset.Create();p.Validate();Check(p.DirectionCount==preset.Directions,"Preset "+p.Name);}
await AudioPipelineChecks.Run(Check,target);
await MasteringChecks.Run(Check,target);
await LimitOnlyChecks.Run(Check,target);
ReflectionEditChecks.Run(Check,target);
EnergyChecks.Run(Check,target);
TimingChecks.Run(Check,target);
var project=Presets.BuiltIn[0].Create();project.StrictMirror=true;project.CenterEqStrengthPercent=100;var h=HeadModel.AtAngle(0,project);Check(Math.Abs((h.B0+h.B1)/(1+h.A1)-1)<1e-12,"Head DC unity");Check(h.Delay<0&&HeadModel.AtAngle(Math.PI,project).Delay>0,"Head delays");
foreach(double az in new[]{-150.0,-80,-10})foreach(double el in new[]{-70.0,0,80}){var l=HeadModel.At(az,el,0,project);var r=HeadModel.At(-az,el,1,project);Check(l==r,"Head mirror "+az+"/"+el);}
Check(HeadModel.AirDbPerMetre(10000)>HeadModel.AirDbPerMetre(1000),"Air HF absorption");
Check(Math.Abs(HeadModel.AirDbPerMetre(1000)-.00466)<.0001,"ISO air absorption numeric reference at 1kHz");
Check(HeadModel.AirDbPerMetre(8000)>.09&&HeadModel.AirDbPerMetre(8000)<.12,"ISO air absorption numeric reference at 8kHz");
var airProject=ProjectIO.Clone(project);airProject.Direct.AirAbsorption=true;airProject.Direct.AirAbsorptionDistance=20;var airFilter=HeadModel.Air(airProject);
Check(Math.Abs(20*Math.Log10(Dtft(airFilter,8000,48000).Magnitude)+HeadModel.AirDbPerMetre(8000)*20)<.05,"Air filter implements expected distance loss");
var exc=project.Sources[0].Left;var a=NoiseKernel.Generate(exc,48000,project.Seed,project.Sources[0].Id,0);var b=NoiseKernel.Generate(exc,48000,project.Seed,project.Sources[0].Id,0);Check(a.SequenceEqual(b),"Gaussian kernel reproducibility");
var other=NoiseKernel.Generate(exc,48000,project.Seed,project.Sources[0].Id,1);Check(!a.SequenceEqual(other),"Different input excitation streams");
Check(a.Take((int)(exc.OnsetMs*48)).All(v=>v==0),"Kernel onset");
var cs=new CancellationTokenSource();cs.Cancel();bool cancelled=false;try{Generator.Generate(project,null,cs.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancellation");
var watch=Stopwatch.StartNew();var result=Generator.Generate(project);Console.WriteLine($"Generate: {watch.Elapsed.TotalSeconds:F2}s, {result.Duration:F3}s kernel, EQ RMS {result.EqResidualDb:F2}dB");
Check(result.Kernels[0].SequenceEqual(result.Kernels[3])&&result.Kernels[1].SequenceEqual(result.Kernels[2]),"Exact final mirror");
Check(result.Kernels.All(x=>x.All(double.IsFinite)),"Finite kernels");Check(result.ZeroSample/48000<.002,"Common guard below 2ms");
var noeq=ProjectIO.Clone(project);noeq.Equalize=false;var raw=Generator.Generate(noeq);Check(SpectralTestReference.SameSources(result,raw),"EQ does not regenerate noise");
Complex Dtft(double[] x,double f,int sr){Complex z=0;for(int i=0;i<x.Length;i++)z+=x[i]*Complex.FromPolarCoordinates(1,-2*Math.PI*f*i/sr);return z;}
foreach(double f in new[]{93.0,753,7500})for(int c=0;c<4;c++)Check(SpectralTestReference.Route(result,c,f),"EQ route "+c+" @"+f);
var asym=ProjectIO.Clone(project);asym.StrictMirror=false;var ar=Generator.Generate(asym);Check(ar.SecondEq.Length>1,"Two EQ stages");Check(!ar.Kernels[0].SequenceEqual(ar.Kernels[3]),"Random asymmetry");
var expectedEq=SpectralTestReference.SecondEq(ar);Check(expectedEq.Zip(ar.SecondEq).Max(v=>Math.Abs(v.First-v.Second))<1e-10,"Second EQ uses complex ear sum after first stage");
var mono=new ReflectionPair{Azimuth=0,Left=exc.Clone()};var median=ProjectIO.Clone(noeq);median.Sources=[mono];var mr=Generator.Generate(median);Check(median.DirectionCount==1,"Median source single count");Check(mr.Contributions[0].LeftInputKernel.SequenceEqual(mr.Contributions[0].RightInputKernel),"Median excitation mirror");
var changed=ProjectIO.Clone(noeq);changed.Sources[0].Left.GainDb+=3;var rr=Generator.Generate(changed);Check(rr.Contributions[1].LeftInputKernel.SequenceEqual(raw.Contributions[1].LeftInputKernel),"Editing one source leaves others unchanged");
string folder=Exporter.Export(result,target);var wav=Exporter.ReadWave(TestFiles.Get(folder,"Matrix_PATHS_LL_RL_LR_RR.wav"));Check(wav.SampleRate==48000&&wav.Channels.Length==4,"WAV header");Check(wav.Channels.Zip(result.Kernels).All(p=>p.First.SequenceEqual(p.Second)),"All exported samples match preview");
ProjectIO.Save(project,Path.Combine(target,"roundtrip.json"));Check(ProjectIO.Serialize(ProjectIO.Load(Path.Combine(target,"roundtrip.json")))==ProjectIO.Serialize(project),"JSON roundtrip");
foreach(int sr in new[]{44100,96000}){var p=ProjectIO.Clone(noeq);p.SampleRate=sr;p.Sources=p.Sources.Take(1).ToList();var r=Generator.Generate(p);var dir=Exporter.Export(r,target);Check(Exporter.ReadWave(TestFiles.Get(dir,"L_to_LeftEar.wav")).SampleRate==sr,"Export sample rate "+sr);}
foreach(int rate in new[]{44100,48000,96000})
{
    var bp=Dsp.OutputBandpass(rate);var pass=new[]{20.0,21,30,100,1000,10000,19000,19999,20000}.Select(f=>20*Math.Log10(Dtft(bp,f,rate).Magnitude)).ToArray();
    File.WriteAllLines(Path.Combine(target,$"bandpass-{rate}.csv"),new[]{"FrequencyHz,ActualGainDb"}.Concat(new[]{5.0,10,15,18,20,30,1000,18000,20000,20500,Math.Min(22000,rate*.5*.995)}.Select(f=>$"{f:R},{20*Math.Log10(Dtft(bp,f,rate).Magnitude):R}")));
    Check(pass.All(db=>Math.Abs(db)<.1),"Bandpass flat INCLUDING 20Hz and 20kHz "+rate);
    Check(20*Math.Log10(Dtft(bp,10,rate).Magnitude)<-75,"Bandpass infrasonic stop "+rate);
    Check(20*Math.Log10(Dtft(bp,Math.Min(22000,rate*.5*.995),rate).Magnitude)<-75,"Bandpass ultrasonic stop "+rate);
    Check(Generator.FirstPeak(bp)<rate*.001,"Bandpass no long leading delay "+rate);
}
if(args.Contains("--statistics"))StatisticsChecks.Run(Check,target);
if(args.Contains("--long"))
{

    foreach(int sr in new[]{44100,96000}){var hall=Presets.BuiltIn[^1].Create();hall.SampleRate=sr;var hallResult=Generator.Generate(hall);var hallDir=Exporter.Export(hallResult,target);var hallWave=Exporter.ReadWave(TestFiles.Get(hallDir,"Matrix_PATHS_LL_RL_LR_RR.wav"));Check(hallWave.Channels.Zip(hallResult.Kernels).All(v=>v.First.SequenceEqual(v.Second)),"Longest preset full export "+sr);}
    var p=ProjectIO.Clone(noeq);p.Sources=p.Sources.Take(1).ToList();foreach(var k in p.Sources[0].Left.Decay)k.Y=10;var r10=Generator.Generate(p);Check(r10.Duration>13&&r10.ZeroSample==raw.ZeroSample,"10s RT and unchanged direct timing");
}
File.WriteAllText(Path.Combine(target,"results.txt"),$"PASS {passed} assertions\nGenerated {DateTime.Now:O}\n");Console.WriteLine($"PASS TOTAL {passed}");
