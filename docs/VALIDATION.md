# 4.14.0 复杂音箱串联验证 / Complex speaker cascade validation

复杂空间音效以普通生成器的完整自由场结果 F 为播放参考，以完整目标声场 T 为目标，直接求解 F × C ≈ T。F 与 T 均保留原有的人头功率校正、平滑 EQ、带通及归一化。实际音箱决定 F，虚拟音箱和反射源决定 T。

Complex spatial audio solves F × C ≈ T using the ordinary generator's complete calibrated free-field response F and complete target field T. Both retain head-power correction, smooth EQ, bandpass and normalization. Actual speakers define F; virtual speakers and reflection sources define T.

复杂模式配置按实际音箱、虚拟音箱、人头设置排列；模板只确认进入配置。实际音箱空气吸收默认开启，播放路径采用远场方向响应。关闭空气吸收后，镜像等距音箱从 1.7 m 改为 4 m，中心校准核逐样本一致；开启空气吸收后，原始播放路径高频能量下降。两种状态均可保存／读取。

Complex configuration orders actual speakers, virtual speakers and head settings. Templates confirm into configuration without immediate synthesis. Actual-speaker air absorption defaults on, using a far-field directional model. With absorption off, mirrored distances of 1.7 m and 4 m produce sample-identical calibrated centre kernels; enabling absorption attenuates raw high-frequency energy. The switch persists in project files.

图表分成五组。求逆图逐点核对求解器记录的数据，并以单位矩阵的正则化解析解核对幅度、相位与群延迟。新增记录逻辑后，耳机、简单混响和复杂空间音效的输出与修改前发布程序逐样本一致。

Plots use five groups. The inverse plot is checked against recorded solver coefficients and the analytic regularized identity inverse. Headphone, simple-reverb and complex-spatial outputs remain sample-identical to the preceding published build after adding the diagnostic recorder.

## 本轮检查 / Current checks

| 检查 / Check | Assertions |
|---|---:|
| F × C 串联、风险诊断及导出回读 / Cascade, risk diagnostics and export round trip | 27 |
| 三种输出逐样本回归及求逆图解析对照 / Sample-exact regression for all three outputs and analytic inverse-plot checks | 5 |
| 音箱核心 / Speaker core | 139 |
| 音箱程序中英文离屏界面 / Speaker bilingual offscreen UI | 456 |
| 耳机程序语言回归 / Headphone language regression | 68 |

- 相同自由场转换得到单位通路；宽阔监听的目标四路径与普通耳机生成器一致。
- 重新读取导出的 F、C WAV 后计算串联，确认对应预览结果；APO 串联测试配置先处理 C，再处理 F。
- 风险检查覆盖退化摆位、狭窄音箱夹角、近场模型提示、实际矩阵增益、相消代价和还原误差。实际生成结果的检查用于结果提示；人为构造的风险结果用于核对图表区与导出页红色提示的绑定和布局。
- 位置参考固定采用中心、左右／前后各偏移 5 cm、左右转头 5° 共七个姿态。始终使用同一 C 和固定的中心校正，统计独立等功率 L/R 输入下每耳 1/3 倍频程平滑功率频响的 dB 标准差，再对频率与两耳取 RMS。没有位置范围设置项。
- 图表检查遍历 14 种对象和 5 类分析，实际触发选择事件和绘制，并连续快速切换 150 次。检查中英文、800／1320／1920 宽度、100%／150%／200% 离屏缩放、生成—导出、过期结果和撤销。
- Windows x64 Release 构建及两款 EXE 发布：零警告、零错误。界面检查没有打开或抢占桌面窗口。

Identical free-field conversion produces an identity path, and the Wide monitor target matches the headphone generator. Re-read exported WAVs reproduce the predicted cascade. Risk tests cover degenerate and narrow-angle geometry, near-field model cautions, actual matrix gain, cancellation cost and reconstruction error. Synthetic risk reports additionally verify the red analysis/export banners. Plot tests render all 14 subjects and five analysis types, then perform 150 rapid selection changes. Both published executables built with zero warnings or errors; UI checks remained offscreen.

The position statistic uses seven fixed poses: centre, ±5 cm laterally and fore/aft, and ±5° yaw. C and the centre calibration remain fixed. For independent equal-power L/R inputs, per-ear power responses are smoothed to 1/3 octave, converted to dB, and their population standard deviation across poses is combined as RMS across frequencies and ears. This is a response-variation reference, not a measured perceptual sweet-spot size.

## 默认宽阔监听的模型结果 / Default Wide monitor model results

| 指标 / Metric | Result |
|---|---:|
| 相对复响应还原误差 / Relative complex reconstruction error | −30.65 dB |
| 左耳平滑频响 RMS 偏差 / Left-ear smoothed response RMS deviation | 0.044 dB |
| 右耳平滑频响 RMS 偏差 / Right-ear smoothed response RMS deviation | 0.034 dB |
| 固定姿态频响标准差参考 / Fixed-pose response SD reference | 0.34 dB |
| 共同因果延迟 / Common causal delay | 341.33 ms |
| 因果化投影残差 / Causal projection residual | −72.97 dB |

该配置没有触发生成结果风险阈值。341.33 ms 是本组解的实际共同延迟，实时用途需要考虑它。以上为数字模型和导出文件验证，实际音箱、房间、个人听感及真实多显示器 DPI 切换由用户测试。

This configuration triggers no final risk threshold. Its measured 341.33 ms common delay matters for real-time use. These are digital-model and export checks; physical speakers, rooms, individual listening and live multi-monitor DPI transitions remain for user testing.

以下为此前阶段的历史记录；旧求逆测试指标不代表上述完整 F × C 流程。
The records below describe earlier stages. Their inverse-test figures do not describe the complete F × C workflow above.

---
# 4.14.0 构建与验证 / Build and validation

音箱应用与耳机应用一起发布。以下记录来自本地 Release 构建及发布目录中的程序。

The speaker and headphone applications share one portable package. The following results were obtained from local Release builds and the published executables.

## 自动检查 / Automated checks

| 检查 / Check | Assertions |
|---|---:|
| 13 组核心回归 / 13 core suites | 942 |
| 音箱版中英文离屏界面 / Speaker bilingual offscreen UI | 76 |
| 耳机版语言与导出 / Headphone language and export | 67 |
| 音箱歌曲处理 / Speaker audio rendering | 15 |
| **总计 / Total** | **1100** |

- 核心回归包含 139 项音箱检查：两种模式、矩阵路由、镜像、距离模型、求逆、随机复现、取消、JSON 与 WAV 回读，以及 44.1／48／96 kHz。
- 界面检查包含中文与英文、三种窗口大小、100%／150%／200% 离屏缩放、实际页面生成与导出、过期结果、撤销／重做、图例、缩放与复位。语言切换刷新缓存图表标题，不重新计算 FFT。
- FFmpeg 实际处理测试覆盖原电平浮点 WAV、仅限幅、−18 LUFS 加限幅三条支路，并核对完整尾部、输出路由与采样数据。
- Windows x64 自包含发布零警告、零错误；两款 EXE 位于同一个命名目录。

The core total includes 139 speaker assertions covering modes, matrix routing, symmetry, the range model, inversion, deterministic synthesis, cancellation, JSON/WAV round trips and all three sample rates. UI checks exercise generation and export through the real page handlers, stale-result handling, undo/redo and interactive plot state. FFmpeg tests process generated audio through all three level modes. Both self-contained executables publish with zero warnings and errors.

## 求逆验证 / Inverse validation

- 窄深谷人工测试中，未启用局部约束时求逆峰值为 +23.98 dB，采用固定的局部功率约束后为 +0.71 dB。这是该测试的结果，不是对所有声场的统一峰值承诺。
- FABIAN 自由场矩阵对照中，常规正则化的相对重建误差为 −54.94 dB；加入窄谷抑制后为 −20.99 dB。后者主动接受残差，减少对精确人头缺口的追补。
- 因果化对照的共同延迟为 42.67 ms，投影残差为 −72.20 dB。实际配置的延迟和残差由其生成结果单独记录。
- 2° 人头偏转的模型测试仍有显著误差（约 −3.70 dB 相对误差）：窄谷抑制不能消除串音抵消对听音位置的敏感性。

A synthetic narrow-notch test reduced the inverse peak from +23.98 dB to +0.71 dB. In the FABIAN free-field matrix comparison, the relative reconstruction error changed from −54.94 dB with baseline regularization to −20.99 dB with notch suppression: the latter deliberately trades exact reconstruction for reduced notch inversion. The causal-projection comparison used 42.67 ms of common delay with −72.20 dB projection residual. A 2° head-yaw perturbation still produced substantial error (approximately −3.70 dB relative error), illustrating the remaining position sensitivity.

## 验证边界 / Scope

以上声学指标是数字模型和导出文件的检查结果。实际音箱、房间和个人听感尚未实测；真实多显示器 DPI 切换由用户验收。离屏检查没有打开或抢占桌面窗口。

These acoustic figures describe the digital model and exported files. Actual loudspeakers, rooms and individual listening impressions have not been measured. Live multi-monitor DPI transitions remain for manual acceptance. Offscreen checks did not open or focus desktop windows.

---

# 4.13.0 构建与验证 / Build and validation

本版增加简体中文与英文切换。声学生成、头部处理、EQ 和归一化算法保持不变。

This release adds Simplified Chinese and English presentation. Acoustic generation, head processing, EQ and normalization algorithms are unchanged.

## 本次检查 / Bilingual checks

- 发布程序的 67 项定向检查通过：资源、切换与持久化、全部模板、配置读写、图表状态和导出。
- 同一配置分别在中文和英文环境生成，四条卷积核逐样本相同；导出的 float32 WAV 与预览数据逐样本对应。
- 切换语言保留生成结果、选中项、图表缩放和隐藏曲线；不重复计算 FFT。
- 英文页面在 100%、150%、200% 缩放下离屏布局；检查正常与窄窗口截图。真实多显示器 DPI 切换和桌面操作由用户验收。
- Windows x64 Release 发布零警告、零错误。中英文使用指南与离线帮助同步更新。

The published application passed 67 targeted assertions. The four kernels are sample-identical between Chinese and English generation, and exported float32 samples match the preview. Language changes preserve project and plot state without repeating FFT analysis. Offscreen layouts and screenshots cover 100%, 150% and 200% scaling within the supported minimum window size. Live multi-monitor DPI transitions and interactive acceptance remain for manual testing. Release build and publish completed with zero warnings and errors.

本次未重复执行全部声学回归，也未运行歌曲处理；以下保留 4.12.0 的历史验证记录。

The full acoustic regression and song rendering were not rerun for this update. The following section records the earlier 4.12.0 validation.

---

# 4.12.0 历史记录 / Previous validation

## 正式处理 / Standard processing

FABIAN 带外边缘延伸与非相干人头功率校正固定集成，界面和项目文件不提供两项试验开关。带内幅度没有 −40 dB 下限。整体 EQ、每耳／中置强度、CTF 与球形头选择保留原有用途。详见 [版本说明](releases/4.12.0.md) 和 [人头处理](HEAD_RESPONSE.md)。

FABIAN boundary extension and noncoherent head-power calibration are fixed parts of the standard path. The experiment switches and magnitude floor are removed. Master EQ, tonal strengths, CTF and spherical-head selection remain available for their established purposes.

## 自动验证 / Automated validation

| Suite | Assertions |
|---|---:|
| Head-reference calibration, three rates, generation and exports | 86 |
| Built-in directions, exact mirroring and obsolete configuration fields | 66 |
| Original data, head rendering and routing | 191 |
| Wet-balance precompensation | 35 |
| Spectral synthesis and bandpass | 40 |
| EQ strengths | 53 |
| Final presets | 54 |
| Preset editing | 103 |
| Shared curve editing | 89 |
| Export naming and WAV data | 22 |
| Energy balance | 10 |
| EQ accuracy and output normalization | 50 |
| Reproducible core checks | **799** |
| Local comparison with the accepted preview.3 binary | **1** |
| Local total | **800** |

本地 800 项检查全部通过，Release 编译及 Windows x64 自包含发布零警告、零错误。CI 执行可从源码重现的 799 项检查；额外一项本地检查使用已试听的 preview.3 程序作为独立对照，不要求 CI 下载历史二进制。

All 800 local checks passed, with zero-warning Release build and Windows x64 self-contained publish. CI runs the 799 source-reproducible checks. One additional local check uses the accepted preview.3 binary as an independent oracle.

## 关键结果 / Key results

- 正式版的宽阔监听与 preview.3 两项均开启时的四条最终卷积核 **逐样本相同**。
- 检查 44.1、48、96 kHz 的带内幅度与带外边缘延伸；低于 −90 dB 的人工窄谷保留，没有幅度截平。
- 全部内置模板涉及的方向及上下极点通过镜像核对；两耳路径逐样本镜像一致。
- 人头参考只含非相干功率；方向间反相或纯延时不会引入虚假的校准深谷。
- 实际随机核保留，干湿预修正仍只有一个共同标量；最终混响占比及四条 WAV 路由通过回读验证。
- 旧项目中的试验开关即使为 false，也不会关闭正式固定处理；保存后这些字段不再出现。

The final Wide monitor kernels are sample-identical to preview.3 with both operations enabled. Tests cover all supported sample rates, retained deep notches, fixed processing after loading old configurations, noncoherent reference construction, exact mirror routing and exported wet-energy balance.

## 交互与试听 / Interaction and listening

作者已确认 preview.3 的听感。本次将相同算法固定集成，移除两个开关，并做二进制对照。验证期间未打开或操作桌面窗口。

The author accepted preview.3 by listening. This release fixes those same operations into the normal path, removes the two controls and verifies the final kernels against that binary. No desktop window was opened or manipulated during validation.
