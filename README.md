# VoiceLite

面向普通 Windows 用户的本地声音克隆桌面工具。选择参考录音，填写录音中的文字，输入想说的内容，在电脑上生成、试听并保存 WAV。

WPF / .NET 8 / MVVM，复用 [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS) 的 `api_v2.py` 推理，原始素材留在本机。

**当前状态：可运行的本地验证版。** 已通过真实中文合成、WPF 生成/播放/导出和中文目录迁移验证。完整离线候选包已在本机构建；公开分发仍待全部组件许可核查、上游快照来源补证和干净 Windows 真正断网验收。没有发布到外部服务。

本源码仓库包含客户端、测试、打包工具和文档，不包含模型、Python/.NET 运行环境、用户录音或完整离线包。首次上传的文件范围见 [Git 上传清单](docs/GIT_UPLOAD.md)。

## 普通用户如何使用

以下是完整离线候选包的使用流程，下载包尚未公开发布。从源码运行请看下方“本地开发”。0.1.2 客户端累积包含 OGG 修复和设备选择；已有 0.1.0 基包可以直接更新到 0.1.2，无须先叠加 0.1.1，保留原来的 `engine` 文件夹和模型。升级步骤见 [离线候选包说明](docs/OFFLINE_PACKAGE.md)。

1. 获取完整离线包并解压整个文件夹，双击 **VoiceLite.exe**。
2. 选择一段 **3–10 秒**、清晰的 WAV、MP3 或 OGG 录音，并填写录音实际说出的文字。
3. 输入合成文字，点击 **生成语音**，完成后试听或保存 WAV。

包内包括 .NET、Python、推理依赖、FFmpeg 和一套模型；日常使用无须命令行、Conda 或手动安装 Python。VoiceLite 自动寻找旁边的 `engine` 文件夹，请保留完整目录。首次加载模型需要等待，CPU 通常更慢；同一次运行中后续生成会复用引擎。

“运行设置”可选择 **自动 / NVIDIA / CPU**，默认自动：包内 PyTorch 检测到可用 CUDA 时使用 NVIDIA，否则使用 CPU。CPU 使用 FP32；Intel 显卡和 NPU 尚未接入加速。“检查环境”和生成使用相同的设备选择；资源缺失、模型错误或显存不足会给出诊断。

取消会停止并回收本应用的引擎；下一次生成重新加载模型。关闭窗口同样回收托管进程。程序记住上次的参考录音、文字、语言和设备选择。

0.1.2 NVIDIA 真实生成、导出和播放已在 Windows 11 x64、RTX 4070 Laptop 8GB、驱动 610.74 上回归通过；选择 NVIDIA 模式需要可用驱动和 CUDA。CPU 真实合成仍未验收，具体命令、耗时、内存及范围见 [测试报告](docs/TEST_REPORT.md)。其它 GPU 和最低显存尚未验收。

“模拟演示”只生成提示音，并明确提示未运行真实模型。

默认数据目录：`%LOCALAPPDATA%\VoiceLite\`，包含设置、生成结果及诊断日志。原始参考录音不会被转换覆盖；保存结果也会拒绝当前参考路径。日志不记录完整合成文字和参考录音内容。仅使用自己拥有或已获授权的参考声音。

## 本地开发

Windows x64，需要 `global.json` 固定的 .NET SDK 8.0.427。可以使用自己的同版本 SDK，或在缺少 `.tools/dotnet/dotnet.exe` 时执行下面的 SDK 引导脚本（需联网下载约数百 MB，校验 SHA-512，不修改系统 SDK）。已携带 `.tools` 的迁移环境可跳过下载：

```powershell
if (!(Test-Path .\.tools\dotnet\dotnet.exe)) { .\tools\bootstrap-dotnet.ps1 }
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:DOTNET_CLI_HOME = "$PWD\.tools\cli-home"
$env:NUGET_PACKAGES = "$PWD\.tools\nuget"
& "$env:DOTNET_ROOT\dotnet.exe" restore VoiceLite.sln --locked-mode
& "$env:DOTNET_ROOT\dotnet.exe" build VoiceLite.sln --no-restore
& "$env:DOTNET_ROOT\dotnet.exe" test VoiceLite.sln --no-restore
& "$env:DOTNET_ROOT\dotnet.exe" run --project src/VoiceLite.Desktop --no-build
```

三个产品项目已显式声明 `win-x64` 还原目标，与提交的锁文件一致；测试和测试辅助项目保留普通 .NET 目标。因此干净检出可以直接对整个解决方案执行 `restore --locked-mode`，不需要修改锁文件或分项目还原。

已携带完整 `.tools/nuget` 缓存时，可将上面的还原命令替换为下列命令，仅使用本地包源；缺包时会明确失败，不会转而下载依赖：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" restore VoiceLite.sln --locked-mode --source "$PWD\.tools\nuget"
```

未放入内置引擎时，可在“运行设置”选择已有兼容整合包，或选择模拟演示。当前只接受审计过的本地 API SHA-256 快照，详见 [上游审计](docs/UPSTREAM_AUDIT.md)。生产代码没有开发机引擎路径。

FFmpeg 集成测试需要将 `ffmpeg.exe` 放到 PATH，或设置 `$env:VOICELITE_TEST_ENGINE_ROOT` 为兼容整合包根目录。缺失工具时这些项目会明确跳过，其余测试仍运行；不会下载模型或悄悄把模拟当真实结果。OGG（Vorbis/Opus）自动转为 WAV，用于推理和试听，原始录音保持不变。

本机真实取消、异常恢复和窗口关闭的手动验收使用 [WorkflowProbe](tools/VoiceLite.WorkflowProbe/README.md)。该工具复用产品界面与服务，显式启动后运行真实NVIDIA模型并主动结束自己拥有的测试引擎；普通单元测试不会自动运行它。

Python 工具测试可以使用已有引擎 Python（不安装任何依赖）：

```powershell
& '<引擎目录>\runtime\python.exe' -B -m unittest discover -s tools -p 'test_*.py' -v
```

## 构建完整离线候选包

资源和运行时不进入 Git。打包脚本从已存在的整合包复制固定模型和推理资源，不执行上游安装脚本。先完成许可清单核查再解除公开发行门禁；当前本地验证命令：

```powershell
.\tools\publish-offline.ps1 -EngineRoot '<已有整合包根目录>' -LocalValidation -Archive
```

输出在 `dist`，包含自包含客户端、engine、逐文件哈希和许可副本。`LOCAL_VALIDATION_ONLY.txt` 表示尚未达到公开再分发状态。包较大，最终下载渠道需考虑附件限制；不能把大模型塞进源码仓库。

迁移与无开发缓存的验证：

```powershell
.\tools\validate-offline.ps1 -PackageRoot '<候选包目录>' -ReferencePath '<测试参考.wav>' -DataRoot '<新的空测试目录>'
```

该命令清理 Python/.NET 路径依赖、使用新缓存；引擎包装层拒绝外部 Python socket 和继承代理。它不是操作系统网络沙箱。真正的干净 Windows 断网验收仍需独立机器/虚拟机，不用本机结果代替。

## 项目结构与验证

- `src/VoiceLite.Core`：契约、WAV 验证、ViewModel 与播放状态。
- `src/VoiceLite.Infrastructure`：环境检测、受控进程、HTTP、音频导入、设置与导出。
- `src/VoiceLite.Desktop`：WPF 窗口、文件选择、试听与保存。
- `tests/VoiceLite.Tests`：服务、进程、音频、状态和回归测试。
- `tools` / `packaging`：审计、真实合成验证、离线资源选择和打包。

实际命令、通过/失败/未执行项见 [测试报告](docs/TEST_REPORT.md)。设计和后续阶段见 [开发计划](VoiceLite_Development_Plan.md)。

## 许可

VoiceLite 原创客户端代码采用 [MIT](LICENSE)。引擎、模型、运行时及第三方组件使用各自许可，见 [第三方清单](THIRD_PARTY_NOTICES.md)。
