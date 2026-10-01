# Headphone spatial audio and HRTF convolution guide

[简体中文](../zh-CN/guide.md) · [Home](../../README.en.md)

Soundstage IR Generator creates four-path binaural impulse responses (IRs) for Equalizer APO or matrix convolution. Head-related transfer functions (HRTFs) describe directional responses at both ears; reverb controls shape ambience, spectrum and decay.

## Three pages

| Page | Purpose |
|---|---|
| 模板选择 — Templates | Ten authored fields, a blank template and a parameter dialog |
| 配置 — Configuration | Direct sound, head model, reflection curves, randomness, EQ and result plots |
| 导出与后处理 — Export & processing | WAV and Equalizer APO export, plus song rendering |

Click a template card to set its starting parameters. **确认** (Confirm) loads the configuration. **确认并生成卷积核** (Confirm and generate) also starts computation. Cancel retains the current project. The template page also provides **导入配置…** (Import configuration).

The template dialog contains the overall spectrum, decay-time and envelope editors, plus the direction generator. Return to Templates and choose **调整当前模板…** (Edit current template) to revisit this stage. Cancel keeps the current project; confirmation loads the changes.

Configuration places direct sound, head model, wet energy, random settings and EQ beside the result plot, or above it in a narrow window. Drag the divider to adjust their proportions; the narrow layout uses a horizontal divider and switching back retains the chosen column ratio. **反射源详细配置…** opens a separate window for direction, group, multi-selection and per-source L/R excitation editing. **完成** (Done) closes that window while retaining its edits in the project.

The plot toolbar places **导入配置…** (Import configuration) and **导出配置…** (Export configuration) to the right of **生成卷积核** (Generate kernels). Configuration JSON files store parameters and random seeds; exporting a configuration does not require generated kernels. Configuration name, kernel sample rate and output gain are edited in the generation area, alongside Undo and Redo. Parameter changes mark the displayed result as requiring an update.

Changing overall template curves or distribution generates downstream sources. Per-source details remain separate. Reopening the template without changing these overall parameters preserves detailed edits. Undo in Configuration can restore the state before confirming a changed template.

Export has its own Generate kernels button. It explains whether kernels are missing, outdated, being generated or ready to export. WAV, APO and song rendering use the same current result. Song rendering is an expandable section on this page. All expandable sections start open; click the arrow header or its explicit expand/collapse label to toggle them.

Analysis is cached per generated result. While dragging a window edge, a scaled preview follows the new size. Releasing the edge reflows the page and restores sharp plots; the wide/narrow layout transition also waits until release. This applies to the main window, template dialog and source editor. Legend, zoom and pan interactions reuse curve geometry and text drawings.

Export paths, result information and processing status support text selection and Ctrl+C. Copy path copies the WAV or APO export directory; Copy result information copies the complete analysis text.

Export displays the current output name. Applying a template updates the configuration name and clears the previous export path. Renaming updates WAV, APO and JSON filenames; a name-only edit can be exported immediately with unchanged kernel samples.

## Tune these first

1. **Head model:** built-in templates use FABIAN with common-response removal enabled. The spherical model is an alternative that simulates head shadow and arrival-time differences. Configuration files store the selected model and its parameters.
2. **Wet energy percentage:** adjusts the overall dry/reverberant balance more directly than changing every direction's gain.
3. **Overall decay-time multiplier:** changes tail length; envelope energy is recalibrated, so a longer tail alone does not increase the target integrated energy.
4. **Spectral contour:** broad changes color the reflections. This is distinct from final-output EQ.
5. **Second EQ strength:** starts at 0%. Increase it if you want more correction toward the coherent reference, then compare actual music.

Keep the same seed and change one thing at a time. Match listening level when comparing.

## Built-in template reference

These are template reference values; directions retain different spectral, decay and weight settings. RT is the 1 kHz tail reference, not an identical measured decay at every direction. The template dialog provides overall adjustments.

| Template / UI name | Directions | Layout | RT ref. s | First excess m | Mixing m | Wet energy % |
|---|---:|---|---:|---:|---:|---:|
| Compact monitor / 紧凑监听 | 6 | Ring | 0.20 | 0.35 | 0.8 | 8 |
| Wide monitor / 宽阔监听 | 8 | Ring | 0.56–0.78 | 0.28–0.34 | 0.88–1.44 | 8 |
| Front space / 前向空间 | 6 | Front | 0.24 | 0.30 | 0.7 | 9 |
| Warm surround / 温暖环绕 | 12 | Ring | 0.55 | 0.55 | 2.5 | 28 |
| Bright short hall / 明亮短厅 | 8 | Sphere | 0.48 | 0.60 | 3.0 | 32 |
| Overhead surround / 上方包围 | 12 | Hemisphere | 0.62 | 0.65 | 3.5 | 24 |
| Control room / 控制室 | 8 | Sphere | 0.22 | 0.65 | 1.0 | 5 |
| Free field / 自由场 | 0 | Direct only | — | — | — | 0 |
| Gentle concert hall / 柔和音乐厅 | 12 | Sphere | 1.35 | 0.75 | 6.0 | 65 |
| Blank / 空白模板 | 0 | No reflections | — | — | — | Actual 0 until sources are added |
| Long hall / 悠长大厅 | 12 | Sphere | 2.30 | 0.85 | 9.0 | 80 |

Built-in templates use FABIAN with common-response removal enabled, strict random mirroring disabled, first-stage EQ at 100% and second-stage EQ at 0%. Each field can be tuned further and saved using Export configuration.

Wide monitor is the author’s listening recommendation. Its table lists ranges across individual directions; it uses 5 m of direct-sound air absorption and 1/12-octave first-stage smoothing. Other templates use 1/12; second-stage strength is 0% throughout. Built-in source defaults are rounded to three decimal places in the actual parameter data. User-entered precision is retained in editing, JSON and synthesis. Random source identities are preserved.

Control room uses a 5% reflected-energy share, a restrained early section and a lighter short tail. Its spectral energy falls gradually above the midrange. Free field uses the same head and output EQ controls, with zero reflection sources and 0% wet energy.

## Relative weights and wet percentage

A source's 0 dB uses a common reference across all directions. Setting every reflection to −6 dB is equivalent to setting every reflection to 0 dB at the same wet percentage. Relative differences determine directional weighting.

The percentage is `Ewet / (Edry + Ewet)`, targeting the result after head filtering, final EQ and band limiting, using independent equal-power inputs with flat PSD over 20 Hz–20 kHz, summed across both ears. Dry/wet interference terms are excluded from this component ratio. The perceived balance also depends on input-channel correlation. 25% means wet/dry energy = 1/3.

The generator retains the direct and reflected spectra, predicts the EQ-induced shift, and solves for one broadband wet gain before final synthesis. Every reflection direction, input and frequency shares this scalar, preserving relative source weights, spectral shapes, phases and envelopes. Both EQ stages are designed from the compensated mixture. Results report the target, pre-EQ and final measured fractions, followed by the original common output calibration. Any remaining fraction error from finite support or incomplete convergence is reported.

Disabling direct sound displays a locked 100% wet balance. Re-enabling it restores the requested percentage. The template dialog shows the same state, and configuration files retain both the switch and the requested balance.

## The three curves

- **Energy spectrum:** the broad integrated-energy contour of a direction kernel, with shape-preserving interpolation on a log-frequency axis.
- **Decay time scale:** a frequency-dependent decay time scale. With a custom non-exponential envelope, this is a tail reference scale, not necessarily the RT60 obtained by an arbitrary regression method.
- **Full envelope:** local mean energy against excess accumulated propagation distance. The energy-decay analysis separately shows the backward-integrated EDC. The buildup segment can rise, remain level or fall; increasing reflection density need not increase energy.

First-reflection excess distance divided by 343 gives its onset relative to direct sound. Mixing distance controls the buildup segment and density scale. Air-absorption distance separately colors direct sound; it does not position a speaker or impose inverse-distance gain or a bulk propagation wait. Display zero is the reference ipsilateral direct peak; exported files retain a common lead-in and interaural timing.

Editing ranges include 0.005–30 s decay, 0–3430 m excess/mixing distance and 0–10000 m air-absorption distance. Extreme values are clamped. Longer kernels take more memory and time.



All four final paths share one scalar calibration to the selected output reference level, preserving relative levels and timing. Results and export notes report frequency gain and conservative transient peak bounds to help set playback headroom. These measurements are informational.

With stage two bypassed, output calibration and residuals use the individual ear responses. When stage two is enabled, they use its complex ear-average reference. Residuals are measured against the selected output reference level. Hover over the generation-notes line beside the plot, or read the complete notes in Results information on the export page.

## Symmetry, direction and randomness

Edit left-half parameters; the other half always mirrors them and swaps L/R inputs. Median-plane directions are not duplicated. 强制随机镜像 (Force random mirror) defaults off: corresponding statistics match, but realizations may differ. Enabling it enforces sample-level mirror symmetry and uses a single shared first EQ.

L/R excitations at one direction are independent by default. Auto-R inherits L timing and changes its spectral energy through relative gain and tilt; manual R editing is available. Different directions do not simply reuse the same noise. The same direction/input kernel feeds both ear filters.

Ordinary edits do not redraw unrelated sources. Copying a source or pasting parameters assigns independent random detail; stable IDs and the seed are saved. Layouts include a horizontal ring, sphere, hemisphere, front sector and mirrored pairs, up to 32 directions.

## EQ and analysis

Stage 1 corrects each ear's smoothed coherent-input response; defaults are 100% and 1/12 octave. Stage 2 designs a common EQ from the complex average of both corrected ear responses; defaults are 0% and 1/3 octave. Strength scales correction in dB, with 0% bypassing the stage. Both stages compute the full gain required by the smoothed target. Double-precision smoothing includes the bandpass transition regions. Both EQ stages and the bandpass target are combined in the frequency domain. The raw scene determines the correction time budget, with one second of bandpass support for short scenes. Generation notes report strong correction, smoothed residuals and finite-length synthesis error.

The second stage uses the complex mean of the two coherent-input ear responses as its reference. Its common correction applies to the entire output matrix.

Plot subjects include final paths, direct sound, selected reflections, pre-head excitation, EQ filters and coherent-input responses. Plot types include magnitude, IR, EDC, phase and group delay. Use the wheel to zoom and drag to pan; double-click the plot or use 复位视图 (Reset view) to restore the axes. Click legends to toggle curves, double/right-click to isolate, and use 显示全部曲线 (Show all curves) to restore them. Display smoothing affects the graph only.

## Export and routing

Folders use `template_date_time`; renamed projects also include the project name. WAVs, the path bundle, JSON and configuration files share that prefix. Collisions use readable `_02` counters without overwriting.

The table lists filename suffixes, always meaning input → ear:

| File | Path |
|---|---|
| `L_to_LeftEar.wav` | L input → left ear |
| `R_to_LeftEar.wav` | R input → left ear |
| `L_to_RightEar.wav` | L input → right ear |
| `R_to_RightEar.wav` | R input → right ear |

`Matrix_PATHS_LL_RL_LR_RR.wav` packs these paths in that order; for convolution engines with matrix routing.

APO export writes absolute paths in `template_APO.txt`. Ordinary WAV export includes the matching APO configuration as well. Include it in your own configuration; the final assignment is `L=LL+RL R=LR+RR`, without an extra unprocessed dry copy. Re-export if files move. Match device and kernel sample rates. This program never changes the system configuration.

Kernels use a shared gain calibration. Export notes report reference gain, peak bounds and common lead-in; allow playback headroom. The final steep bandpass keeps 20 Hz and 20 kHz inside the passband rather than making them −3 dB edges.

## Song rendering

In **导出与后处理 → 处理歌曲** (Export & processing → Song processing), click **选择 ffmpeg.exe…** and select a complete FFmpeg build with libsoxr support and `ffprobe.exe` in the same directory. Selection checks the tools automatically; **检查可用性** checks again. Supported input includes WAV, FLAC, MP3 and M4A; export WAV, FLAC or M4A with the full convolution tail.

The path is stored separately in `settings/audio-tools.json` under the application workspace, outside project JSON. Opening a project preserves local tool settings. **自动查找** clears the explicit selection and searches nearby `tools/ffmpeg/`, `%ProgramFiles%/ffmpeg/bin/` and PATH. An invalid explicit selection produces an error instead of silently switching builds.

Checks cover versions, convolution options, resampling, loudness and limiting, and run again before rendering. Obtain a build from [FFmpeg downloads](https://ffmpeg.org/download.html). Attribution and licenses are in the root `THIRD_PARTY_NOTICES.md`.

Mono/stereo inputs are resampled to the kernel rate; the tail is retained. Outputs are float32 WAV, 24-bit FLAC or AAC/M4A, without overwriting input.

- Default: measure convolved loudness, apply fixed gain toward −18 LUFS, then linked stereo limiting.
- Limit only: no LUFS gain correction; limiting and encoding safety adjustments may change loudness.
- Bypass: inspect unmastered processing.

−18 LUFS is the default target for sharing. The finished file is measured again; actual loudness and true peak are recorded in `render.json`.

## Portable data

Keep the complete application folder in a writable location. Configurations, kernels, APO configurations and processed songs live beside the EXE in `projects/`, `exports/`, `EqualizerAPO/` and `processed-audio/`. Clean distribution archives exclude personal data. Do not upload an entire used application folder as a public release.

Magnitude plots default to 20 Hz–20 kHz. Enable “显示带通范围” (Show bandpass range) to inspect the transition and stopbands from 5 Hz down to −100 dB. Disable it to restore the normal range. This changes visualization only.
