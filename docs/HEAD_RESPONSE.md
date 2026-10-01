# FABIAN 人头处理 / FABIAN head-response processing

## 固定处理链

FABIAN 在生成时执行带外边缘延伸；EQ 开启时，在每耳／中置音色校正前执行共同人头功率校正。这两项随正式处理流程生效，不提供独立开关。整体 EQ 旁路、两级音色校正强度及原有 CTF 选项保留。

```text
方向统计核 / 音箱直达声
→ FABIAN 方向响应（带外边缘延伸）
→ 合成四条路径
→ 共同人头功率校正
→ 每耳平滑 EQ
→ 中置共同 EQ
→ 输出带通与共同归一化
```

## 带外边缘延伸

20 Hz–20 kHz 保留每个方向、每只耳朵的幅度，包括低于 −40 dB 的深谷。低于 20 Hz 沿用 20 Hz 的幅度，高于 20 kHz 沿用 20 kHz 的幅度，直至输出采样率的 Nyquist 频率。不存在幅度下限。

由原生 44.1 kHz 数据及所选 CTF 状态直接重建目标采样率，避免反演重采样器的超声阻带。带内超额相位来自原始数据；带外按边缘斜率延伸并平滑满足实数脉冲条件。最小相位主体随目标幅度变化，有限长因果投影后校核实际幅度。共同前导约 1.09 ms，并计入时间基准；核尾长不等于直达等待时间。

数值验收采用 0.05 dB 相对误差或峰值幅度的 1e-5 绝对误差容差；容差不参与幅度目标，不会把深谷截到某个电平。尾端按剩余能量选长并渐隐，必要时修正投影残差。最终 20 Hz–20 kHz 带通由输出环节单独负责。

## 非相干人头功率校正

全部启用方向各用一个等能量单位冲激测试，包括音箱和反射源镜像方向，重复方向只取一次。计算：

```text
P(f) = sum_d (|H_left,d(f)|² + |H_right,d(f)|²) / (2 × directionCount)
|Q_head(f)| = 1 / sqrt(P(f))
```

该参考没有方向间相干交叉项，不包含某次随机混响的细纹，也不在频率轴上平滑。共同最小相位校正施加到四条实际路径，保持路径间的复响应比；实际随机核保持原样。

随后沿用每耳平滑 EQ 和可调强度的中置 EQ。混响能量预修正仍只使用一个共同标量，计算时包含各级校正对直达声与混响的影响。

## English

FABIAN bandwidth extension is a fixed part of head rendering. With master EQ enabled, a fixed common noncoherent head-power calibration precedes the adjustable per-ear and center tonal stages. There are no separate switches for these two steps. Master EQ bypass, tonal strengths and the existing CTF option retain their roles.

For each direction and ear, preserve magnitude—including deep notches—within 20 Hz–20 kHz, and extend the corresponding boundary magnitude outside that range to the output Nyquist frequency. No magnitude floor is applied. Reconstruction starts from native 44.1 kHz data, avoiding inversion of a resampler's ultrasonic stop band. Measured in-band excess phase is combined with the target's minimum-phase component; out-of-band excess phase is extended with smooth real-impulse endpoint conditions. Finite causal projection uses a declared common lead-in of about 1.09 ms and checks the resulting magnitude. Numerical accuracy tolerances do not clamp the target. The final output bandpass remains separate.

Head calibration uses equal-energy unit impulses for each distinct enabled direction. The binaural power mean P(f), defined above, has no coherent cross terms, random-room detail or frequency smoothing. One common minimum-phase inverse acts on all four actual paths, preserving their complex ratios. Existing per-ear EQ, center EQ and scalar-only wet-balance precompensation follow it.
