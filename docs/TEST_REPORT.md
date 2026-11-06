# VoiceLite 验证报告

## 0.1.6 真实训练、角色导出与GUI — 2026-11-05

沿用859691e之后尚未提交的0.1.5实现。用户“下一步继续做”时，本机素材集 `0ec386011cd743fd8886a136a2f62209` 已由用户标记18/18校对、revision3；本轮只读导出，未自动勾选。规范化副本约116.085秒，标记来自用户操作，不是独立质量认证。

下表 `$p` 为 `E:\develop\github\voicelite\.artifacts\training-016-20261105`，`$sdk` 为工作区 `.tools\dotnet\dotnet.exe`8.0.427，`$py` 为 `E:\ai\voice\GPT-SoVITS-v2pro-20261006\runtime\python.exe`，`$engine` 为该原完整包，`$daily` 为工作区 `dist\VoiceLite 离线验证 0.1.0`。SDK使用项目内DOTNET_ROOT/CLI_HOME/NuGet缓存，FFmpeg测试设 `VOICELITE_TEST_ENGINE_ROOT=$engine`。

| 实际命令或操作 | 实际结果与证据 |
|---|---|
| 私有 `$sdk run --project $p/probe/Probe.csproj --no-restore -- $p/exports $p/export-report.json` | 18条已校对副本导出，用户源清单SHA `ec697b1ee5a6761187fec387b4c703fc5aef38f0f650f5b9c7c5f24fff30f8bd`前后不变。导出目录 `VoiceLite-dataset-0ec38601-9c3d1dec`，`export-report.json` |
| `$py -B -u $p/run_real_cli.py`，实际依次调用 `tools/training_cli.py validate` / `init` / `features` / `sovits --epochs 8` | 四阶段exit0，0.250/3.734/69.157/**222.672秒**。原四项特征匹配18条；真实8轮生成推理权重和完整G/D。`logs`、`real-work/commands.jsonl`记录完整argv/退出码 |
| 上述编排的 `sovits --epochs 9 --resume` | 训练子进程exit0、67.203秒，G/D从8恢复且保存9轮，`VOICELITE_VERIFIED_CHECKPOINTS 9`；但CLI旧判断依赖上游不存在的“loaded G”，**顶层exit1误报**。`real-cli-report.json`保留passed=false，未改为成功 |
| 新增实际日志格式回归先RED，修复 `verify_resume_log` 后 `$py -B $p/recover_resume.py` | 严格核对真实G/D Loaded checkpoint行、iteration8及目标9标记，先备份旧run.json，再凭证据核对元数据至9（未改权重）；正式CLI **9→10轮完整续训exit0，67.718秒**。`resume-recovery-report.json` passed=true；G/D/优化器与前轮备份保留 |
| `$py -B -m unittest discover -s tools -p test_training_export.py -v`；C# TrainingService/VM过滤测试 | 新功能5→6项Python、服务5项/VM4项均RED→GREEN；坏null参考/过大run/历史顺序3项先RED后GREEN。最终训练相关过滤14/14；动态数组Count是测试反射问题，改正式接口类型后通过 |
| `$sdk restore VoiceLite.sln --source .tools/nuget --disable-parallel --force-evaluate` 后locked-mode；WorkflowProbe同样恢复 | exit0，版本0.1.6，未新增依赖、未联网下载，`restore-version.log` |
| `$sdk build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal`；同样build WorkflowProbe | **0警告、0错误**，`build-full.log`、`probe-build.log` |
| `$sdk test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=full-first.trx" --results-directory $p/tests` | **137/137通过，0跳过**，真实FFmpeg启用；`tests/full-first.trx` |
| 设置WorkflowProbe SDK/DLL环境变量后 `$py -B -m unittest discover -s tools -p "test_*.py" -v` | **52/52通过，0跳过**，15.193秒，`python-full-first.log` |
| `$sdk publish src/VoiceLite.Desktop/VoiceLite.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o $p/client` | 首次及最终exit0，训练CLI/进程/导出脚本与profile随发布；`publish-first.log`、`publish-final.log`。最终加入清单边界、任务排序与日志按钮；模型算法未改 |
| `$py -B $p/run_gui.py`，实际WPF `--smoke-training --data-dir $p/ui-real --engine $daily/engine --device nvidia --training-engine $engine --training-dataset <上述ID>` | **真实独立GUI8轮→选参考→ZIP保存通过，292.4649秒**；随后现有RoleLibrary导入并真实合成/导出/完整播放 **4.66秒@32kHz**，mock=false，103.1253秒。整体400.093秒，`gui-report.json`、`ui-real/training-smoke.json`、`ui-smoke.json`；截图已检查 |
| 私有probe `--cancel $p/cancel-real <已校对导出> $engine` | 最终服务真实启动初始化/特征后12秒取消，Cancelled记录与输入保留，未发现模型残留；`cancel-real/cancel-report.json`。这是实际进程取消，不将VM假服务测试冒充模型取消 |
| 独立只读 whole-feature 复审 | 无Critical/Important；不代替模型/GUI验收，不裁定音色。`CODE_REVIEW.md` |
| 私有Compare工具（引用当前Core/Infrastructure）执行 `$p/comparison-plan.json` | 原E、新8、新10共**12段**（3句+首句重复），固定原E主/两辅助、seed20261103、temperature1，逐组预热并关闭，输入SHA不变，三组重复SHA均一致；`comparison/comparison-report.json` passed=true |
| `$py -B $p/final_audit.py` | 原222源码、4底模、18原录音、旧E ZIP SHA全部不变；9条非重复WAV逐条FFmpeg解码且非静音。三份15.4秒试听副本约**-24dBFS**，峰值约-5.20/-5.18/-5.08dBFS，无削波，原WAV未改；`final-audit.json` |
| 打包说明文本更新后 `$py -B -m unittest discover -s tools -p test_client_update.py -v` | 更新子集**10/10通过**，4.352秒，`update-tests.log`；属于完整52项中的子集复核 |
| `$py -B tools/package-client-update.py $p/client .artifacts/voicelite-013-20261103/base-010-package-files.json dist/VoiceLite-0.1.6-client-update packaging/START_HERE.txt`；`apply-client-update.py ... --check` 后 `--backup dist/VoiceLite-0.1.6-home-backup-20261105` | 20安装payload，2317453字节ZIP，SHA256 `52424a8beda5af72b9515696620da391000bc3e0ccfdbcc1599016bc03f4aaa0`；校验/备份应用exit0，日常EXE0.1.6.0，`update-package/check/applied.json` |
| `$py -B tools/verify-package.py $daily` | exit0，**40627文件、11520011478字节**完整SHA通过，`package-full-sha.log`，清单版本0.1.6 |
| 将真实GUI导出的新8轮ZIP复制至dist，并用既有RoleLibrary导入本机库 | 305317457字节，SHA256 `2be06bdec331bd7176463329d888b1187835f891f74f91d37ca989e33926b7e7`；旧角色保留、默认设置SHA未改，`user-role-installed.json` |
| 正常模式窗口终检；新增 `$py -B $p/run_close.py $p/close-red-client $p/close-red` | 首次关闭TrainingWindow时清理同步完成，Closing内Close重入；真实进程exit **3762504530 (0xE0434352)**，无完成报告。Windows.NET事件明确InvalidOperationException/VerifyNotClosing及TrainingWindow.CloseCoreAsync栈，`close-red.log` |
| 三个窗口实际Close改为Dispatcher排队，`$py -B $p/run_close.py $p/client $p/close-green` | **exit0**，实际训练/素材/主窗口均一次Close后收到Closed；model_run=false，不冒充训练取消验收。`close-green/window-close-smoke.json`；原reviewer针对性确认修复，无剩余Important |
| 关闭修复后再次build solution及完整test，TRX `final.trx` | **0警告/错误，137/137通过，0跳过**，`build-final.log`、`tests-final.log`。Python代码除已复核的更新说明外未再变化，完整52项记录仍以上述实际执行为准 |
| 保存首候选和记录到 `rejected-first-update`，重新执行packager | 最终20payload，**2319556字节**，SHA256 `b8f9f24143a9bc029c3b8f3118e9588f3273de97b6faddcdecd3649fa0486ba6`。上表2317453字节/52424a…/全包40627记录对应首候选，**不是最终关闭修正版安装记录** |
| 尝试回收旧空闲验收窗口以覆盖修正；自动审批拒绝 | 强制结束操作**未执行**。拒绝理由是仅凭路径无法证明窗口归属、可能丢未保存输入；已请求用户保存关闭或明确授权。原目录修正覆盖保留待处理，未绕过拒绝 |
| 将最终发布复制到 `dist/VoiceLite-0.1.6-client`，逐文件SHA；computer-use普通模式运行 | **476文件SHA一致**，读取已保存的原engine路径；实际E仍默认、新8轮候选可选、18/18校对恢复，三个窗口各一次关闭成功、主窗口重开正常。修正版窗口保持打开、未自动训练/生成；`side-client.json` |
| 用户回复“已保存并关闭VoiceLite”后CIM复核；`apply-client-update.py <最终更新> $daily --check` 后 `--backup dist/VoiceLite-0.1.6-close-fix-backup-20261105` | 无VoiceLite/模型进程，ZIP SHA仍b8f9f…；校验20文件、备份安装各exit0。`update-close-fixed-check.json`、`update-close-fixed-applied.json`。强制结束操作从未执行 |
| `$py -B tools/verify-package.py $daily`，最终修正版单独核验 | exit0，**40627文件、11520017274字节**完整SHA通过；`package-close-fixed-full-sha.log`。这是关闭修正后的正式安装结果，与首候选记录区分 |
| `$py -B $p/run_close.py $daily $p/installed-close-fixed`；computer-use启动原目录EXE | exit0，原安装目录真实WPF三窗口首次Close/Closed均通过、无模型加载；`installed-close-fixed/window-close-smoke.json`。正式入口0.1.6正常打开，默认E/原参考恢复，未自动训练或生成 |

这次真实训练使用单RTX4070 Laptop 8188MiB。CLI初次编排约1秒采样整板显存峰值6558MiB（338次样本，含其它应用），不是精确峰值或所有素材的最低显存。模型技术成功不等于相似度提升；对照报告和导出metadata均保留quality/similarity未听选状态。新增/改标签必须新建冻结目录；GUI当前新建固定8轮任务，不自动恢复执行中断或跨数据续训。

真实失败均保留：第9轮验收误报如上，没有重写上游日志或权重；修复后另行验证9→10，而非把失败命令标成exit0。测试里的反射数组Count仅在确认测试类型原因后修正。窗口关闭异常有真实失败与事件日志，再修复、回归和普通模式验收。没有下载模型、安装依赖或覆盖既有Python；训练/ASR完整资源未打入这个客户端小更新，公开发行/干净机器/CPU TTS仍沿用已有范围。

## 0.1.5 素材管理、离线转写与训练入口整理 — 2026-11-05

基线859691e（已推送的0.1.4）。本轮在当前工作区新增独立素材窗口、受管音频副本、文字校对/保存/训练集导出和本地中文ASR，并整理通用训练CLI。没有重新训练用户角色，也没有把历史微调成功当成本轮CLI的训练验收。

以下 `$p` 为 `E:\develop\github\voicelite\.artifacts\datasets-015-20261104`，`$sdk` 为工作区 `.tools\dotnet\dotnet.exe`8.0.427，`$py` 为已有整合包 `E:\ai\voice\GPT-SoVITS-v2pro-20261006\runtime\python.exe`，`$engine` 为该整合包目录，`$daily` 为工作区 `dist\VoiceLite 离线验证 0.1.0`。日期后缀为任务开始日；完成日在11-05。SDK配置使用项目内DOTNET_ROOT、CLI_HOME及NuGet缓存；真实FFmpeg测试设置 `VOICELITE_TEST_ENGINE_ROOT=$engine`。

| 实际命令或操作 | 实际结果与证据 |
|---|---|
| `$sdk restore VoiceLite.sln --source .tools/nuget --disable-parallel --force-evaluate`，再以 `--locked-mode` restore；WorkflowProbe同样更新锁文件并locked restore | exit0，项目版本更新0.1.5，未新增NuGet依赖或联网下载 |
| 分别执行DatasetServiceTests、OfflineTranscriberTests、DatasetViewModelTests过滤测试 | 初始用例先RED后GREEN；坏清单null条目、超过2MB保存分别新增RED回归再修复。最终素材服务12项、ASR4项、ViewModel4项通过，保留各次TRX |
| `$sdk build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal` | **0警告、0错误**，`$p/build-final.log` |
| `$sdk test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=v015-final.trx" --results-directory $p/tests` | **125/125通过，0跳过**，`$p/tests/v015-final.trx`；实际使用本机FFmpeg |
| `$sdk build tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --no-restore --disable-build-servers -m:1 -v minimal`，设置probe SDK/DLL环境变量后 `$py -B -m unittest discover -s tools -p "test_*.py" -v` | **46/46通过，0跳过**，13.701秒，`$p/python-final.log`；其中Windows Job两项使用真实父/子进程，验证父进程结束后的子进程回收与超时终止，无模型加载 |
| `$sdk publish src/VoiceLite.Desktop/VoiceLite.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o $p/client` | exit0，生成0.1.5自包含WPF客户端和离线转写桥接 |
| `$sdk build $p/probe/Probe.csproj ...`，随后 `$sdk $p/probe/bin/Debug/net8.0/Probe.dll $p/asr-real $engine <3.03秒参考WAV> <Vision.ogg>` | **真实CPU ASR两段完成**，37.1667秒；原音频SHA不变、全部未人工校对、未校对导出被拒绝。`asr-real/asr-report.json` |
| 同一私有probe以 `$p/asr-cancel $engine --cancel <两参考>` 启动真实转写，2秒后取消 | 取消返回，临时ASR任务目录0；`asr-cancel/cancel-report.json` passed=true |
| `$p/client/VoiceLite.exe --smoke-dataset --data-dir $p/ui-final --engine $daily/engine --asr-engine $engine --dataset-reference <3.03秒参考> --dataset-extra <Vision.ogg>` | **实际WPF闭环通过**，两条真实CPU草稿、3.03秒完整试听、测试专用文字/校对标记保存重开、只导出1条，17.5388秒。`ui-final/dataset-smoke.json`；最终窗口截图已检查，校对复选框固定可见 |
| 上述UI命令改用 `$daily/engine` 作ASR资源，独立目录 `$p/ui-missing-asr`；由 `$py -B $p/gui_followups.py` 汇总 | **预期失败路径通过**：exit1并明确“离线中文转写资源不完整”，没有模型下载；`gui-followups.json`两种预期匹配。日常推理包确实缺ASR模型，原完整包具备资源 |
| `$py -B tools/training_cli.py validate --dataset <ui-real导出目录>`，再 `init --engine $engine --dataset <该目录> --work $p/formal-training-work-final` | exit0；真实导出验证、222项锁定源码复制、4个本地资源junction、独立配置/清单/冻结摘要生成完成，未运行GPU特征或训练。该输入的校对标记只用于自动化状态验收，**不代表人工确认内容** |
| 新训练CLI英文特征、冻结输入变更及点点路径回归；修复后完整Python套件及独立针对性复核 | 三项先RED后GREEN；英文不再要求不存在的BERT，改数据必须新建工作目录，`..`规范化后检查边界。复核关闭全部Important，详见CODE_REVIEW |
| `tools/validate-offline.ps1 -PackageRoot $p/client -EngineRoot $daily/engine -ReferencePath <3.03秒参考> -RolePackage dist/VoiceLite-hutao-E-experimental-20261104.zip -DataRoot $p/ui-tts-real -Device nvidia -TimeoutSeconds 420` | **真实E角色合成/导出/播放通过**，4.06秒、32kHz、两辅助，完整流程48.9151秒，mock=false；`ui-tts-real/ui-smoke.json`。整板显存约1秒采样峰值4284MiB，非精确峰值或最低配置结论 |
| 更新打包器说明文本后 `$py -B -m unittest discover -s tools -p test_client_update.py -v` | **10/10通过**，4.905秒，`client-update-tests.log`，是对完整46项中的更新子集复核 |
| `$py -B tools/package-client-update.py $p/client .artifacts/voicelite-013-20261103/base-010-package-files.json dist/VoiceLite-0.1.5-client-update packaging/START_HERE.txt` | exit0，15安装payload，2266925字节ZIP，SHA256 `2dcb66633bef19f1d53047e8a1d79aaa285363a034318035d89b71431f701e19`，`update-package.json` |
| `$py -B tools/apply-client-update.py dist/VoiceLite-0.1.5-client-update $daily --check`，再 `--backup dist/VoiceLite-0.1.5-home-backup-20261105` | exit0，校验与备份安装完成，日常EXE0.1.5.0；`update-check.json`、`update-applied.json` |
| `$py -B tools/verify-package.py $daily` | exit0，**40622文件、11519877733字节**完整SHA通过，`package-full-sha.log`；引擎沿用原包 |
| 私有probe新增 `--seed <旧selection.json> $daily $engine $p/user-seed` | 18段原素材副本及旧机器草稿导入用户素材库，116.0854秒、已校对0条，源SHA与原主界面设置SHA不变，未校对导出拒绝；`user-seed/seed-report.json`。该工具初次构建的局部变量作用域冲突已修正，最终构建0警告/错误 |
| computer-use：启动日常EXE，点击“训练素材”，展开资源、正常关闭后再次打开 | 确认0.1.5、E角色保留，18条待校对及原完整包资源路径恢复；未修改草稿或勾选用户校对。关闭后首次重开UI自动化遇到geometry unavailable，重新观察并激活主窗口后重试成功，非产品故障；窗口留给用户继续校对 |
| `git diff --check`、`git status --short`、`git check-ignore`及CIM/临时目录复核 | exit0，候选仅源码/配置/文档/测试；模型/录音/ZIP/SDK仍被忽略。锁定API SHA不变，无API/ASR/训练模型进程，ASR临时任务及真实TTS临时参考均0；只有交付用日常窗口保留。`verification-summary.json`记录125/46、全包校验、18条且0已校对、未训练/未提交/未推送 |

真实GUI自动化仅在新的私有测试目录设置过测试校对标记，报告明确 `human_transcripts_verified=false`。导入的是独立规范化副本，原录音不改；转写不会覆盖已有文字，也不会自动标成已校对。新CLI的 `features` / `sovits` / 恢复流程本轮**没有实际启动模型执行**，只完成输入/配置/状态/受控进程验证与真实初始化；不能将其描述为通用训练端到端通过。CPU ASR通过也不等于CPU声音合成验收。

真实执行中的修正：私有probe初版项目相对引用多一层导致构建失败，修正为 `../../../src` 后构建通过。null条目和过大JSON保存由新增失败测试复现后修复；ASR缺资源用例的非零退出是预期结果。没有把这些失败算作成功，没有更换已有Python环境或下载模型。当前完整离线基包未重新制作，中文转写需选择已有兼容完整资源；正式开发者命令和边界见TRAINING_CLI.md。

## 0.1.4 角色包 GUI — 2026-11-04

用户选 E（原 GPT + 微调 9 轮 SoVITS）后授权接入“导入角色包→选择角色→生成”。实现本机角色库、ZIP 兼容/清单/大小/SHA/路径/链接检查、生成前重验、设置恢复及自定义输入保留。引擎启动配置同时指定两组权重；角色变化通过现有 manager 结束旧 Job 并启动新进程，HTTP 继续只连接已确认拥有的 127.0.0.1。没有新增训练 GUI 或改动上游算法。

下表 SDK 使用工作区 `.tools/dotnet/dotnet.exe`8.0.427，配置项目内 CLI/NuGet 目录；`$p` 是 `.artifacts/roles-014-20261104` 的完整路径，`$py` 是已有整合包 `E:\ai\voice\GPT-SoVITS-v2pro-20261006\runtime\python.exe`。所有测试与产物均在本机执行，没有下载依赖。

| 实际执行命令/检查 | 结果 |
|---|---|
| `test tests/VoiceLite.Tests/VoiceLite.Tests.csproj --no-restore --disable-build-servers -m:1 --filter <RolePackage/Generation/ViewModel 对应测试>` | 首批缺失功能真实 RED 后实现 GREEN，TRX 在 `$p/tests`。新增 ZIP、设置、权重切换等最终共 33 项回归 |
| `restore VoiceLite.sln --force-evaluate --source .tools/nuget --disable-parallel`；`restore VoiceLite.sln --locked-mode --source .tools/nuget --disable-parallel` | 版本改为 0.1.4，只有项目引用锁版本变动；锁定还原通过 |
| `build VoiceLite.sln --no-restore --disable-build-servers -m:1 -v minimal` | 最终 **0警告、0错误**，`$p/build-final.log` |
| `test VoiceLite.sln --no-restore --no-build --disable-build-servers -m:1 --logger "trx;LogFileName=v014-final.trx" --results-directory $p/tests -v minimal` | **105/105通过，0跳过**；设置 VOICELITE_TEST_ENGINE_ROOT 执行真实 FFmpeg 集成 |
| `restore tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --force-evaluate --source .tools/nuget --disable-parallel`，随后 `build ... --no-restore --disable-build-servers -m:1 -v minimal` | 复用产品的工具重建通过，无新增依赖 |
| `$py -B -m unittest discover -s tools -p "test_*.py" -v` | 显式设置 VOICELITE_WORKFLOW_PROBE_DOTNET / DLL，最终 **30/30通过，0跳过**，`$p/python-final.log` |
| `publish src/VoiceLite.Desktop/VoiceLite.Desktop.csproj -c Release -r win-x64 --self-contained true --no-restore --disable-build-servers -m:1 -o $p/client -v minimal` | 自包含 0.1.4 发布通过 |
| `tools/validate-offline.ps1 -PackageRoot $p/client -EngineRoot "dist/VoiceLite 离线验证 0.1.0/engine" -ReferencePath .artifacts/voice-quality-20261103/hutao-name-first-sentence-3.03s.wav -RolePackage dist/VoiceLite-hutao-E-experimental-20261104.zip -DataRoot $p/ui-real -Device nvidia -TimeoutSeconds 420` | 新目录真实角色导入/生成/导出/播放通过：mock=false、0.1.4、两辅助、**4.90秒32kHz WAV**，40.147秒全流程 |
| 最终发布 EXE `--smoke-ui --data-dir $p/ui-real --device nvidia` | 无角色 CLI 参数，自动恢复同一角色 ID 与两辅助，**4.46秒 WAV**、实际播放完成，35.094秒；`restore-real.json` |
| 最终发布 EXE `--smoke-ui --data-dir $p/ui-real --role custom --device nvidia` | 恢复自定义参考，启动日志 gpt=stock/sovits=stock、role=custom，**5.06秒 WAV**、实际播放完成，35.548秒；`custom-real.json` |
| 最终发布 EXE `--smoke-ui --data-dir $p/ui-real --role <已导入ID> --mock` | 两辅助与角色恢复，明确模拟提示音 2秒；未加载真实模型 |
| 最终发布 EXE `--smoke-ui --mock --data-dir <各自新目录>` 配五组非法 `--role` / `--role-package` 参数 | 不存在角色、不存在ZIP、两个缺失值、两个参数混用均 exit非0、passed=false、results未创建；`followups.json`共8项通过 |
| `$py -B tools/package-client-update.py $p/client $p/base-010-package-files.json dist/VoiceLite-0.1.4-client-update packaging/START_HERE.txt` | 累积更新14个payload，**2213533字节**，SHA256 `5179589bb1e901ae66e425c8fd6e4170d99291c5b547a5aefba7a55501c03585`；不含角色/模型/用户录音 |
| `$py -B tools/apply-client-update.py dist/VoiceLite-0.1.4-client-update "dist/VoiceLite 离线验证 0.1.0" --check`，再加 `--backup dist/VoiceLite-0.1.4-home-backup-20261104` 应用 | 校验通过、备份后应用成功，日常目录名保持原样 |
| `$py -B tools/verify-package.py "dist/VoiceLite 离线验证 0.1.0"` | exit0，**40621文件、11519749847字节**完整SHA通过；日常EXE0.1.4.0 |
| computer-use：打开日常EXE，点击“导入角色包”，原生文件框选择E ZIP；关闭后再次打开 | 实际按钮路径导入完成，E角色ID已保存、原自定义参考/两辅助保留，重开自动恢复E，窗口保持打开等待用户输入；没有自动生成 |
| `git diff --check`、`git status --short`，检查测试模型进程和临时目录 | 差异检查通过；模型/音频/ZIP仍被忽略。最终进程与产物摘要见 `$p/verification-summary.json` |

实际失败与修正：首批测试 ZIP 的保留名 AUX.wav 和 Create 模式 GetEntry 是测试构造问题，修正后通过；私有 followups 脚本初次因 Windows 默认 GBK 读取 UTF-8 报错，模型尚未启动，显式 UTF-8 后8项完成。复审发现清单非整数/超范围 bytes 可使 FormatException 逃逸并阻断启动，两种场景先复现 RED，再改 TryGetInt64；完整105项通过。真实解压开始后的取消与已安装 references junction 拒绝也已执行。

独立只读复审最终无剩余 Critical / Important。实测 E 和自定义生成启动日志分别指向角色库权重和两组 stock；相似度沿用用户对 E 的主观选择，不扩大为所有文案验收。首轮资源采样整板显存4348MiB、进程树WorkingSet4328.62MiB，约1秒采样，不是最低配置或精确峰值。独立电脑断网首次运行、CPU真实合成继续按用户要求后置。

## 真实角色微调、续训与 API 对照 — 2026-11-04

用户授权先做一次小规模微调验证再决定界面。基线 `beca7fb`，客户端仍为 0.1.3；没有修改产品推理代码或日常模型。本节记录真实 GPU 训练，非 Mock；用户随后反馈“E更像”，已将原 GPT + 微调 SoVITS 的 E 组合设为本轮首选。机器转写尚未逐字人工校对。

下表用 `$p` 表示 `E:\develop\github\voicelite\.artifacts\character-training-20261104`，`$py` 表示 `E:\ai\voice\GPT-SoVITS-v2pro-20261006\runtime\python.exe`，`$e` 表示 `$p\engine-source\GPT_SoVITS`，`$sdk` 表示工作区 `.tools\dotnet\dotnet.exe`。这些是实际命令固定路径的缩写，不要求用户照表重新训练。每个训练/特征子进程的完整 argv、PID、退出码、时限与耗时保存在 `$p\commands.jsonl`；日志保留于 `$p\logs`。

| 实际命令或阶段 | 结果及证据 |
|---|---|
| `$py -B $p\setup_spike.py`，随后用 `New-Item -ItemType Junction` 连接已有 runtime / pretrained_models / G2PW / vendor | 记录 39 原音频、4 底模、222 上游源码/配置 SHA；源码副本和输入清单保留。普通沙箱创建链接被拒，使用本机授权环境创建成功；不是只读权限隔离 |
| `$py -B -u $p\run_stage.py $p\transcribe.py` | 已有本地 Paraformer / VAD / 标点模型离线生成 29 条机器草稿，`logs/asr.log`。筛选后 18 条、115.936 秒，人工逐字审核未完成 |
| `$py -B -u $p\run_stage.py $e\prepare_datasets\1-get-text.py` | exit 0，31.140 秒 |
| 同上启动 `2-get-hubert-wav32k.py`、`2-get-sv.py` | 各 exit 0，23.797 / 7.969 秒 |
| 同上启动 `3-get-semantic.py` | 首次 exit 1（错误使用普通 v2 配置，Plus 权重维度不符）；指定原有 `s2v2ProPlus.json` 后 exit 0，8.328 秒；`features-verified.json`：18/18 全部匹配 |
| `$py -B -u $p\run_stage.py $e\s2_train.py --config $p\configs\s2.json` | 首次 checkpoint 目录缺失，2 轮保存时 exit 1；创建与上游 WebUI 相同的日志目录后重新从底模执行，**8 轮 exit 0，225.875 秒**；`train-sovits-fixed-dir.log` |
| `$py -B -u $p\run_stage.py $e\s1_train.py --config_file $p\configs\s1.yaml` | 首次硬编码 DDP 的 wildcard rendezvous 被离线 socket 策略阻断；第一次单卡修正后 sampler 仍要求 process group。改用单设备和原 sampler 的 rank/replica 参数后，**15 轮 exit 0，72.453 秒**；`train-gpt-single-device.log` |
| `$py -B -u $p\run_stage.py $e\s2_train.py --config $p\configs\s2-resume.json` | **exit 0，66.532 秒**；实际恢复 G/D 与优化器、epoch 8→9，无 pretrained fallback |
| `$py -B -u $p\run_stage.py $e\s1_train.py --config_file $p\configs\s1-resume.yaml` | 首次 weights-only 拒绝本机自建 checkpoint 中的 WindowsPath；仅加入该标准库 safe global 后，**exit 0，22.484 秒**；恢复所有状态，15→16 轮、global_step 165→176。`resume-verified.json` 通过，数据集未变化 |
| `$sdk restore $p\compare\Compare.csproj --source .tools/nuget --disable-parallel` | exit 0，使用已有本地 NuGet 缓存，`compare-restore.log` |
| `$sdk build $p\compare\Compare.csproj --no-restore --disable-build-servers -m:1 -v minimal` | 最终 **0 警告、0 错误**，`compare-build-final.log` |
| `$py -B $p\make_comparison_plan.py`，`$sdk $p\compare\bin\Debug\net8.0\Compare.dll $p\comparison-plan.json` | exit 0；D 底模、E 仅声学微调、F 两组微调，各自预热并真实合成三句+第一句重复。共 **12 段 32kHz PCM16 WAV**，`comparison/comparison-report.json` passed=true |
| `$py -B -u $p\run_stage.py $p\finalize.py` | exit 0，12 段 FFmpeg 解码且非静音、两组权重有限且已更新、3 个完整 checkpoint 含优化器状态、39/4/222 个原文件 SHA 不变；ZIP 复制 SHA 与 CRC 通过，`final-audit.json` technical_passed=true |
| `$py -B -u $p\run_stage.py $p\make_listening_previews.py` | exit 0；三段试听 MP3 仅调整固定增益，解码 RMS -23.997 / -23.990 / -23.997 dBFS、无削波、原 WAV SHA 不变；`listening-previews.json` |
| 用户选择 E 后：`$py -B -u $p\export_selected_e.py` | exit 0；新 E 包 7 文件，ZIP CRC 和包内 5 个 payload SHA 全通过。GPT SHA=87133414860ea14ff6620c483a3db5ed07b44be42e2c3fcdad65523a729a745a，SoVITS SHA=9c4c1f1e7f36ad2e9e34ede6ab5869a4c6e301ae345288f46b98e53822dd28a2，均与试听 E 相同；原 F ZIP SHA 不变 |
| `Get-CimInstance Win32_Process` 按实验/引擎命令行核对 | 导出后未发现本轮 Python、Compare、VoiceLite 或 FFmpeg 残留进程 |
| `git diff --check`、`git status --short`，逐组检查 `comparison/runtime-*/references` | 差异检查通过；仅 4 个文档为提交候选，无模型/音频/私有脚本；三组临时参考目录均为 0 文件 |

实验编排入口还运行过 `experiment.py prepare` / `finish-prepare` / `train` / `train-gpt-only` 和 `resume_spike.py` / `resume_spike.py --gpt-only`。初次恢复入口因便携 Python `_pth` 未加入脚本目录而 import 失败，补充私有脚本路径后完成；完整失败与修正日志均保留。没有把失败标为成功，也没有停用 weights-only 或修改既有 Python 环境。

固定主参考 3.03 秒及两条辅助、中文、seed 20261103、temperature 1。D/E 的三句时长均为 5.32 / 4.16 / 5.22 秒，F 为 5.08 / 4.88 / 5.38 秒。三组各自的第一句与重复样本 SHA 一致；输入/模型 SHA 前后不变。使用已有 EngineProcessManager 和 TtsApiClient，权重切换连接前后核对进程拥有的 loopback socket；控制请求与生成有超时，各组独立进程，结束关闭 Job 并逐项清理临时参考。

产物 `dist/VoiceLite-hutao-experimental-20261104.zip` 为 **304633151 字节**，SHA256 `ff4cb93863cb724785d28f84f81109b14c5696644ac3ac3604b06e6df11503fe`。7 个文件包含 F 候选两组推理权重、三参考、实验 metadata 与说明，不含训练集或完整恢复检查点。当前 0.1.3 GUI 不支持角色包导入；没有公开发布或推送模型/音频。

听选后的新首选包为 `dist/VoiceLite-hutao-E-experimental-20261104.zip`，**304630055 字节**，SHA256 `f03c25f373daf093e89488390c17d2e1b78c9c26cc67f805f3f2199140c481ae`；原 GPT 底模 + 微调 9 轮 SoVITS，不含本次微调的 GPT。`listener-preference.json` 记录用户原话“E更像”，`selected-e-export.json` 记录导出和完整性结果；初次 F 包及技术报告保留。

本轮新构建的是引用产品 Core/Infrastructure 的私有对照工具；**未重新执行上一轮 72 项 C# / 30 项 Python 测试**，不能将 11-03 的记录当成本次执行。真实训练/恢复/推理/导出检查如上。E 已获该次试听的主观选择；人工转写审核、更多文案发音/怪音、追加语料收益、GUI 导入均未验收。听选后的工作仅修改私有导出和文档，没有重新训练或修改产品代码；完整训练记录见 `CHARACTER_TRAINING.md`。

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
