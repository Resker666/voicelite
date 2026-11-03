# VoiceLite 验证报告

> **OGG 修复追加：**当前客户端为0.1.1，最新 C# **49/49**、Python **9/9**；Vorbis/Opus导入、短/长OGG试听、关闭时阻止新试听与取消清理已覆盖。用户目录内6.28秒和19.05秒OGG实际播放完成、原文件不变。详细记录与更新包见 [OGG_FIX.md](OGG_FIX.md)。下文保留首轮0.1.0的34/8项及真实模型验证历史，不把OGG试听验证冒称为新角色声音的真实合成。

日期：2026-11-02，Asia/Shanghai。执行设备为 Windows 11 x64 / RTX 4070 Laptop 8188 MiB / 驱动 610.74。

## 最终结果

| 验证 | 结果与范围 |
|---|---|
| dotnet build VoiceLite.sln | **通过：0 警告、0 错误** |
| dotnet test VoiceLite.sln | **34 通过、0 失败、0 跳过**（显式指定本机 FFmpeg 资源） |
| Python 工具测试 | **8 通过** |
| 原目录真实 GPT-SoVITS API | **通过**：32kHz / 单声道 / PCM16 / 5.14 秒 WAV |
| WPF Mock 生成、导出、播放 | **通过**；明确提示未运行真实模型 |
| WPF + 原引擎真实生成、导出、播放 | **通过**，5.10 秒 WAV |
| 最终完整候选包，中文空格目录、新缓存、精简 PATH | **通过**，打包复核为 5.14 秒 WAV，全流程 34.466 秒，MediaEnded 完成 |
| 41 秒长音频实际 WPF 播放 | **通过**，42.036 秒流程，未被旧 35 秒超时截断 |
| 退出后托管进程 | 本次最终检查没有遗留 VoiceLite/包内 Python/测试辅助进程 |
| 完全干净 Windows，操作系统断网 | **未执行**：没有独立干净测试机/VM。开发机上的新缓存和 Python 网络约束不代替此验收 |
| 公开分发许可 | **未完成**，候选包明确标记 LOCAL_VALIDATION_ONLY |
| CPU / 其它 GPU / 最低显存 / 音色人工评价 | **未执行**，不宣称通过 |

仓库开始时没有客户端源码或现有客户端测试。上述 34 项是本轮新增测试，不包含上游训练、全模型版本或全部语言的测试。

## 实际执行命令

以下均在项目根目录执行。为避免与系统 dotnet 6 混淆，构建前设置：

```powershell
$env:DOTNET_ROOT = "$PWD\.tools\dotnet"
$env:DOTNET_CLI_HOME = "$PWD\.tools\cli-home"
$env:NUGET_PACKAGES = "$PWD\.tools\nuget"
$env:VOICELITE_TEST_ENGINE_ROOT = 'E:\ai\voice\GPT-SoVITS-v2pro-20261006'
```

| 实际命令 / 步骤 | 结果 / 证据 |
|---|---|
| Get-ChildItem 当前目录与引擎目录；读取两份需求及 api_v2.py、TTS.py、tts_infer.yaml、LICENSE | 开始时客户端无代码；引擎是无 .git 的整合包，有运行时与模型 |
| dotnet --info | 系统只有 6.0.5 运行时，没有 SDK |
| nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader | RTX 4070 Laptop、8188 MiB、610.74 |
| 包内 python -B -c 导入 torch / importlib.metadata | Python 3.12.10；Torch 2.7.1+cu118；CUDA 可用；FastAPI 0.115.14；uvicorn 0.50.1 |
| tools/bootstrap-dotnet.ps1 | 官方 SDK 8.0.427 ZIP 下载、SHA-512 校验、项目内解压成功；来源在 .tools/sdk-source.json |
| dotnet new sln -n VoiceLite；dotnet sln add 四个项目 | 成功，使用项目内 SDK |
| dotnet restore VoiceLite.sln --disable-parallel | 成功，MVVM 与测试依赖来自 NuGet，已生成 packages.lock.json |
| Windows PowerShell -File tools/create-test-reference.ps1 | Microsoft Huihui Desktop 生成测试录音；修正 UTF-8 BOM 与参考长度后成功 |
| 包内 python -B tools/engine-smoke.py --engine 原引擎 --reference .artifacts/smoke/reference.wav | 成功，.artifacts/smoke/real-report.json |
| Windows SoundPlayer.Load / PlaySync | 返回成功，无解码错误；不是人工听感评分 |
| dotnet test tests/VoiceLite.Tests --no-restore ... services-red.trx | 11 项预期失败（功能桩） |
| 同一套测试 ... services-green.trx | 11/11 通过 |
| ... process-red.trx → process-green.trx | 新增进程/FFmpeg 测试先失败；首次实现 19/20，Windows 删除可执行映像的短暂锁定导致清理失败；加入有限重试后 20/20 |
| ... viewmodel-red.trx；workflow-red.trx → workflow-green.trx | ViewModel/设置/生成/导出测试先失败，随后 28/28 通过 |
| dotnet build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal | 第一次完整 WPF 构建通过 |
| dotnet VoiceLite.dll --smoke-ui --mock --data-dir .artifacts/ui-mock | 模拟 UI 生成、保存、播放通过；2 秒提示音 |
| dotnet VoiceLite.dll --smoke-ui --engine 原引擎 --reference 测试录音 --data-dir .artifacts/ui-real | 真实 WPF 闭环通过；5.10 秒音频，全流程 26.456 秒 |
| python -B -m unittest discover -s tools -p test_*.py -v | 初期 6/6；后新增 2 项离线回归，最终 8/8 |
| tools/publish-offline.ps1 -EngineRoot 原引擎 -OutputDirectory dist/VoiceLite-local-win-x64-0.1.0 -LocalValidation -Archive | 初始本地候选构建成功；后来修复和补资源，此初始 ZIP 已被最终包取代 |
| 独立只读代码审查 | 找出 3 Important，见 CODE_REVIEW.md；未把审查者静态意见当作执行证据 |
| ... review-red.trx → review-green.trx | 5 项回归先失败，修复进程 Job 竞争、播放截断、参考覆盖及坏 FFmpeg 启动错误后 33/33 |
| 自包含 VoiceLite.exe --smoke-ui --mock --playback-check .artifacts/long-silent.wav | 实际 41 秒静音 WAV 播放至 MediaEnded，通过；.artifacts/ui-long-playback/ui-smoke.json |
| Move-Item 已核实位于 dist 内的候选目录到“VoiceLite 离线验证 0.1.0” | 成功；实际 Python sys.path 指向新目录，没有旧引擎目录依赖 |
| tools/validate-offline.ps1，DataRoot=.artifacts/离线 首次启动 | **失败**：API ready 后 /tts 180 秒超时，不能标通过 |
| engine-smoke.py --runner tools/engine_runner.py --clean-env，原引擎 | 通过：32.875 秒启动、11.062 秒合成；排除了包装层普遍不可运行的假设 |
| 同上，迁移后的包内引擎 | **失败**；开发诊断栈定位到 g2pw 缺失触发的 requests 下载 |
| 新增缺失必需 G2PW / 本地代理隧道回归 | 两项先失败；补齐本地模型、强制必需资源不能被筛除、清理代理并拒绝外部 DNS 后 8/8 通过 |
| tools/validate-offline.ps1，DataRoot=.artifacts/离线 修复后 | 通过：5.46 秒音频，全流程 40.021 秒 |
| OOM 单元测试 oom-red.trx → final.trx | 原通用 HTTP 提示不能识别 OOM；修复后显示“显存不足”且不泄露路径 |
| dotnet build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal | **最终通过**；.artifacts/build-final.log |
| dotnet test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger trx;LogFileName=final.trx --results-directory .artifacts/tests -v minimal | **最终 34/34**；.artifacts/tests/final.trx |
| dotnet publish src/VoiceLite.Desktop -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o 最终候选目录 | **通过**；.artifacts/publish-final.log |
| tools/validate-offline.ps1，DataRoot=.artifacts/离线 最终验证 | **最终通过**；5.26 秒、32kHz、单声道；播放完成；.artifacts/offline-final-validation.log |
| 补入显式 fast_langdetect 必需资源后重建、34 项测试、最终自包含发布和 DataRoot=.artifacts/离线 打包复核 | **通过**；5.14 秒、32kHz、播放完成；.artifacts/offline-release-validation.log；最后 UI 间距已验证 |
| tools/check-package-coverage.py，比较实际候选与源目录选择集合 | 40139/40139 个引擎文件；无缺失、无意外文件，避免依赖运行时补齐资源 |
| tools/audit-upstream.py | 对 21 个源码/模型文件哈希；197 个 Python distribution 清单；3 个核心上游文件哈希与最初一致 |
| git init -b main；git check-ignore dist、.tools、.artifacts 音频 | 初始化本地仓库并确认大资源被忽略；没有提交、远程创建、推送或公开发布 |

受限进程环境中的首次联网、SAPI/COM 和 dotnet test 执行因 socket、注册表/进程权限失败；在正常 Windows 权限下重试后取得上述实际结果。没有收到自动审批拒绝。开发过程中保留了失败日志，没有将其计入通过项。

## 离线缺陷与修复记录

初包错误地依据 `G2PW=0` 排除了 G2PWModel；实际 `chinese2.py` 写死启用 G2PW。在首次中文推理时，上游尝试下载模型。继承的本地代理使请求绕过仅基于 loopback 的初版 socket 限制，产生了 **411648000 字节的部分 ZIP**。诊断进程已结束，该部分文件已删除，未用于最终包。

清单复核还发现初次运行补齐了语言识别资源 `fast_langdetect/lid.176.bin`（131266198 字节）；与原整合包本地文件 SHA-256 一致，最终包显式从原本地资源复制并将它加入必需清单，不能依赖运行时补齐。其 SHA-256 为 `7e69ec5451bc261cc7844e49e4792a85d7f09c06789ec800fc4a44aec362764e`。

修复使用原本已在本机的完整 G2PW 与语言识别资源；EnvironmentProbe 和打包清单均将它们列为必需；打包器拒绝筛掉必需文件。引擎清除 HTTP(S)/ALL/FTP 代理、绕过注册表代理，并在 Python socket/DNS 层拒绝外网。没有修改上游核心算法。该约束不是 OS 网络沙箱；公开版仍需要真正断网验证。

## 硬件记录与证据限制

首次修复后的最终验证整板显存峰值为 4962 MiB，打包复核为 **4898 MiB**（每秒采样，包含其它应用），不能视作精确的 VoiceLite 进程峰值或最低显存要求。WDDM 下进程级显存查询未提供可靠数值。

最终 WAV 与 WPF 截图位于 `.artifacts/离线 打包复核/ui-export.wav`、`ui.png`；较早验证记录也保留。日志、原始报告和 TRX 均在 `.artifacts`。测试参考来自 Windows SAPI，不包含真实个人音色授权或音色相似度评价。上述音频没有进入离线包或源码仓库。

## 剩余验收与下一步

1. 在独立 Windows 11 + NVIDIA 测试机，预先装好驱动后断网、使用新用户和无 Python/.NET/模型缓存环境，验证完整包首次启动。当前设备有 SDK 和其它软件，不能声称是干净机器。
2. 补齐原整合包 commit/来源及每个模型、G2PW、CUDA/cuDNN、加速模块、FFmpeg/ffprobe 与全部 Python 依赖的再分发记录。公开打包门禁保持关闭。
3. 英文实际推理、CPU、其它 GPU/驱动与最低显存矩阵尚未执行。真实 GPU 推理中途手动点击取消尚未专项执行，取消的状态/进程回收已通过自动化辅助进程测试，真实引擎的正常退出回收已验证。主流程中文真机已通过。
4. Minor：运行时 users.pth 中有未启用的开发路径（实际迁移 sys.path 不依赖它）；超大无 Content-Length 响应、运行中的 FFmpeg 取消尚缺专项测试；OpenAPI 应进一步使用有界流读取。详见代码审查。

最终包文件数、整体校验与 ZIP 大小/哈希另记录于 `OFFLINE_PACKAGE.md`，避免将早期包与最终包混淆。
