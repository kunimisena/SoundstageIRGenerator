# Release validation / 发布验收 — 4.11.0

2026-10-01 · Windows x64 · .NET 8

## 发布范围 / Release scope

4.11.0 汇总本轮试听确认的改动：四路径频域 EQ 与带通合成、混响尾端渐隐、EQ 后混响占比预修正、带通图表显示开关，以及 FABIAN 单耳镜像。一级 EQ 默认平滑为 1/12 oct，已保存配置继续使用各自的值。详细变化见 [4.11.0 更新说明](releases/4.11.0.md)。正式版沿用 preview.4 的声学实现。

This release collects the auditioned spectral EQ/bandpass synthesis, tail fade, post-EQ wet-balance precompensation, optional bandpass plot range and single-ear FABIAN mirroring. First-stage smoothing defaults to 1/12 octave; saved settings retain their values. The DSP implementation is unchanged from preview.4. See the [release notes](releases/4.11.0.md).

## FABIAN 核对 / FABIAN checks

FABIAN 人头以实测左耳的全方向响应作为参考。右耳使用镜像入射方向对应的左耳响应，中线与极点共用同一实测响应。移除左右镜像响应的复数／时域平均，保留选中 IR 的全部原始样本及共同时间参考。

The FABIAN renderer uses the measured left ear over the full sphere and reflects source direction to construct the right ear. Median-plane and polar directions share one measured response. Selected samples retain their original complex response and time reference; mirrored responses are no longer averaged.

内置数据、CTF、采样率转换、统计混响、能量预修正、两级 EQ、带通、核长预算及输出归一化保持 preview.3 的实现。文件哈希核对确认：除 `HeadRenderer.cs` 和同步的 FABIAN 处理说明外，其余核心文件未变。作者前言保持原文，README 仅更新版本号。

The embedded data, inverse CTF, resampling, statistical synthesis, wet precompensation, EQ, bandpass, support budget and common output calibration retain their preview.3 implementations. File hashes verify the change boundary.

## 自动验证 / Automated validation

| Suite | Assertions |
|---|---:|
| Head model, original samples, mirrored routing and resampling | 191 |
| Wet precompensation and scalar-only preservation | 35 |
| Spectral synthesis, transitions, fade and three sample rates | 40 |
| EQ strengths | 53 |
| Built-in and recommended presets | 54 |
| Energy balance | 10 |
| EQ accuracy and common calibration | 50 |
| Export naming and exact WAV samples | 22 |
| Total | 455 |

455 项自动检查全部通过。Release 编译与 Windows x64 自包含发布零警告、零错误。界面交互未修改，本轮不运行桌面窗口；试听由用户完成。

All 455 automated checks passed. Release build and self-contained Windows x64 publish completed with zero warnings and errors. UI interactions are unchanged; no desktop window is opened in this pass. Listening acceptance belongs to the user.

## 具体核对 / Checks

- 直接读取内置 FABIAN 原始数据，与渲染器 44.1 kHz、CTF 关闭的输出逐样本比较；覆盖同侧、跨侧、中线、后方、上方与极点。
- 验证 44.1/48/96 kHz 的复响应、原有 CTF 和到达时差，以及严格／非严格随机镜像下的路由。
- 水平 60°、8.265 kHz：所选原始同侧响应约 −0.15 dB；被移除的镜像平均处理此前约 −14.75 dB。数值均为共同频响补偿前的单路径传递增益。
- 原始耳廓陷波保留；此次修改只移除由镜像响应相加造成的额外相消。
- 最终混响占比继续从输出核实测；悠长大厅目标 80%，实测约 79.999996%，输出长度仍为 8.106646 秒。

Original samples are verified independently of the renderer, including contralateral and median directions. At horizontal 60 degrees and 8.265 kHz, the selected original ipsilateral gain is about -0.15 dB before CTF compensation, compared with about -14.75 dB under the removed complex average. Native spectral notches remain. Final wet balance is checked against rendered and reread WAV kernels.

日志、测试导出和修改边界哈希位于 `artifacts/fabian-mirror-preview/`。正式版由 GitHub Actions 重新运行 build.ps1 的完整默认检查列表，成功后从干净源码打包并发布；每次运行的日志与断言总数可在仓库 Actions 页面查看。
