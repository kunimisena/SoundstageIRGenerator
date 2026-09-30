# 构建与发布

[English](../en/development.md) · [首页](../../README.md)

## 环境与构建

Windows x64、.NET 8 SDK 即可；`global.json` 接受已安装的最新稳定 .NET 8 功能带。不需要付费 IDE。FABIAN 转换表已包含，Python 仅在重新转换数据时需要。

```powershell
./build.ps1
```

构建 Release，执行推荐与新增模板、EQ 强度、人头、模板参数、整体编辑、发布命名、能量、EQ 精度八组核心检查，然后发布带运行时的程序到 `publish/SoundstageIRGenerator/`。不会把私人项目复制到分发包；覆盖自己的本地程序时保留原有用户数据；要制作干净分发包请用打包脚本或全新输出目录。

```powershell
# 完整 DSP、统计、长尾、音频处理和离屏 WPF 检查：
./build.ps1 -FullTests -HeadlessChecks
# 仅编译发布：
./build.ps1 -SkipTests
# 指定新的输出目录：
./build.ps1 -OutputDirectory ./publish/MyLocalBuild
# 为个人使用复制 FFmpeg，可包含 bin/ 和许可证文件：
./build.ps1 -FFmpegDirectory 'C:\path\to\ffmpeg'
```

完整音频测试需要提前可用的 FFmpeg/ffprobe。离屏 WPF 检查在 Windows 后台运行，不显示窗口。`-FFmpegDirectory` 在发布阶段复制工具；音频测试通过 PATH 或 Program Files 查找 FFmpeg。结果放入 `artifacts/`。普通 CI 只执行八组核心检查和构建，完整音频与手动桌面验收另行进行。

## 源码与本地用户数据分开

仓库根目录是包含 SoundstageIRGenerator.sln 的文件夹。保留 App、Core、Tests、tools、docs、.github 和根目录构建/文档文件。`Core/Data/FABIAN.bin` 约 24 MiB，是正常构建所需数据，刻意纳入版本管理，并附来源与哈希。

`bin`、`obj`、`publish`、`artifacts`、`projects`、`exports`、`EqualizerAPO`、`processed-audio` 和备份文件夹不进 Git，已列入 `.gitignore`。不要直接公开使用过的整个便携目录，其中可能含私人路径、歌曲和参数。

```powershell
./tools/package.ps1
```

脚本按明确白名单复制源码到全新暂存目录，从该副本编译，生成源码包、Windows x64 程序包和 SHA256 校验值，放在 `publish/packages/`。不包含 FFmpeg 或个人数据。运行时许可证来自实际发布使用的 NuGet 运行时包。暂存目录位于被忽略的 `artifacts/`，便于检查；同版本压缩包不覆盖，重新运行可指定 `-Destination`。

自有代码与文档使用 MIT 许可证，FABIAN 保留 CC BY 4.0 署名。打包脚本生成本地文件。

## 仓库与发布

源码仓库：[SoundstageIRGenerator](https://github.com/kunimisena/SoundstageIRGenerator)。[Releases](https://github.com/kunimisena/SoundstageIRGenerator/releases) 提供带运行时的 Windows 程序、源码包和 SHA-256 校验值。

CI 对推送与拉取请求执行构建和核心检查。发布前运行 `./build.ps1 -HeadlessChecks` 与 `./tools/package.ps1`，按版本打标签，并上传 `publish/packages/` 中的发布包。中英文文档同步维护。

## 代码入口

- `Core/Models.cs`、`Presets.cs`：项目参数与内置模板。
- `Core/Generator.cs`：方向叠加、到耳时间、能量平衡和最终处理。
- `Core/HeadRenderer.cs`：实测人头数据、镜像与重采样。
- `Core/Dsp.cs`：FFT/卷积、平滑和最小相位处理。
- `Core/EnergyBalance.cs`：能量参考与干湿归一化。
- `Core/AudioRenderer*.cs`：FFmpeg 外部处理与电平管理。
- `App/MainViewModel*.cs`：编辑、生成、导出状态。
- `App/MainWindow.xaml`、`ReflectionBatchEditor*`、`VisualControls.cs`：页面与图表。
- `Tests/`：复现、路由、EQ、统计、音频和文件验证。

离屏验收命令为 `SoundstageIRGenerator.exe --headless-check <输出目录>`，验证控件、编辑流程和离屏布局。测试项目与导出放在指定目录内；键鼠操作和实际显示器 DPI 切换由手动验收完成。

## 可选：重新转换 FABIAN

从官方来源下载 `Core/Data/FABIAN-NOTICE.txt` 指出的两个 SOFA 文件。在单独的 Python 环境运行：

```powershell
python -m pip install -r tools/requirements-fabian.txt
python tools/convert_fabian.py C:\path\to\sofa-files
```

转换器写入二进制表和来源清单；核对哈希并保留署名。日常构建和使用不需要 Python，不要提交原始 SOFA 与 Python 环境。

## 项目格式与验证

项目 JSON 使用格式版本 5。TemplateName 标识模板名称，TemplateSources 保存模板参数，Sources 保存逐源编辑参数。配置通过 JSON 导出与导入。内置模板的一级 EQ 默认 100%，二级 EQ 默认 0%。[验收说明](../VALIDATION.md)列出测试版本、范围与结果。

本机 FFmpeg 路径保存在 `settings/audio-tools.json`，已由 Git 和干净打包流程排除。发布使用 `tools/package.ps1` 创建的独立包，FFmpeg 由用户在歌曲处理区选择。
