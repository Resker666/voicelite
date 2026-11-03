# VoiceLite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付可构建、有测试的 WPF 本地 TTS 客户端及可验证的离线分发流程。

**Architecture:** Core 定义边界，Infrastructure 实现受控进程/HTTP/音频导入，Desktop 绑定 ViewModel。现有 GPT-SoVITS 作为独立引擎，以真实非流式 /tts 集成。

**Tech Stack:** .NET 8 WPF、C#、HttpClient、Windows Job Objects、FFmpeg、Python/PyTorch、PowerShell、xUnit。

**Spec:** docs/superpowers/specs/2026-11-02-voicelite-design.md，及 VoiceLite_Development_Plan.md。

## Global Constraints

- 只绑定和连接 `127.0.0.1`；不请求或终止未知端口所有者。
- 不修改 GPT-SoVITS 核心，不下载模型，不覆盖已有环境。
- 不提交权重、用户音频和构建工具；记录真实命令和结果。
- Mock 明示；取消、退出、日志、超时、端口归属均需处理。
- 完整离线包必须使用固定资源与校验清单；许可和干净机器离线测试未完成时不得宣称公开发布就绪。

## Review Focus

- 占用端口的未知服务不得收到一次 HTTP 请求。
- 取消或窗口退出后，无本应用遗留 Python/FFmpeg 子进程。
- 含空格、中文与不存在/损坏的录音均有明确结果，不覆盖原始素材。
- 错误 JSON、截断 WAV、超时不能留下有效结果或部分导出。
- 离线包迁移目录后不能依赖开发机绝对路径或缓存。

### Task 1: Audit and real baseline

Files: docs/UPSTREAM_AUDIT.md, tools/engine-smoke.py, .artifacts/ (ignored).
Produces: 本地源码 SHA-256、API 契约、运行时/模型清单、真实 WAV 验证证据。
- [x] 核对引擎版本、许可、运行时、模型和自下载资源（来源/分发阻塞已记录）。
- [x] 使用包内 Python 和外置临时配置启动真实 API，生成一条测试 WAV；记录阻塞而不伪造结果。

### Task 2: Service boundaries and safe implementation

Files: src/VoiceLite.Core/*, src/VoiceLite.Infrastructure/*, tests/VoiceLite.Tests/*.
Interfaces: EngineOptions, TtsRequest, IEngineManager.StartAsync/StopAsync, ITtsClient.GenerateAsync, IAudioImportService.ImportAsync。
- [x] 先写环境缺失/端口冲突/WAV 校验/HTTP 契约/取消/FFmpeg 测试，运行确认失败。
- [x] 实现 EnvironmentProbe、EngineProcessManager（端口归属、Job Object、真实 OpenAPI 就绪）、TtsApiClient、AudioImportService。
- [x] 运行 dotnet test，确认核心测试通过。

### Task 3: Desktop workflow

Files: src/VoiceLite.Desktop/*, tests/VoiceLite.Tests/ViewModelTests.cs.
Consumes: Task 2 服务；Produces: WPF GUI、可取消单任务、设置持久化、试听与保存。
- [x] 先写验证状态与取消/错误的 ViewModel 测试并确认失败。
- [x] 实现参考录音、参考文字、语言、合成文字、生成/取消、状态、试听/保存、Mock 显式提示。
- [x] 构建并启动 UI，执行模拟闭环及真实服务集成测试。

### Task 4: Offline package and handoff

Files: tools/publish-offline.ps1, packaging/*, README.md, THIRD_PARTY_NOTICES.md, docs/TEST_REPORT.md.
- [x] 包装脚本先校验清单、路径和资源完整性，构建自包含客户端，复制固定资源并生成哈希清单。
- [x] 执行打包或精确记录空间、SDK、许可等阻塞；开发产物和公开发行就绪分开说明。
- [x] dotnet build / dotnet test、真实 WAV/播放/退出检查；记录逐项命令与结果。
- [x] 进行独立代码审查并修复关键问题，给出运行说明和剩余风险。

交付：docs/TEST_REPORT.md、docs/OFFLINE_PACKAGE.md、docs/CHANGES.md。此计划的本地验证工作已执行；公开许可和干净机器验收仍未通过，参见报告，不将其标成完成。
