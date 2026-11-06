# Independent code review — 2026-11-02

## 0.1.6 训练闭环复审 — 2026-11-05

按executing-plans / requesting-code-review，独立只读reviewer检查本轮训练服务/VM/WPF/CLI/导出、既有角色兼容及对应测试；未编辑文件、运行模型或再派reviewer。结论：**无具体Critical / Important，无需复审驱动修复轮次**。

审查覆盖独立冻结目录、推理释放与模态窗口、取消/关闭等待、坏任务隔离、权重摘要、兼容ZIP和实际上游续训日志格式。作者先用失败回归修复null参考数组、超大run清单、按创建时间恢复记录；针对性14项训练相关测试与完整137项C#通过。续训日志误报有独立失败回归，6项Python训练导出测试及完整52项通过。

明确未由静态审查裁定：音色/发音质量（需听选）、真实GPU/最终发布包运行（由主流程另验）、此前0.1.5功能及用户后置的公开发行门禁。主流程已完成真实模型和GUI验证，详情见TEST_REPORT。没有把静态无问题扩大为质量或所有硬件保证。

后续主流程正常模式终检发现空闲TrainingWindow清理同步完成时在Closing内重入Close，触发WPF VerifyNotClosing。新增真实WPF单次Close/Closed事件回归先RED（CLR异常退出），三个窗口均改为Dispatcher.InvokeAsync排队实际Close后GREEN。原reviewer只针对这项修复复核，确认无剩余Important；没有再次全量复审或运行模型。自动回归采用显式shutdown，随后独立修正版的普通模式三个窗口实际一次关闭/重开也已验证，补足该覆盖差异。

## 0.1.5 素材管理与通用训练入口 — 2026-11-05

依照requesting-code-review，由独立只读reviewer检查本轮素材库/ASR/Core/窗口/训练CLI及测试；未修改代码或运行模型。发现2项Important、1项Minor，均先新增回归观察RED，再修复并运行完整46项Python测试及针对性复核：

| 发现 | 修复与证据 |
|---|---|
| 英文数据不产生BERT文件，特征验收却一律要求BERT | 仅中文要求3-bert；新增英文特征通过回归 |
| 修改清单/标签后可沿用旧upstream.list与特征缓存 | init冻结dataset.json、train.list、upstream.list摘要，后续阶段必须一致；变化或旧格式运行记录要求新建工作目录，避免旧特征配新标签 |
| Path.absolute保留点点，目录包含判断可能被绕过 | 先拒绝路径上的链接，再resolve规范化后比较边界；加入`..`回归 |

同一reviewer针对三个修复复核，确认全部关闭，无剩余Important。作者另外通过失败回归修复素材null条目和2MB清单保存边界，导出复制后重验SHA；全C#125/125通过。真实ASR/窗口/取消及E合成证据见TEST_REPORT。静态复审与配置测试不代表新的通用训练入口已实际跑完GPU训练，本轮明确未执行该阶段。

## 0.1.4 角色包导入复审 — 2026-11-04

依据 requesting-code-review / executing-plans，由独立只读 reviewer 审查本轮未提交的 Core/Infrastructure/Desktop/角色测试及计划。没有启动模型或编辑代码；真实 GUI 由主流程另行验收。

发现一项 P2：角色清单 `files[*].bytes` 是小数或超出 Int64 时，`GetInt64` 的 FormatException 逃逸 ListAsync，导致即便未选择该角色也可能无法启动。已先复现新包与已安装清单两种失败，再改为 TryGetInt64 并抛可隔离的 VoiceLiteException；新增回归与全 C#105/105 通过。复核结论：无剩余 Critical / Important。

ZIP路径/链接检查、解压中取消清理、两组权重往返切换、关闭等待导入、自定义设置恢复均在复审范围。模型来源信任与声音质量仍按角色使用说明及人工听选处理，静态审查不代表模型质量验收。

依据 requesting-code-review 技能，独立 reviewer 对 src/tests/tools/packaging 和设计进行只读审查，没有修改代码、重跑模型或将静态检查当作验收。初始结论 With fixes；没有 Critical，3 Important。

| Important | 实际修复 | 证据 |
|---|---|---|
| 整段播放被固定 35 秒超时截断 | 超时仅限制媒体打开，打开后等待 MediaEnded 或用户取消 | Playback_open_timeout_does_not_limit_audio_duration；实际 WPF 播放 41 秒 WAV 完成 |
| Process.Start 后才加入 Job 存在逃逸窗口 | CreateProcessW(CREATE_SUSPENDED)，加入 Job 后 ResumeThread；stdout/stderr 使用受控管道 | 回归在 Resume 前等 300ms，确认根进程未执行；恢复后根立即派生并退出，关闭 Job 仍回收子进程 |
| 保存结果可覆盖参考音频 | 拒绝当前参考的规范绝对路径，忽略路径大小写 | 两个源文件哈希不变的回归用例 |

5 项回归最先执行均失败；修复后全套 33/33。后新增 OOM 中文错误测试，总计 34/34。没有把作者自审替代独立审查，也没有重派 reviewer 反复审查同一修复；修复由回归与完整套件验证。

Minor 保留：runtime users.pth 的旧路径目前未被 site 导入，迁移 sys.path 已实测；大/未知长度 HTTP 响应与运行中 FFmpeg 取消的专项测试；OpenAPI 读取缓冲进一步有界化。这些不改变已通过的正常中文闭环，但仍应在公开发行前继续收敛。

Reviewer 未裁决的事项由实施者明确处理：干净机器断网、第三方许可、其它设备/音色评价均标为未完成；Python socket monkeypatch 仅是受控依赖的离线约束，不是对恶意原生扩展的 OS 安全隔离。
