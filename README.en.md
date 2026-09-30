# Statistical Field Studio

[Download Windows x64](https://github.com/kunimisena/StatisticalFieldStudio/releases/latest)

## Works well with Equalizer APO

## Author’s foreword

<!-- AUTHOR_FOREWORD_START -->
This project was made almost entirely by GPT, including all the source code, documentation and guides. My own role was to decide the architecture and listen to the results.

The initial inspiration came from EFOtech: <https://github.com/Joe0Bloggs/EFOtech_MLV>.

I have used EFOtech's four convolution kernels for years. As a heavy headphone user, I neither enjoy nor feel used to the original sound sitting inside my head.

Headphones have distinctive advantages as a playback system, but much stereo music is quite dry, and the left and right ears have no acoustic connection.

That is understandable. People may prefer music with a little ambience, but speaker playback systems introduce coloration of their own. To me, a speaker system is a problem of two excitation sources reaching two sampling points through coloration. A dry audio file also means that passing it through the speakers' coloration avoids duplicated coloration—a correct and convenient arrangement that also leaves an opportunity for headphone effects.

At first, I simply wanted an alternative to EFOtech with more flexible spatial effects, potentially avoiding some of the low-frequency room structure in its kernels.

Most spatial effects I could find either changed the EQ noticeably or pursued a particular real room, with recognizable room structure. I liked neither approach.

Initially, I tried something like an ellipsoidal space simulated with random reflection points. It sounded strange, and deciding how those points should respond at different frequencies was difficult. Doing acoustic ray tracing would clearly be too complicated.

After repeated experiments and adjustments, I abandoned random reflection points. But listening to the kernels from that ellipsoidal experiment helped me realize what I actually wanted.

There were only three things:

1. The effects of the head, shoulders, torso and pinnae, with head responses for speakers at ±30°.
2. Reverberation—a wetter sound—but **no room structure**.
3. A neutral, causal response that changes neither my headphone frequency response nor the overall loudness.

With these three aims and help from Astra and 5.6sol, this little convolution-kernel generator came into being.

For the first part, I used FABIAN's head responses. There is also a spherical-head model, but I was not happy with its sound. FABIAN brought excellent results, probably thanks to the pinna responses. I simply placed a small number of diffuse reflection sources around the head. That is fine: human ears provide only two sampling points; good luck resolving AoA ambiguity with that! (laughs)

For the second part, because I did not want room structure, I gave up on specific early-reflection points and their phase/delay relationships, as well as low-frequency standing waves. Instead, I combined Gaussian noise under constraints on total energy, spectral energy, frequency-dependent decay time and reflection buildup time to construct the reverberation kernels.

For the third part, taking causality and latency into account, I used smoothing plus minimum-phase EQ to flatten the kernel response, followed by energy normalization.

This way, switching the effect on or off does not change the playback system's frequency response or overall loudness. Of course, with only two channels, calibrating the left and right channels separately can leave the center response uneven. I therefore added an adjustable, separate center-channel calibration. Calibrating the center often makes the left and right channels uneven instead, and I do not like the sound of that center calibration. This is a limitation of two-channel recordings.

The software therefore does the following:

1. Defines reverberant reflection sources through the reverberation settings.
2. Places those sources in custom directions around FABIAN's head.
3. Combines direct sound and reflections into four kernels: left input to left ear, left input to right ear, right input to left ear and right input to right ear.
4. Applies two stages of minimum-phase EQ, with center calibration disabled by default, to minimize coloration beyond the intended reverberation and head response.
5. Exports the final kernels and an Equalizer APO configuration.

Enjoy the concert hall!

Finally, please do not take everything the AI wrote in the documentation at face value—especially the “Feedback that helps” section (laughs). I really cannot keep everything under control.
I asked the AI to check the project's open-source licensing. If there is any infringement, please let me know.
<!-- AUTHOR_FOREWORD_END -->


**Design a headphone sound field. Export the convolution. Keep your own headphone EQ.**

[简体中文](README.md) · [User guide](docs/en/guide.md) · [Design philosophy](docs/en/design.md) · [Build & repository](docs/en/development.md)

An offline Windows editor for stereo-to-binaural convolution kernels. Shape a field of virtual reflection directions, give each direction an energy spectrum and decay envelope, and render the direct speakers and reflections through a head model.

The goal is enjoyable, spacious headphone stereo with flexible tuning. The editor combines authored statistical reverberation, measured FABIAN head responses and inspectable EQ, exporting ordinary WAV convolution files.

![Preset editing and analysis](docs/images/editor.png)

## What you can do

- Start with ten authored fields or a blank template. Wide monitor is selected on startup. Save and load configurations as JSON files.
- Adjust a whole field or individual directions: spectral energy, frequency-dependent decay, onset, density buildup and the full energy envelope.
- Compare FABIAN head/pinna/torso responses with a simple spherical head. Parameter symmetry and random-realization symmetry are separate choices.
- Inspect actual kernels: magnitude, impulse response, energy decay, phase and group delay. Toggle individual plotted curves.
- Set per-ear and common second-stage minimum-phase EQ strengths independently. The second stage defaults to **0%**.
- Export four float32 WAV kernels, a path bundle, or a ready-to-include Equalizer APO configuration at 44.1, 48 or 96 kHz.
- Optionally render a song through the matrix and apply loudness normalization and/or limiting. This feature uses an external FFmpeg installation.

## Start listening

1. Open `StatisticalFieldStudio.exe` from the complete Windows x64 portable folder. The .NET runtime is included. **The UI is Chinese**; the English guide maps the controls.
2. Click a template, set its initial parameters, then choose **确认** (Confirm) or **确认并生成卷积核** (Confirm and generate). Edit overall curves in the template dialog, then head and EQ controls in Configuration. Generate kernels beside the plot; import and export JSON configurations with the buttons to its right. Edit individual sources in a separate window.
3. Select **导出与后处理** (Export & processing) for WAV, Equalizer APO or optional song rendering.
4. Choose **导出配置…** (Export configuration) to save your settings. Changes require regeneration. Project, WAV, APO and song outputs carry the template name, with readable counters for collisions.

For kernels and APO export, FFmpeg is not required. For song rendering, choose **选择 ffmpeg.exe…** in Export & processing → Song processing. Select a build with libsoxr support and `ffprobe.exe` in the same directory. The application remembers the path; **检查可用性** checks the tools. Automatic discovery also supports `tools/ffmpeg/` beside the EXE and `PATH`. The clean distribution does not include FFmpeg. See the [guide](docs/en/guide.md#song-rendering).

![Exported four-path convolution in Equalizer APO](docs/images/equalizer-apo.png)

## What this is designed to do

Listening comes first; parameters remain inspectable. Smooth spectral and decay trends describe the broad character; causal random kernels supply fine temporal and spectral detail. Both direct sound and reverberation pass through the head model. The final result is a fixed, reproducible 2×2 linear system.

Wide monitor is the author’s recommended everyday starting point. Control room offers a dry, short reflected field; Free field contains head-filtered direct speakers. Templates also cover surround and hall-like fields. Tune overall curves, refine individual directions, and save the parameters to reproduce the result. [Read the design and signal path](docs/en/design.md).

## Repository

| Location | Contents |
|---|---|
| `App/` | .NET 8 WPF interface, view models and UI checks |
| `Core/` | Statistical synthesis, head filtering, EQ, analysis and export |
| `Core/Data/` | Embedded FABIAN table, attribution, hashes and authored preset data |
| `Tests/` | Console validation suites |
| `docs/` | Matched Chinese/English guides and local HTML help |
| `tools/` | Optional dataset conversion and packaging |
| `publish/` | Local portable application and release bundles; ignored by Git |

Build on Windows with a .NET 8 SDK: `./build.ps1`. No Python, SOFA download or FFmpeg is needed for the standard build and core checks. [Build details](docs/en/development.md).

## Feedback that helps

Electroacoustics, DSP and headphone enthusiasts are welcome to compare settings and question the model. A useful report includes the project JSON, head model, EQ strengths, wet energy percentage, sample rate and what changed in a level-matched comparison. Avoid uploading copyrighted songs or someone else's impulse responses. See [contributing](CONTRIBUTING.md).

## Data, tools and research

FABIAN supplies the measured head responses (CC BY 4.0). Brown–Duda supplies the spherical-head model; air absorption follows ISO 9613-1. Statistical reverberation draws on published natural-reverberation research. Optional song processing uses FFmpeg for convolution, media conversion, loudness measurement and limiting, with libsoxr for resampling. The portable application includes the .NET/WPF runtime. [Sources, modifications and licenses](THIRD_PARTY_NOTICES.md).

## Status and attribution

Application version: **4.9.3**. [Build and validation record](docs/VALIDATION.md).

Original code and documentation use the [MIT License](LICENSE): use, modify and redistribute them while retaining the copyright and license notice.

FABIAN is separately attributed under CC BY 4.0. The source and distribution materials exclude personal projects and experimental presets. [Third-party notices and research](THIRD_PARTY_NOTICES.md).
