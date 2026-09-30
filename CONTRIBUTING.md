# Contributing / 参与

Original code and documentation use the [MIT License](LICENSE). Third-party materials retain their respective licenses.

自有代码与文档使用 MIT 许可证，第三方材料保留各自许可。

## Useful reports / 有用的报告

Include app version, Windows version, sample rate, preset/project JSON, head model, mirror setting, both EQ strengths, wet energy percentage, reproduction steps and expected/actual behavior. For listening observations, describe material and level matching; report a preference as a preference. Do not post copyrighted songs, external IRs without redistribution permission, private paths or personal audio exports.

请附版本、采样率、项目 JSON、人头、镜像、两级 EQ、混响比例、复现步骤与预期/实际结果。听感报告说明素材类型和等响度方法；偏好就是偏好，不必装成普适结论。不要上传无权分享的歌曲、核和私人信息。

## Changes / 修改约定

- Keep core DSP independent of WPF. / 核心 DSP 与 WPF 分离。
- Project-format changes require an explicit schema version and serialization tests. / 项目格式变更需明确格式版本并验证序列化。
- Save actual generated outputs in tests; do not substitute target curves. / 验证生成结果，不用目标曲线代替。
- Keep source weights, global energy balance and EQ references distinct. / 区分源权重、整体能量比例和 EQ 参考。
- Discuss perceptually significant model changes before expanding controls. / 显著声音模型变更先讨论，不堆无明确用途的旋钮。
- Update Chinese and English documentation together. / 中英文同步更新。

Run `./build.ps1` for standard checks. Audio tests require FFmpeg; offscreen WPF checks require Windows, and pointer/display acceptance uses a desktop; see [development](docs/en/development.md) / [构建说明](docs/zh-CN/development.md).
