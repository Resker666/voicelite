# Independent code review — 2026-11-02

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
