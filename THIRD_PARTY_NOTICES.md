# Third-party notices / 第三方说明

## FABIAN

The embedded dataset is a float32 conversion of `FABIAN_HRIR_measured_HATO_0.sofa` and `FABIAN_CTF_measured_inverted_smoothed.sofa` (2020-01-22). Copyright/authors: Fabian Brinkmann, Alexander Lindau, Stefan Weinzierl, Gunnar Geissler, Steven van de Par, Markus Mueller-Trapet, Rob Opdam and Michael Vorlaender; TU Berlin / University of Oldenburg / RWTH Aachen.

License: [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).

- [Database and versioned provenance](https://doi.org/10.14279/depositonce-5718.4)
- [Original SOFA files and documentation](https://sofacoustics.org/data/database/tu-berlin/)
- [Brinkmann et al. 2017, JAES 65(10), 841–848](https://doi.org/10.17743/jaes.2017.0033)
- Full attribution and changes: `Core/Data/FABIAN-NOTICE.txt` (also copied beside the EXE).
- Source and converted checksums: `Core/Data/FABIAN-manifest.json`.

The table conversion preserves original timing and gains in float32. Rendering constructs both ears from measured left-ear data using mirrored source directions, with nearest-direction selection, optional inverse CTF, in-band magnitude preservation, out-of-band edge extension and finite causal reconstruction as detailed in the notice. The generator also applies a common noncoherent head-power calibration before its tonal EQ. These modifications are not endorsed by the dataset authors.

内嵌表来自上述原始文件，使用 CC BY 4.0，作者、数据来源、转换校验值与具体渲染修改均保留。人头数据是第三方成果；项目的统计声场、EQ 和预设不代表原作者的认可。

## .NET

The Windows self-contained build uses Microsoft's .NET 8 and WPF runtimes. Their own license and third-party notices apply separately. [dotnet/runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [runtime notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT), [WPF](https://github.com/dotnet/wpf).

Windows 自包含程序包含 .NET 8 与 WPF 运行时，按运行时自身的许可和第三方声明分开处理。

## FFmpeg and libsoxr (optional / 可选)

Song processing runs user-selected `ffmpeg.exe` and the adjacent `ffprobe.exe` as external processes. FFmpeg performs media inspection, decoding/encoding, four-path convolution (`afir`), loudness measurement (`loudnorm`), gain and limiting (`volume`, `alimiter`). Its libsoxr resampler supplies sample-rate conversion and limiter oversampling. The application supplies the generated kernels, channel routing, gain decisions and output verification.

歌曲处理调用用户选择的 FFmpeg 和同目录的 ffprobe。媒体探测、解码与编码、四路径卷积、响度测量、增益与限幅由 FFmpeg 完成；采样率转换和限幅过采样使用其 libsoxr 重采样器。程序负责生成卷积核、组织路由、计算增益及复查输出。

The source repository and packages produced by `tools/package.ps1` exclude FFmpeg/libsoxr binaries. Select a complete FFmpeg build with libsoxr support in the song-processing panel. FFmpeg is LGPL-2.1-or-later by default; optional components can change the build's license to GPL. libsoxr uses LGPL-2.1-or-later. The selected build's notices and build configuration determine its redistribution requirements. Redistributors who add binaries must also supply the required license, copyright and corresponding-source materials for that exact build and its bundled dependencies.

源码仓库与 `tools/package.ps1` 生成的发布包不附带 FFmpeg/libsoxr 二进制。歌曲处理需要支持 libsoxr 的完整构建。FFmpeg 默认采用 LGPL-2.1-or-later，启用可选组件的构建可能采用 GPL；libsoxr 采用 LGPL-2.1-or-later。若自行将工具加入发布包，应按该构建及所含依赖的许可提供版权、许可与对应源码材料。

- [FFmpeg project and downloads](https://ffmpeg.org/download.html)
- [FFmpeg licensing and redistribution](https://ffmpeg.org/legal.html)
- [SoX Resampler library / libsoxr](https://sourceforge.net/projects/soxr/) · [license](https://sourceforge.net/p/soxr/code/ci/master/tree/LICENCE)

## Dataset conversion tools / 数据转换工具

`tools/convert_fabian.py` uses NumPy and h5py to read SOFA/HDF5 data and create the embedded table. These Python packages are development dependencies installed separately, not application runtime components. Their own distributions include their component licenses.

数据转换脚本使用 NumPy 和 h5py 读取 SOFA/HDF5 并构建内嵌表；它们是单独安装的开发依赖。标准构建直接使用已转换的数据表。

- [NumPy](https://numpy.org/about/) — modified BSD; see the installed distribution for component notices.
- [h5py](https://docs.h5py.org/en/stable/licenses.html) — BSD-3-Clause with its included component notices.
- Version ranges: `tools/requirements-fabian.txt`.

## Models and research / 模型与研究

- **Spherical head / 球形头:** C. P. Brown and R. O. Duda (1998), [A Structural Model for Binaural Sound Synthesis](https://doi.org/10.1109/89.709673). `Core/Generator.cs` implements the head-shadow filter and propagation-delay approximation. The paper's other anatomical components are separate from this spherical-head implementation. / 程序实现头部遮挡滤波与传播延时近似；此球形头实现对应论文的这两部分。
- **Air absorption / 空气吸收:** the frequency-dependent absorption calculation in `Core/Generator.cs` follows ISO 9613-1, with equations available in [ECMA-108, fifth edition, Annex A](https://www.ecma-international.org/wp-content/uploads/ECMA-108_5th_edition_december_2010.pdf). The application uses fixed standard environmental values and builds a minimum-phase attenuation filter. / 使用标准环境值计算频率相关空气损耗，并构造最小相位衰减滤波器。
- **Statistical reverberation / 统计混响:** [Traer & McDermott (2016), Statistics of natural reverberation enable perceptual separation of sound and space](https://mcdermottlab.mit.edu/papers/Traer_McDermott_2016_reverberation.pdf). This research informs the spectral and decay statistics; the project's directional synthesis, controls and presets are its own implementation and tuning. / 研究提供频谱与衰减统计的参考；方向核生成、编辑控制与模板调校由本项目实现。

These entries identify model and research sources. The papers and standards themselves are linked rather than redistributed. / 上述条目说明模型与研究来源，论文和标准通过链接提供。

## Equalizer APO interoperability / 配置导出

The exporter writes configuration text for [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) and accompanying WAV paths. Equalizer APO is installed separately. / 导出器生成 Equalizer APO 使用的文本配置和 WAV 路径；Equalizer APO 由用户单独安装。
