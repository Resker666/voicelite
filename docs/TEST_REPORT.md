# VoiceLite 验证报告

## 0.1.3 多参考GUI与本机交付 — 2026-11-03

用户在温度1.0/0.8/0.6对照中反馈听感接近，B备用音频可播放，温度继续保留默认1.0。随后用锁定版本已存在的`aux_ref_audio_paths`能力，保持主参考、文字、文案、种子和温度不变，仅加入两段同角色辅助录音；用户明确反馈**D多参考更像**。这是该组样本的主观选择，不外推为所有素材或所有用户必然提升。

辅助输入为本机About_the_Vision（6.28秒）和Good_Morning（4.096秒）；主参考为用户确认过的3.03秒候选。锁定TTS.py将辅助音频加入refer_spec，models.py对各段声线/SV条件求均值；只有主参考需要文字，没有训练、下载新模型或重写推理算法。每个条件独立预热；单/多参考的各自重复样本哈希都一致，原始3段输入SHA未变。原始报告在`.artifacts/quality-comparison-20261103/samples-references/comparison-report.json`。

GUI已接入添加/多选/试听/移除、最多3条、去重和旧设置兼容；更换主参考清空辅助项。GenerationService实际转换和传送辅助路径，成功/取消后逐条尝试清理。源版本0.1.3，NuGet依赖版本未变，项目引用版本对应的锁文件已本地更新。

| 实际验证 | 结果与证据 |
|---|---|
| 项目内SDK `restore VoiceLite.sln --force-evaluate --source .tools/nuget --disable-parallel`，随后`restore --locked-mode --source .tools/nuget --disable-parallel` | 更新项目版本后锁定还原通过，无依赖下载 |
| `build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal` | 最终0警告、0错误 |
| `test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=v013-final.trx" --results-directory .artifacts/voicelite-013-20261103/tests -v minimal` | **72/72通过、0跳过**，包含真实FFmpeg及多参考成功/取消/设置回归 |
| 包内Python `-B -m unittest discover -s tools -p "test_*.py" -v`，显式启用CLI边界 | **30/30通过**，包括真实Desktop Mock启动的坏辅助路径拒绝及累积补丁回归 |
| `publish src/VoiceLite.Desktop/VoiceLite.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o .artifacts/voicelite-013-20261103/client` | 通过，EXE0.1.3.0 |
| `tools/validate-offline.ps1 -PackageRoot .artifacts/voicelite-013-20261103/client -EngineRoot "dist/VoiceLite 离线验证 0.1.0/engine" -ReferencePath .artifacts/voice-quality-20261103/hutao-name-first-sentence-3.03s.wav -AuxiliaryReferencePaths <上述两条OGG> -DataRoot .artifacts/voicelite-013-20261103/ui-real -Device nvidia -TimeoutSeconds 420` | **真实GUI通过**；mock=false、0.1.3、NVIDIA CUDA、辅助计数2、4.66秒32kHz WAV，导出及实际播放完成，全流程41.261秒 |
| 复制该次settings到新测试目录，运行发布EXE `--smoke-ui --mock --data-dir .artifacts/voicelite-013-20261103/ui-restore-mock` | 不传辅助CLI参数也恢复2条，2秒提示音生成/导出/播放通过 |
| 从原0.1.0 ZIP只读取原始package-files.json，运行`tools/package-client-update.py`制作累积更新 | 最终14个payload，2193475字节，SHA见OFFLINE_PACKAGE |
| 包内Python `tools/apply-client-update.py <0.1.3解压目录> <日常目录> --check`，随后`--backup dist/VoiceLite-0.1.3-home-backup-20261103` | 校验、备份、升级成功，日常EXE0.1.3.0 |
| 包内Python `tools/verify-package.py "dist/VoiceLite 离线验证 0.1.0"` | **40621文件/11519703265字节**完整SHA通过，模型未替换 |
| 安装后日常EXE `--smoke-ui --mock --data-dir .artifacts/voicelite-013-20261103/ui-installed-mock`（新目录复制测试设置） | 启动/恢复两条辅助/2秒提示音导出及播放通过 |

上表SDK命令均使用`.tools/dotnet/dotnet.exe`8.0.427和项目内CLI/NuGet目录。UI截图已检查，辅助名称与2/3计数正常；截图为滚动到辅助区域的状态。约1秒采样中真实GUI进程树WorkingSet峰值4154.82 MiB、Python4001.42 MiB、整板显存4689 MiB；这不是精确峰值、独立进程显存或最低配置要求。

本轮实际发现并经RED→GREEN修复：辅助传参/非法路径、日常服务与设置6项、ComparisonCleanup锁文件不应跳过其他清理及缺失哈希不应报通过2项、显式CLI坏辅助路径应在推理前拒绝、累积补丁应覆盖早期版本改变后本版恢复原基包字节的受管组件。首次升级因包装脚本仅换行差异被校验挡住，未造成部分安装；旧失败候选保留，修复后完成上述真实升级。最终只读复审无剩余Important问题。

真实/Mock区分、原始记录和所有命令输出保存在`.artifacts/voicelite-013-20261103/`。本机3.03秒参考已复制到个人数据目录voice-presets，原文件保留；日常客户端已打开并预选用户认可的主+两条辅助组合。程序没有自动开始生成，用户可输入文案继续试听。独立电脑断网验收仍按用户要求后置，CPU真实合成仍未执行。

## 本机音色参数对照 — 2026-11-03

用户试听3.03秒参考候选后反馈“有点像了，仍有提升空间”。这不是音色相似度验收通过；本轮进一步生成控制变量样本供用户听选。参考来自用户本机素材，只放在忽略目录中。

固定同一3.03秒参考、对应文字、中文文案及种子20261103。文案为“你好，今天我们一起出去走走，顺便看看有什么有趣的事情吧。”。使用本地已锁定v2ProPlus/NVIDIA引擎，仅改变温度；top_k、top_p、语速等保持同一上游默认值。

| 最终预热后样本 | 温度 | WAV时长 | 结果 |
|---|---|---|---|
| A-default | 1.0 | 5.32秒 | 有效32kHz WAV |
| A-repeat | 1.0 | 5.32秒 | 与A-default逐字节一致，SHA256均为`AFF84B1E39ED9FEF0CA7F7DB0E8BBF99B28256C41DCDC9B0C726D8F95CAD6390` |
| B-temperature-0.8 | 0.8 | 4.80秒 | 有效32kHz WAV，用户确认备用版可播放，听感与A/C接近 |
| C-temperature-0.6 | 0.6 | 5.84秒 | 有效32kHz WAV，用户反馈与A接近 |

该轮先加载模型（17.232秒），再用相同输入预热（5.925秒）并丢弃预热输出，随后采集对照样本。原参考SHA保持一致。每段参数和哈希见`.artifacts/quality-comparison-20261103/samples-warm/comparison-report.json`；报告明确`similarity_improvement_verified=false`，没有自动选择更好的温度。固定种子与重复一致只证明本机本轮的变量控制，不保证不同硬件或版本逐字节一致。

用户首次反馈A、C听感接近，B在聊天预览里没有声音。复查原始B的PCM非零样本125696个、峰值-9.76dBFS、RMS-27.66dBFS；A/C的RMS约-27.88/-27.99dBFS，B文件并非数字静音且音量相近。已提供内容哈希完全相同的短文件名`B.wav`和编码为MP3的备用播放文件，两者FFmpeg解码均通过。用户随后确认备用B可播放，三者听感接近；没有选择降低温度作为改进方案。具体预览故障原因未确认，不把温度0.8记为模型静音。

初始未单独预热的批次保留在同目录的`samples/`：同参数重复时长一致，但波形哈希不同；PCM相关系数约0.999836，差异信号相对原信号约-34.85dB。首次请求含参考特征/AR加速器初始化，因此没有把冷A与温B/C的耗时差异归因于温度。最终试听使用上述`samples-warm/`批次。

实际代码与命令：

- `TtsRequest`增加`seed`、`temperature`两个JSON字段，默认仍为锁定API的-1、1；新增默认值和实际HTTP传参测试先2项失败，补齐后通过。日常GUI未增加控制项，也未将某档温度设为推荐值。
- 项目内SDK运行`dotnet build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal`：0警告、0错误。
- `dotnet test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=quality-green.trx" --results-directory .artifacts/quality-comparison-20261103/tests -v minimal`：**61/61通过、0跳过**。
- `dotnet build tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --no-restore --disable-build-servers -m:1 -v minimal`：0警告、0错误。
- 包内Python运行`-B -m unittest discover -s tools -p "test_*.py" -v`，显式启用CLI边界测试：**28/28通过**；新增对照模式拒绝输出到引擎目录的回归。
- `dotnet tools/VoiceLite.WorkflowProbe/bin/Debug/net8.0-windows/VoiceLite.WorkflowProbe.dll --quality .artifacts/quality-comparison-20261103/input-warm.json`：预热和4段真实样本生成通过，进程退出。运行前用户已确认关闭日常VoiceLite，避免在8GB显卡上并行加载两份模型。
- 只读审查无剩余Important问题；其中冷启动公平性建议已通过预热与重复控制处理。

上述`dotnet`均指项目内SDK8.0.427，使用既有NuGet缓存。原始RED/GREEN、构建、生成记录在`.artifacts/quality-comparison-20261103/`。没有下载模型、上传用户音频或改动既有Python环境。用户反馈温度组听感相近，因此没有在GUI增加温度控件；随后多参考对照的选择及GUI接入结果见本报告顶部。

## 本机真实取消与恢复验收 — 2026-11-03

本轮按用户要求只完成自己电脑上的流程，独立电脑/干净Windows断网验收后置，不作为当前本机迭代的前置条件。设备仍为RTX 4070 Laptop 8GB，产品版本0.1.2；CPU真实合成没有在本轮执行。

新增手动运行的 `tools/VoiceLite.WorkflowProbe`，直接使用产品的MainWindow、ViewModel、GenerationService、EngineProcessManager和TtsApiClient。测试使用独立数据目录、SAPI测试参考录音及其实际文字；没有修改推理代码、模型、原始参考录音或日常设置。

| 最终真实场景 | 结果与实际证据 |
|---|---|
| `/tts`请求发出1.5秒后仍未完成时执行取消命令 | 取消收尾约0.050秒；界面恢复、引擎退出、端口释放，无结果或临时参考残留 |
| 同一窗口取消后重新生成 | 新引擎进程启动，生成并导出5.58秒、32kHz WAV |
| 请求中主动终止本次测试自己拥有的引擎 | 先验证端口归属、PID、可执行文件并持有原进程句柄；约0.084秒收尾，显示可重试的中文错误，无无效结果 |
| 同一窗口异常后重新生成 | 新引擎启动，生成并导出5.10秒、32kHz WAV |
| 请求中关闭实际WPF窗口 | 实际OnClosing路径完成取消、窗口关闭、进程退出和端口释放，约0.076秒；无界面错误和残留结果 |

这5项在最终工具版本上全部通过；取消使用与按钮绑定相同的命令，窗口关闭使用真实WPF事件路径。这里没有把程序调用描述为物理鼠标点击。以上是单次测量，不是取消延迟保证或性能基准。原始参考SHA256保持一致。

实际执行的命令均在仓库根目录执行；下表的`dotnet`指项目内`.tools/dotnet/dotnet.exe`，SDK环境变量设置与下方家里回归记录相同：

| 命令 | 结果 |
|---|---|
| `dotnet restore VoiceLite.sln --locked-mode --source .tools/nuget --disable-parallel` | 通过，无依赖下载、无产品锁文件改动 |
| `dotnet build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal` | 0警告、0错误 |
| `dotnet test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=workflow-final.trx" --results-directory .artifacts/workflow-20261103/tests -v minimal` | 59/59通过、0跳过，显式指定已有FFmpeg资源 |
| `dotnet restore tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --source .tools/nuget --disable-parallel`，随后同项目 `restore --locked-mode --source .tools/nuget --disable-parallel` | 新工具初次还原生成自身锁文件，随后锁定还原通过 |
| `dotnet build tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --no-restore --disable-build-servers -m:1 -v minimal` | 最终0警告、0错误 |
| 包内Python `-B -m unittest discover -s tools -p "test_*.py" -v` | 显式设置工具DLL和dotnet路径后，27/27通过；含6项临时假引擎CLI边界测试，不加载模型 |
| `dotnet tools/VoiceLite.WorkflowProbe/bin/Debug/net8.0-windows/VoiceLite.WorkflowProbe.dll "dist/VoiceLite 离线验证 0.1.0/engine" .artifacts/smoke/reference.wav .artifacts/workflow-20261103/real-verified` | 最终5项真实NVIDIA场景通过，工具正常退出 |

完整命令输出、TRX及失败/通过记录在 `.artifacts/workflow-20261103/`，最终真实报告为 `real-verified/workflow-report.json`。工具及复现说明见 [WorkflowProbe](../tools/VoiceLite.WorkflowProbe/README.md)。

开发过程保留的失败记录：最初工具继承Application初始化时发生WPF资源身份错误，尚未启动模型；改为独立测试Application读取产品样式后运行通过。只读审查发现工具输入路径隔离边界，临时假引擎上的末尾斜杠/junction回归先2项失败，扩展路径别名回归也先2项失败；目录句柄规范化和链接祖先检查修复后6项边界回归及全部27项工具测试通过。最终复审无剩余Important问题。失败发生在测试工具或临时假引擎中，没有据此修改产品的推理/取消实现。

## 0.1.2 家里 NVIDIA 回归 — 2026-11-03

代码基线：`675ae97`（Fix migration and add CPU device selection）。执行 `git fetch origin` 后，`HEAD...origin/main` 为 `0 / 0`，开始验证时工作区干净。设备为 Windows 11 x64、RTX 4070 Laptop 8188 MiB、驱动 610.74、约32GB系统内存；这是现有开发机，不是干净 Windows。

本次使用项目内 SDK 8.0.427、本地 NuGet 缓存和已有离线包的 `engine`，没有下载模型或修改上游源码。命令在项目根目录执行，设置 `DOTNET_ROOT=.tools/dotnet`、`DOTNET_CLI_HOME=.tools/cli-home`、`NUGET_PACKAGES=.tools/nuget`；FFmpeg 测试通过 `VOICELITE_TEST_ENGINE_ROOT` 指向现有包内引擎。

| 实际命令 | 结果 |
|---|---|
| `.tools/dotnet/dotnet.exe restore VoiceLite.sln --locked-mode --source .tools/nuget --disable-parallel` | 5个项目还原成功，锁文件未改变 |
| `.tools/dotnet/dotnet.exe build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal` | 0警告、0错误 |
| `.tools/dotnet/dotnet.exe test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=home-012.trx" --results-directory .artifacts/home-012-20261103/tests -v minimal` | 59/59通过、0跳过，包含真实FFmpeg转换 |
| 包内 `runtime/python.exe -B -m unittest discover -s tools -p "test_*.py" -v` | 21/21通过 |
| `.tools/dotnet/dotnet.exe publish src/VoiceLite.Desktop/VoiceLite.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o .artifacts/home-012-20261103/client` | 0.1.2自包含客户端发布成功 |
| `tools/validate-offline.ps1 -PackageRoot .artifacts/home-012-20261103/client -EngineRoot "dist/VoiceLite 离线验证 0.1.0/engine" -ReferencePath .artifacts/smoke/reference.wav -DataRoot .artifacts/home-012-20261103/nvidia-ui -Device nvidia -TimeoutSeconds 420` | 真实NVIDIA生成、WAV导出、实际播放完成，进程正常退出 |

最后一项使用新发布的客户端和显式指定的既有包内引擎，清理了 Python/.NET 路径依赖并使用新数据/缓存目录；它验证新版客户端的真实闭环，不代替新完整包在干净 Windows 上的断网验收。参考录音为此前 Windows SAPI 生成的测试素材，参考文字从其 JSON 旁文件读取，没有使用用户角色录音。

结果：`mock=false`、实际设备 `NVIDIA CUDA`、客户端 `0.1.2`；生成 **5.42秒、32kHz WAV**，导出后播放至完成，全流程 **106.077秒**。日志记录模型加载70.862秒、推理14.987秒。约1秒间隔的采样中，进程树 WorkingSet 峰值4268.07 MiB、Python WorkingSet 峰值4116.53 MiB、整板显存峰值4507 MiB；这些不是精确峰值或最低配置要求，整板显存包含其他应用，进程树 WorkingSet 可能重复计算共享页。

原始日志、TRX、截图及音频保存在忽略目录 `.artifacts/home-012-20261103/`。CPU真实合成本次未重新执行，继续保持公司记录中的未验收状态。

### 家里日常运行目录升级

发现现有 `dist/VoiceLite 离线验证 0.1.0/VoiceLite.exe` 仍为0.1.1.0。已核对带回的 `dist/VoiceLite-0.1.2-client-update.zip` 与公司记录SHA256一致（`15d3400cd1ce3e0436abb960ab1ab601097095786f9ad5ef374b385664c143d3`），解压后依次执行：

1. 包内Python运行 `tools/apply-client-update.py dist/VoiceLite-0.1.2-client-update "dist/VoiceLite 离线验证 0.1.0" --check`：13个payload校验通过。
2. 同一脚本使用 `--backup dist/VoiceLite-0.1.2-home-backup-20261103`：备份并应用成功，EXE文件版本为0.1.2.0。
3. 包内Python运行 `tools/verify-package.py "dist/VoiceLite 离线验证 0.1.0"`：**40621个文件、11519682736字节**全部SHA校验通过。
4. 实际运行更新后的EXE，参数 `--smoke-ui --mock --data-dir .artifacts/home-012-20261103/packaged-ui-mock`：0.1.2启动、2秒提示音生成、导出及实际播放通过。此项为Mock；真实NVIDIA结果来自上面独立发布客户端的验证。
5. 验证结束后，按专属数据目录和本次真实引擎PID核对，残留验证进程为0。

本次没有重建或覆盖8.50GB基包ZIP。日常运行目录仍保留原名称；回滚备份保留在 `dist/VoiceLite-0.1.2-home-backup-20261103`。相关原始证据为本次目录中的 `update-check.log`、`update-apply.log`、`updated-package-verify.log` 和 `packaged-ui-mock/ui-smoke.json`。

## 0.1.2 公司电脑迁移验证 — 2026-11-03

设备：Windows 11 x64、Intel Core Ultra 5 225H、约32GB内存、Intel Arc 130T，无 NVIDIA。

- 全新 `obj`、固定 SDK 8.0.427、本地 NuGet 包源：整个 solution `restore --locked-mode` 通过，5个锁文件未改变，无依赖下载。
- 完整构建通过：0警告、0错误；自包含 win-x64 Release 发布通过。
- C# 59/59通过，0跳过，包含设备选择、旧设置兼容、CPU精度/超时、取消、环境检查并发和真实FFmpeg OGG转换。
- Python工具21/21通过，包含累积更新、损坏/不匹配拒绝、失败恢复、手动回滚及Windows长路径验证。
- 0.1.2 WPF Mock生成、导出及实际播放通过：2秒、32kHz WAV。此项不代表真实声音克隆。
- 已更新的实际运行目录自动定位engine，完成7.5755秒OGG转换/实际播放，原始录音保持；生成部分为Mock。
- 本机显式NVIDIA模式按预期明确报告CUDA不可用，没有静默转CPU。
- 实际完成旧基包直接升级、回滚、再次升级；主备份 `dist/VoiceLite-0.1.2-backup` 可恢复旧客户端。
- 更新前40619文件/11519655164字节、更新后40621文件/11519682736字节的完整SHA校验通过；40139条引擎文件的路径、长度、SHA集合完全一致。原始8503049681字节ZIP保留。
- 新更新ZIP为2179813字节，包含14个条目，13个安装payload哈希逐项匹配，不含engine/模型。SHA256：`15d3400cd1ce3e0436abb960ab1ab601097095786f9ad5ef374b385664c143d3`。
- CPU真实合成未完成：模型加载期间系统可提交内存余量降至512MiB以下，开发验证脚本停止并回收进程。用户随后明确要求先完成修复，CPU实测稍后；未生成真实CPU结果，不报告速度或最低内存要求。
- CPU使用FP32，加载上限5分钟、单次生成上限10分钟，模型进程最多4个OMP/MKL线程；这些设置不代表性能已经验收。
- NVIDIA真实合成回归未执行，本机无对应显卡；历史NVIDIA结果保留在下文。
- 真实CPU验收后续命令：`tools/validate-offline.ps1 -PackageRoot <完整包目录> -ReferencePath <3-10秒参考.wav> -DataRoot <新的空目录> -Device cpu`。脚本采集进程树/Python WorkingSet采样值，默认总时限1000秒，系统提交余量低于512MiB时终止验证。开发时可通过 `-EngineRoot` 指定已有引擎，不必再复制模型。

以下为0.1.1/0.1.0历史验证记录，不能作为0.1.2真实CPU已通过的证据。

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

1. 独立 Windows 11 + NVIDIA 测试机的干净用户/断网首次启动验收，按用户2026-11-03的要求后置；当前先完成本机使用，不因缺少另一台设备暂停。此项仍未执行。
2. 补齐原整合包 commit/来源及每个模型、G2PW、CUDA/cuDNN、加速模块、FFmpeg/ffprobe 与全部 Python 依赖的再分发记录。公开打包门禁保持关闭。
3. 英文实际推理、CPU、其它 GPU/驱动与最低显存矩阵尚未执行。本机真实NVIDIA请求中取消、取消/异常后重试和窗口关闭回收，已通过顶部新增的工作流验收；不再将这些仅记为辅助进程测试。
4. Minor：运行时 users.pth 中有未启用的开发路径（实际迁移 sys.path 不依赖它）；超大无 Content-Length 响应、运行中的 FFmpeg 取消尚缺专项测试；OpenAPI 应进一步使用有界流读取。详见代码审查。

最终包文件数、整体校验与 ZIP 大小/哈希另记录于 `OFFLINE_PACKAGE.md`，避免将早期包与最终包混淆。
