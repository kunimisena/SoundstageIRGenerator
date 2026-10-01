# Build and release

[简体中文](../zh-CN/development.md) · [Home](../../README.en.md)

## Prerequisites and build

Windows x64 with a .NET 8 SDK. `global.json` accepts the latest installed stable .NET 8 feature band. No paid IDE is required. FABIAN's converted table is included; Python is optional for re-conversion only.

```powershell
./build.ps1
```

This builds Release, runs core suites covering head bandwidth and calibration, wet balance, spectra, presets, EQ, editing and exports, and publishes a self-contained application to `publish/SoundstageIRGenerator/`. It does not copy personal projects into a distribution. It retains existing local runtime data when publishing over your own portable folder.

```powershell
# Full DSP, statistics, long-tail, audio mastering and offscreen WPF checks:
./build.ps1 -FullTests -HeadlessChecks
# Compile/publish only:
./build.ps1 -SkipTests
# A different fresh output location:
./build.ps1 -OutputDirectory ./publish/MyLocalBuild
# Optional local FFmpeg copy; directory may contain bin/ and license notices:
./build.ps1 -FFmpegDirectory 'C:\path\to\ffmpeg'
```

Full audio tests need FFmpeg/ffprobe available before tests (e.g. under Program Files or on PATH). Offscreen WPF checks run on Windows without showing a window. `-FFmpegDirectory` copies tools during publishing; audio tests resolve FFmpeg through PATH or Program Files. Tests write under `artifacts/`. Ordinary CI does not claim to run audio or desktop acceptance: it builds and runs the core suites.

## Public source vs local portable data

The repository root is the folder containing SoundstageIRGenerator.sln. Track App, Core, Tests, tools, docs, .github and root build/docs files. `Core/Data/FABIAN.bin` is required and intentionally tracked (about 24 MiB), with its notice and provenance manifest.

Do not track `bin`, `obj`, `publish`, `artifacts`, `projects`, `exports`, `EqualizerAPO`, `processed-audio` or backup folders. They are covered by `.gitignore`. Publishing a used application folder directly may expose personal paths, audio and settings.

```powershell
./tools/package.ps1
```

Packaging copies an explicit source allowlist into a fresh staging directory, compiles that copy, and produces source and Windows x64 ZIPs plus SHA256 sums in `publish/packages/`. It does not include FFmpeg or any personal runtime data. Runtime license notices come from the exact installed NuGet runtime packs used for publishing. Staging stays under ignored `artifacts/` for inspection. Existing versioned ZIPs are not overwritten; use `-Destination` for another run.

Original code and documentation are MIT-licensed. FABIAN retains its CC BY 4.0 attribution. The packaging script produces local files.

## Repository and releases

Source: [SoundstageIRGenerator](https://github.com/kunimisena/SoundstageIRGenerator). [Releases](https://github.com/kunimisena/SoundstageIRGenerator/releases) contain the Windows portable application, source archive and SHA-256 checksums.

CI builds and runs core checks on pushes and pull requests. For a release, run `./build.ps1 -HeadlessChecks` and `./tools/package.ps1`, set the stable version in `Directory.Build.props` and add `docs/releases/<version>.md`. A push to main runs the checks, then packages clean source, creates the version tag and publishes the Release. The Actions workflow also supports manual release runs. Maintain Chinese and English documentation together.

## Source map

- `SpeakerApp/`: the speaker entry point, reusing App themes, editors and plots. Both executables ship in one portable folder.
- `Core/Speakers/`: both speaker modes, playback matrix conversion, pose checks and export.
- `Core/Models.cs`, `Presets.cs`: project/parameter definitions and built-in templates.
- `Core/Generator.cs`: direction accumulation, head path timing, energy balance and final processing.
- `Core/HeadRenderer.cs`: measured head table, symmetry and resampling.
- `Core/Dsp.cs`: FFT/convolution, smoothing and minimum-phase processing.
- `Core/EnergyBalance.cs`: component energy reference and wet/dry normalization.
- `Core/AudioRenderer*.cs`: external FFmpeg processing and level management.
- `App/MainViewModel*.cs`: editing/generation/export state.
- `App/MainWindow.xaml`, `ReflectionBatchEditor*`, `VisualControls.cs`: workflow and plots.
- `Tests/`: reproducibility, routing, EQ, statistics, media and export checks.

Run `SoundstageIRGenerator.exe --headless-check <output-folder>` for offscreen controls, workflow checks and rendered layout images. For the speaker application, run `SoundstageSpeakers.exe --check <results.txt>`. Test projects and exports stay under the supplied folder. Manual keyboard, pointer and native monitor-DPI acceptance remains a separate check.

## Optional FABIAN table regeneration

Download the two filenames listed in `Core/Data/FABIAN-NOTICE.txt` from the official dataset. Then, in a separate Python environment:

```powershell
python -m pip install -r tools/requirements-fabian.txt
python tools/convert_fabian.py C:\path\to\sofa-files
```

The converter writes the embedded table and its manifest. Verify source hashes and preserve attribution. Regular build/runtime never requires Python. The original SOFA data and Python environment should not be committed.

## Project format and validation

Project JSON uses schema version 5. TemplateName identifies the template, TemplateSources stores template parameters, and Sources stores per-source edits. Configurations are exported and imported as JSON. Built-in templates default to first-stage EQ at 100% and second-stage EQ at 0%. The [validation record](../VALIDATION.md) identifies the tested version, scope and results.

Local FFmpeg preferences live in `settings/audio-tools.json`, excluded from Git and clean packages. Use `tools/package.ps1` for public packages; users select FFmpeg in the song-processing panel.

## Bilingual resources

`Core/Localization/strings.json` stores paired Chinese and English text, accessed through `Core/TextCatalog.cs`. WPF dynamic resources update labels, and `App/UiLanguage.cs` persists the language preference. Presentation language is separate from numeric calculations and project JSON. Preserve formatting placeholders and units when translating.

Run `SoundstageIRGenerator.exe --language-check <output-folder>` for targeted checks covering language switching, all templates, plot state, sample-identical generation and export readback. Checks run offscreen and write results to the supplied directory.
