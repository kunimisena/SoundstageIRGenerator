# Release validation / 发布验收 — 4.9.3

2026-10-01 · Windows x64 · .NET 8

Release build and self-contained publish completed with **zero warnings and errors**. The following checks were run for the public release:

| Suite / 检查 | Assertions / 通过项 |
|---|---:|
| Built-in and recommended presets / 内置及推荐模板 | 54 |
| EQ strengths / EQ 强度 | 53 |
| Head models / 人头模型 | 172 |
| Preset parameters / 模板参数 | 103 |
| Shared editing / 整体编辑 | 89 |
| Export naming and routing / 导出命名与路由 | 22 |
| Energy balance / 能量比例 | 10 |
| EQ accuracy / EQ 精度 | 50 |
| **Core total / 核心合计** | **553** |
| Published WPF offscreen checks / 发布程序离屏检查 | **171** |

Core checks cover reproducibility, preset parameters, head filtering, EQ strength, energy normalization, adaptive EQ support, WAV routing and configuration round trips. The actual generated kernels are checked. The head table SHA-256 matches `Core/Data/FABIAN-manifest.json`.

Offscreen checks cover the template/configuration/export workflow, numeric editing, generation, cancellation, JSON saving/loading, export availability, tool-path handling, chart caching and layout at several sizes and DPI settings. The published executable completed these checks without showing a desktop window or opening an audio device.

本次发布完成 553 项核心检查、171 项发布程序离屏检查，构建零警告、零错误。宽阔监听参数和声学算法未改动。检查实际生成的核，覆盖能量归一化、EQ、路由、命名、参数保存重开以及多种尺寸和 DPI 布局。

Interactive desktop operation and listening are assessed separately by users. Song rendering was not rerun in this cleanup/documentation release; it requires a separately selected FFmpeg build with libsoxr and adjacent ffprobe. The distribution includes no FFmpeg binaries.

本轮清理与文档发布未重跑歌曲处理；该功能使用单独配置的 FFmpeg。实际桌面交互与试听由用户验收。

## Reproduce / 复现

```powershell
./build.ps1 -HeadlessChecks
./tools/package.ps1
```

Tests write their evidence under `artifacts/`, which is excluded from source packages. Packaging builds from an explicit clean source copy and includes runtime notices from the exact .NET packs used. Public assets contain the MIT license and FABIAN attribution; personal configurations, exports, listening comparisons and temporary tools are excluded.

验证产物写入 `artifacts/`。打包从源码白名单副本重新编译，保留 MIT、FABIAN 和对应 .NET 运行时声明。源码和程序包均不含个人配置、试听对照或临时工具。
