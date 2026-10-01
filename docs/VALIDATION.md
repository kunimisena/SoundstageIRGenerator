# 4.12.0 构建与验证 / Build and validation

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
