# 文件清单

## 0.1.3 — 2026-11-03

- 增加可选辅助参考GUI：多选、试听、移除、最多3条、去重、主参考变化清空、上次选择持久化及旧设置兼容。用户同条件对照选择多参考D更像，温度仍保持默认。
- 接通成熟API的aux_ref_audio_paths并校验路径；服务真实转换/传参，成功与取消逐条清理。开发工具支持固定种子、预热、重复控制的温度与单/多参考对照。
- 修复显式CLI无效辅助被软过滤后仍通过Smoke的问题，以及对照工具某条清理失败跳过剩余文件/错误标记哈希通过的问题。
- 修复累积客户端补丁未包含“当前与原基包一致但曾被旧更新修改”的受管文件问题，覆盖换行差异场景。
- 72项C#、30项工具测试通过；真实GUI带两条辅助生成/导出/播放通过；设置恢复、已安装客户端启动及整包40621文件SHA通过。日常目录已备份升级0.1.3，原模型和8.50GB基包保留。

## 0.1.2 — 2026-11-03

- 接通自动/NVIDIA/CPU设备选择及持久化，检查环境与生成共用包内PyTorch检测；CPU为FP32，独立超时，取消和关闭会等待检测收尾。
- 修正CPU内存错误提示，显式NVIDIA不可用时明确报错，模型错误不自动改用CPU重试。
- 三个产品项目声明win-x64还原目标，修复干净环境solution locked restore，测试项目和锁文件保持原配置。
- 累积客户端更新工具从统一版本号打包，提供校验、进程检查、备份、自动恢复和手动回滚；原模型与基包ZIP保留。
- 完整包校验兼容Windows关闭长路径支持时的portable Python，不修改系统设置。
- 真实CPU验收因可提交内存余量不足停止，按用户要求延期；不能将自动化测试或Mock结果视作真实CPU已通过。
- 回家后复核675ae97：本地还原/构建、C#59项、Python21项和0.1.2真实NVIDIA生成/导出/播放通过；已备份升级家里日常目录，全包40621文件SHA校验通过。详见TEST_REPORT。
- 增加手动运行的真实NVIDIA取消/恢复/关闭验收工具，以及其输入目录隔离回归；独立电脑断网验收按用户要求后置。
- 支持锁定API的seed/temperature字段并保持原默认值；新增预热、固定文案/种子、重复控制的手动音色参数对照。当前C#61/61、工具28/28，具体音色选择待用户试听。

以下为此前源码文件清单。

首轮客户端和离线候选实现，以及0.1.1 OGG修复的源码文件。构建物、SDK、模型、用户音频与测试音频均排除；没有自动提交或推送。

- .gitignore
- CODEX_START_HERE.md
- Directory.Build.props
- LICENSE
- README.md
- THIRD_PARTY_NOTICES.md
- VoiceLite.sln
- VoiceLite_Development_Plan.md
- docs/CHANGES.md
- docs/CODE_REVIEW.md
- docs/GIT_UPLOAD.md
- docs/IMPLEMENTATION_PROGRESS.md
- docs/OFFLINE_PACKAGE.md
- docs/OGG_FIX.md
- docs/TEST_REPORT.md
- docs/UPSTREAM_AUDIT.md
- docs/superpowers/plans/2026-11-02-voicelite.md
- docs/superpowers/specs/2026-11-02-voicelite-design.md
- global.json
- packaging/START_HERE.txt
- packaging/engine-profile.json
- src/VoiceLite.Core/Contracts.cs
- src/VoiceLite.Core/MainViewModel.cs
- src/VoiceLite.Core/PlaybackAwaiter.cs
- src/VoiceLite.Core/PlaybackQueue.cs
- src/VoiceLite.Core/ReferenceAudioFormats.cs
- src/VoiceLite.Core/VoiceLite.Core.csproj
- src/VoiceLite.Core/WaveInfo.cs
- src/VoiceLite.Core/packages.lock.json
- src/VoiceLite.Desktop/App.xaml
- src/VoiceLite.Desktop/App.xaml.cs
- src/VoiceLite.Desktop/MainWindow.xaml
- src/VoiceLite.Desktop/MainWindow.xaml.cs
- src/VoiceLite.Desktop/VoiceLite.Desktop.csproj
- src/VoiceLite.Desktop/app.manifest
- src/VoiceLite.Desktop/packages.lock.json
- src/VoiceLite.Infrastructure/AppLog.cs
- src/VoiceLite.Infrastructure/AssemblyInfo.cs
- src/VoiceLite.Infrastructure/AudioImportService.cs
- src/VoiceLite.Infrastructure/AudioPreviewService.cs
- src/VoiceLite.Infrastructure/EngineLayout.cs
- src/VoiceLite.Infrastructure/EngineProcessManager.cs
- src/VoiceLite.Infrastructure/EnvironmentProbe.cs
- src/VoiceLite.Infrastructure/GenerationService.cs
- src/VoiceLite.Infrastructure/LoopbackHttp.cs
- src/VoiceLite.Infrastructure/ProcessRunner.cs
- src/VoiceLite.Infrastructure/SettingsStore.cs
- src/VoiceLite.Infrastructure/TtsApiClient.cs
- src/VoiceLite.Infrastructure/VoiceLite.Infrastructure.csproj
- src/VoiceLite.Infrastructure/WaveExporter.cs
- src/VoiceLite.Infrastructure/WindowsChildProcess.cs
- src/VoiceLite.Infrastructure/WindowsProcess.cs
- src/VoiceLite.Infrastructure/packages.lock.json
- tests/VoiceLite.Tests/OggImportTests.cs
- tests/VoiceLite.Tests/PlaybackQueueTests.cs
- tests/VoiceLite.Tests/ProcessAndAudioTests.cs
- tests/VoiceLite.Tests/ReviewRegressionTests.cs
- tests/VoiceLite.Tests/ServiceTests.cs
- tests/VoiceLite.Tests/ViewModelTests.cs
- tests/VoiceLite.Tests/VoiceLite.Tests.csproj
- tests/VoiceLite.Tests/WorkflowTests.cs
- tests/VoiceLite.Tests/packages.lock.json
- tools/VoiceLite.TestEngine/Program.cs
- tools/VoiceLite.TestEngine/VoiceLite.TestEngine.csproj
- tools/VoiceLite.TestEngine/packages.lock.json
- tools/archive-offline.ps1
- tools/audit-upstream.py
- tools/bootstrap-dotnet.ps1
- tools/check-package-coverage.py
- tools/create-test-reference.ps1
- tools/engine-smoke.py
- tools/engine_runner.py
- tools/package-client-update.py
- tools/package-resources.py
- tools/publish-offline.ps1
- tools/test_client_update.py
- tools/test_engine_runner.py
- tools/test_package.py
- tools/update-package-manifest.py
- tools/validate-offline.ps1
- tools/verify-package.py
