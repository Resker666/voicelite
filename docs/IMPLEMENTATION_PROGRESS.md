# Implementation progress — 2026-11-02

Plan: docs/superpowers/plans/2026-11-02-voicelite.md

- 用户已授权按完整离线包方向开始实施，不再次请求方案确认。
- Ruling: 在用户指定当前目录实施；目录尚未初始化 Git，不创建另一 worktree，不自动提交/推送。
- Ruling: 使用 inline 实现，末尾独立代码审查；现有整合包只作为引擎与本地资源来源。
- Pre-flight: Task 1 的 API/模型配置约束 Task 2；Task 2 接口供 Task 3；Task 4 复制已验证组合，避免隐式依赖系统环境。
- Baseline: 系统 dotnet 只有 6.0.5 运行时，无 SDK；包内 Python 3.12.10、Torch 2.7.1+cu118、CUDA 可用，GPU 为 RTX 4070 Laptop 8188 MiB。

- SDK: 官方 8.0.427 在 .tools/dotnet，来源和 SHA-512 在 .tools/sdk-source.json；NuGet 锁文件已生成。
- Task 1: 原目录真实 API 合成通过，报告 .artifacts/smoke/real-report.json；Windows SoundPlayer 播放完成。
- Task 2/3: 分层服务、ViewModel、WPF GUI 已实现。初始 11→20→28 项 C# 测试经过 RED/GREEN；GUI Mock 和原目录真实模型均生成、保存、播放完成。
- Task 4: 6 项 Python 资源选择/离线网络约束测试通过。初始候选 40130 个引擎文件、10574271796 字节，初始 ZIP 7690920256 字节。候选已移动到 dist/VoiceLite 离线验证 0.1.0，尚需刷新最终 ZIP。
- Final review: /root/final_code_review（gpt-6-astra）只读审查。3 Important：35 秒播放截断、Start→Job 竞争窗口、覆盖参考录音。
- Final fixed: 新增 5 项回归先失败，修复为创建 suspended 进程→加入 Job→Resume；只对播放打开设超时；导出保护当前参考路径；修复坏 FFmpeg 启动错误。完整 C# 33/33 通过（.artifacts/tests/review-green.trx）。
- Long playback: .artifacts/ui-long-playback/ui-smoke.json 通过，实际 WPF 播放 41 秒静音 WAV 至 MediaEnded，耗时 42.036 秒。
- Ruling: 保留公开发行门禁；许可、来源与干净机器离线验收不冒充通过。
- Deferred Minor: runtime/users.pth 存在旧开发路径但未启用 site，迁移后实际 sys.path 都位于新包；超大无 Content-Length HTTP、运行中 FFmpeg 取消仍缺专门测试；OpenAPI 默认缓冲值得后续限制。
- Blocker under investigation: 迁移包新缓存首次启动可 ready，但 /tts 超时180秒（.artifacts/离线 首次启动）。原目录 GUI 通过。正运行 tools/engine-smoke.py --runner tools/engine_runner.py --clean-env，输出 .artifacts/wrapper-source，session 78483，以隔离包装层/环境与资源差异。诊断 flag VOICELITE_DIAGNOSTIC_STACKS 只在该开发脚本启用。

- Resolved: 原目录带包装层/新缓存通过；包内诊断栈定位 g2pw download。此前 G2PW=0 可关闭的判断错误，Chinese2 写死启用；继承 loopback 代理导致限制被绕行。部分下载 ZIP 411648000 bytes 已删除，未使用。
- Offline fixes: 清单与探测器要求完整本地 G2PW；所选文件集合必须包含 required_files；清除代理并 NO_PROXY=*；设置本包 bert_path。2 项回归先失败后通过，总 Python 8/8。
- Final build/test: 0 warnings/errors，C# 34/34（显式环境变量启用本机 FFmpeg 集成），Python 8/8；.artifacts/build-final.log、tests/final.trx、python-final.log。
- Final real UI: .artifacts/离线 最终验证/ui-smoke.json passed，WAV5.26s@32k mono，播放完成，全流程33.1288s，整板1Hz显存峰值4962MiB；全部本应用进程已退出。
- SDK bootstrap 已固定读取 global.json=8.0.427，未浮动到其它 SDK；GPU/驱动前置诊断已接入。
- Git: 用户目标下初始化本地 main，无提交、无远程、无推送；.tools/.artifacts/dist 与音频均忽略。
- Pending handoff only: 更新最终资源/整体manifest、验证哈希，刷新最终 ZIP；写 OFFLINE_PACKAGE 与 CHANGES 清单。许可与干净机验收继续明确阻塞公开发行，不影响当前本机可运行候选。

- Asset reconciliation: 新增 fast_langdetect/lid.176.bin 为必需（131266198 bytes）；它曾在首轮测试自动补齐，和原目录本地哈希7e69ec...匹配。最终明确从源复制，profile包含它；删除手动复制带来的 G2PW字节码。
- Coverage: tools/check-package-coverage.py 已确认40139expected=actual，missing/unexpected均空，避免被先前下载掩盖资源缺失。
- Final release verification: 重建/34tests/publish后，.artifacts/离线 打包复核/ui-smoke.json passed，5.14s@32000 mono，播放完成，34.466秒，board sampled peak4898MiB。最后布局模拟验证也通过，参考语言完整可见。
- Packaging in progress: 最终 coverage→manifest→fullhash→archive 串行 pipeline，exec session见当前工具结果；源目录代码不再修改。下一步读取 .artifacts/archive-final.log 和 dist/VoiceLite-0.1.0-win-x64-offline-local.zip.metadata.json，删除已被取代的旧ZIP（仅旧构建产物），完成OFFLINE_PACKAGE/CHANGES/计划复选。

- Packaging final: pipeline session89738 exit0。Engine40139files/11342914956bytes；whole manifest40619files/11519655164bytes，所有hash通过；ZIP所有清单条目长度匹配。
- ZIP: dist/VoiceLite-0.1.0-win-x64-offline-local.zip，8503049681bytes，SHA256=984865d52d683691a2be2ceef343c11338beb37df8f4bfbba6834de1382184d4。
- Handoff docs: README、UPSTREAM_AUDIT、TEST_REPORT、CODE_REVIEW、OFFLINE_PACKAGE 与 CHANGES 均已落盘。实现计划验证步骤勾选，公开发行与干净机验收继续明确未完成。
- Final handoff: 旧初始ZIP已删除，最终ZIP与.sha256/.metadata.json存在；生产代码无NotImplemented桩；最后只读核对无本应用残留进程。源码文件清单72项。保留当前main工作目录，未提交/推送，后续可由用户继续审阅与发布源码。

## OGG follow-up

- 用户E:/ai/voice/hutao的39段OGG元数据均可读，20段在3–10s；客户端三处白名单只支持WAV/MP3是导入阻塞根因。
- 已统一扩展策略WAV/MP3/OGG（大小写均支持），Vorbis/Opus自动转PCM16 WAV；独立预览转换不使用推理时长限制。
- 独立审查2个P2（关闭竞态、预览误套用推理限制）经4项RED→GREEN回归解决；全C#49/49、Python9/9。实际OGG6.28s/19.05s播放结束，原文件hash不变、previews空。
- 用户确认已关闭旧窗口，原启动目录已应用0.1.1（目录名仍0.1.0）。8个客户端文件更新、模型未变。小补丁ZIP约2.18MB；8.50GB0.1.0基包保留，文档明确叠加补丁。
- 补丁打包发现Path.with_suffix误截版本号后缀，新增回归先失败，再改为完整目录名追加.zip；实际ZIP重命名并保持原内容SHA256。

## 0.1.2 home revalidation — 2026-11-03

- 用户在公司修复并推送后回到家里。家里工作区基线为675ae97；fetch后与origin/main一致，无需合并或覆盖工作区。
- 公司变更包含迁移时locked restore、自动/NVIDIA/CPU设备选择、累积更新/备份回滚和Windows长路径校验。家里本地依赖还原、构建0警告0错误、C#59/59及Python21/21复核通过。
- 独立发布0.1.2自包含客户端，配合原离线包engine执行显式NVIDIA真实生成/导出/播放：5.42秒32kHz WAV，全流程106.077秒。参考录音为原SAPI测试素材。不是干净Windows验收。
- 已核对公司带回0.1.2更新ZIP的SHA，备份并更新家里原日常目录到0.1.2.0，目录名仍为“VoiceLite 离线验证 0.1.0”；备份为dist/VoiceLite-0.1.2-home-backup-20261103。全包40621文件SHA及更新后Mock启动/导出/播放通过；残留验证进程0。
- 本次原始证据在.artifacts/home-012-20261103；公开摘要在TEST_REPORT与OFFLINE_PACKAGE。源码基线未修改，仅补充验证文档，未自动提交或推送。
- 下一步优先补齐首版离线交付验收：真实推理取消与异常恢复、独立Windows断网首次运行、第三方再分发清单。CPU实际合成因公司内存保护停止后按用户要求延期，仍未验收；不把设备选择功能通过当作CPU性能已通过。

## 本机工作流验收 — 2026-11-03

- 用户明确将独立电脑/断网首次启动验收后置，当前先把自己电脑上的流程做好；不得因此暂停本机迭代或要求用户准备另一台设备。
- 添加手动运行的tools/VoiceLite.WorkflowProbe，直接复用现有WPF窗口、ViewModel、真实引擎和HTTP客户端。覆盖请求中取消、取消后重试、测试引擎进程异常退出、异常后重试、实际窗口关闭路径。
- 验证工具的第一轮WPF资源初始化失败发生在启动模型之前；改为独立测试Application读取产品样式，避免进入用户默认设置/启动流程。没有修改产品启动代码。
- 只读审查发现验证工具的路径隔离有末尾斜杠/junction/Windows设备路径别名边界；在临时假引擎上分别复现失败后，使用目录句柄规范化并拒绝链接祖先。27项Python测试（含6项显式启用的CLI路径边界测试）通过，审查无剩余Important问题。
- 严格关闭断言要求无错误且确实进入取消状态。真实模型验收原始证据在.artifacts/workflow-20261103，各次运行保持独立目录；最终结果见TEST_REPORT。
- 产品代码仍为0.1.2，本轮没有修改推理算法、用户音频、已有Python环境或模型。CPU真实合成继续延期；当前范围只验证这台电脑的NVIDIA流程。

## 音色对照实验 — 2026-11-03

- 用户在当前真实模型测试中认为声音不像；确认日志为real/NVIDIA，通用v2ProPlus预训练模型，未加载胡桃专用微调权重。
- 只读音频分析显示原参考约9.77秒、生成音高中位数较低；这是诊断线索，不是相似度评分。按句间停顿制作3.03秒第一句参考候选，用户反馈更像一些。
- 用户确认关闭日常VoiceLite后运行单引擎参数对照。复用WorkflowProbe增加--quality模式；共享原路径隔离检查、受控进程与HTTP客户端，不修改模型或日常设置。
- TtsRequest新增seed/temperature默认-1/1（与已锁定API相同）。两项回归先RED再GREEN；当前C#61/61，Python28/28，构建0警告0错误。
- 第一批冷/热重复有波形差异，保留证据；增加同输入预热后重新生成。最终A温度1.0与重复控制逐字节一致，B0.8和C0.6均输出有效WAV。样本在.artifacts/quality-comparison-20261103/samples-warm。
- 已向用户展示A/B/C并询问哪个更接近期望音色与语气。当前等待听感选择，未自动推荐温度、未宣称音色相似度已通过，GUI也尚未新增参数控件。
- 首次听选反馈A/C差不多、B预览无声。B实际PCM/音量正常，已验证解码并提供短文件名WAV及MP3备用试听；等待确认播放与听感，不把未核实的播放问题当成推理参数问题。

## 0.1.3 多参考GUI — 2026-11-03

- 用户确认B备用可播放，A/B/C听感接近，温度保留1.0。核对锁定TTS.py和models.py确实支持aux_ref_audio_paths并平均融合每段参考的声线条件；用同一主参考/文案/种子做单参考和主+两条辅助对照，用户明确选择D更像。
- 辅助为用户本地About_the_Vision（6.28秒）与Good_Morning（4.096秒），无需额外参考文字；这些音频仅用于私人实验，没有入包或Git。
- 已接入GUI添加/试听/移除、最多3条、去重、主参考变化清空、旧设置兼容、保存恢复；GenerationService实际导入全部参考并传给API，逐条尝试清理。源版本更新到0.1.3，依赖版本未变，项目引用锁文件已本地更新并locked restore通过。
- 当前C#72/72、Python30/30。CLI显式辅助参数曾被软校验跳过，实际Mock CLI回归先RED后GREEN，已改为启动前拒绝；ComparisonCleanup锁定文件/缺失哈希两项回归先RED后GREEN，逐条独立处理。复审无剩余Important。
- 0.1.3真实WPF生成/导出/实际播放通过：auxiliary_reference_count=2，4.66秒32kHz WAV，41.261秒全流程；新数据目录重启Mock恢复两条辅助选择通过。
- 更新包首次预检查因installed engine_runner.py为CRLF而原基包/当前源为LF而拒绝（未修改日常目录）。修复累积补丁始终包含受管VoiceLite.*及包装脚本，新增回归通过。失败候选已保留于.artifacts/voicelite-013-20261103/rejected-first-update。
- 最终更新包dist/VoiceLite-0.1.3-client-update.zip：2193475字节，SHA256=04c5abb6c04339e70da36da379f8f75281819998da766a33331610327610e224，14个payload。
- 日常目录已备份并应用到0.1.3.0；备份dist/VoiceLite-0.1.3-home-backup-20261103。pipeline74059 exit0，40621文件/11519703265字节完整SHA通过。安装后Mock恢复两条辅助/播放通过；已打开真实日常GUI128596并预选D组合，主参考永久副本在%LOCALAPPDATA%/VoiceLite/voice-presets，未自动生成。
- 最终代码72项C#、30项Python测试通过，坏CLI与累积补丁新增回归均经历RED→GREEN；只读审查无剩余Important。真实GUI证据及安装记录在.artifacts/voicelite-013-20261103。仍未提交/推送，本机范围之外验收后置。

## 真实角色微调与续训试验 — 2026-11-04

- 用户授权先做小规模真实试验，再依据 D/E/F 听感决定 GUI 接入。基线 beca7fb，客户端保持 0.1.3，本轮没有产品功能改动或自动提交/推送。
- 39 段原录音中留出 4 段，29 条离线 Paraformer 草稿筛掉 11 条明显可疑/唱段，最终 18 段共 115.936 秒。文字尚未逐字人工审核。上游 loader 重复到 90 条不代表 90 段独立素材。
- 复制 222 个上游源码/配置，既有模型和运行时通过 junction 复用；目录链接不等同只读权限。使用已有离线依赖，未安装/下载模型；原 39 段录音、4 个底模和 222 个源码/配置的已记录 SHA 最终全部不变。
- 原四项预处理匹配 18/18；真实 SoVITS 训练 8 轮、GPT 训练 15 轮，随后从完整检查点恢复到 9/16 轮，优化器状态保留。GPT 实际 global_step 165→176（上游手动优化器步数）。证明了续训能力，尚未测试追加新素材的收益。
- 私有 launcher 适配单 GPU Lightning/sampler 与自建 checkpoint 的 WindowsPath；原算法及上游文件未改。准备阶段型号配置、检查点目录、DDP 单卡和恢复类型等失败均保留日志，修正后完成，不隐藏初次失败。
- D 原底模、E 仅 SoVITS 微调、F 两组微调，固定三参考/文案/seed/temperature，分别预热后生成三句和一次重复，共 12 段真实 WAV；解码、PCM16、非静音及各组重复 SHA 一致通过。音量接近的聊天试听副本另存，不改原 WAV。用户随后反馈“E更像”，E 成为本轮首选；这是该次试听的主观选择，其他文案发音/怪音尚未验收。
- 导出私有实验角色包 dist/VoiceLite-hutao-experimental-20261104.zip，304633151 字节；SHA256=ff4cb93863cb724785d28f84f81109b14c5696644ac3ac3604b06e6df11503fe。含 F 候选两组推理权重、三参考、说明和实验 manifest，不含完整训练检查点。
- 听选后另行导出 E 首选包 dist/VoiceLite-hutao-E-experimental-20261104.zip，304630055 字节，SHA256=f03c25f373daf093e89488390c17d2e1b78c9c26cc67f805f3f2199140c481ae。原 GPT + 微调 SoVITS，五个 payload 与试听条件 SHA 一致、ZIP CRC 通过；原 F 包保留未改。listener-preference.json、selected-e-export.json 保存反馈和验证记录，未重新训练。
- 当前 GUI 不能导入角色包，未发布通用训练功能；脚本、音频和权重在忽略目录 .artifacts/character-training-20261104 与 dist。详见 CHARACTER_TRAINING、TEST_REPORT。上一轮 72 项 C# / 30 项 Python 是 11-03 基线，本轮不冒充重新执行。

## 0.1.4 角色包 GUI — 2026-11-04

- 用户在选 E 后明确要求执行下一步。新增 Core角色契约、Infrastructure角色库及启动权重选项、MVVM角色选择/导入、WPF入口和设置恢复。角色参考自动填充且可试听，自定义参考保留并可切回；切换保留合成文字、清除旧结果选择。
- 仅支持已锁定v2ProPlus实验ZIP，检查清单/大小/SHA/相对路径/别名/链接，私有staging原子安装、取消清理，生成前重验。两组权重通过已有启动配置选择，选项变化回收旧Job再启动，不增加未核实API。
- 功能先RED再GREEN。独立复审发现并复现非整数bytes的异常逃逸，TryGetInt64修复后无剩余Important。最终构建0警告0错误，C#105/105、Python30/30均0跳过。
- E包新目录真实GUI导入/生成/导出/播放通过，4.90秒32kHz；最终客户端重启恢复角色4.46秒、切自定义底模5.06秒、返回角色Mock2秒及5组坏CLI拒绝均通过。证据在.artifacts/roles-014-20261104。
- 累积客户端更新dist/VoiceLite-0.1.4-client-update.zip，2213533字节，SHA256=5179589bb1e901ae66e425c8fd6e4170d99291c5b547a5aefba7a55501c03585，14个payload；日常安装已备份并应用，备份dist/VoiceLite-0.1.4-home-backup-20261104。私人E包/音频/完整检查点仍在Git忽略目录，未自动提交/推送。
- 日常完整包40621文件/11519749847字节SHA通过，EXE0.1.4.0。通过computer-use实际点击导入E角色到用户数据目录，关闭/重开验证自动恢复，保留原自定义参考与两辅助；现在窗口已打开、E已选中，未自动生成。独立电脑断网和CPU真实验收仍后置。
