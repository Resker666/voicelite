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
