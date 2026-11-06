# 0.1.6 本地训练闭环 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; one fresh read-only whole-change reviewer at completion. Current-workspace and no-auto-commit preferences override worktree/commit/handoff defaults.

**Goal:** 已校对素材通过图形界面完成独立的SoVITS训练任务、取消/结果恢复和角色ZIP导出，并验证正式CLI的真实训练与完整检查点续训。

**Architecture:** 继续复用锁定上游、既有DatasetService与训练CLI。一个窗口管理素材集的新训练任务；每次训练冻结独立输入，固定保留stock GPT、8轮SoVITS，完成后选择一条已校对3–10秒录音作为角色参考并导出兼容角色包。现有推理窗口与训练窗口模态隔离，进入训练前释放本应用推理Job。

**Tech Stack:** .NET8/WPF/MVVM、C#服务、现有便携Python/PyTorch和原v2ProPlus。无新增依赖/下载。

**Spec:** VoiceLite_Development_Plan.md的M4、用户“下一步继续做”、上轮DATASETS/TRAINING_CLI及已完成角色导入契约。brainstorming的直接执行选项已由持续“开始/继续做”接受，不重复方案批准。

## Global Constraints

- 复用当前未提交0.1.5实现，不丢弃其改动，不自动commit/push。
- 用户素材当前18/18标记已校对（revision3），只读取、导出副本；不代替用户勾选或认证质量。
- 不覆盖原整合包、用户录音、旧E模型；每任务随机ID独立目录，已有输出不覆盖。
- 训练只支持已有兼容完整包与单NVIDIA CUDA；无资源不下载。进程Job所有权、超时、关闭取消和日志有界。
- GUI首版创建固定8轮新任务与导出；本轮验证同冻结数据CLI8→9完整续训，不实现跨数据版本续训或训练质量自动评分。
- 实测成功与未执行范围分别记录；用户已后置的干净机器/CPU TTS不作为本轮新增阻塞。

## Review Focus

- 训练与推理显存不能重叠；退出训练窗口/应用必须等待本应用任务和进程树回收。
- 保存/恢复任务路径受管、不可链接逃逸；坏任务不影响其它任务，运行中崩溃不能冒充完成。
- 素材变化不复用旧特征；取消/失败保留输入与日志，不损坏以前模型。
- 角色ZIP仅含锁定GPT、所选轮次SoVITS及已校对参考，逐项SHA并可被现有RoleLibrary导入，输出不覆盖。
- 进程exit0不足以宣称训练完成：最终目标轮次权重和完整检查点必须存在；训练质量由固定参考/文案试听判断。

## Task 1 — 真实正式CLI与结果契约

- [ ] 使用私有.NET入口调用DatasetService导出用户18条已校对快照；记录源/输入/上游SHA和标签确认来源。
- [ ] 原包Python执行validate/init/features/sovits8/resume9；GPU资源采样、完整日志，遇错先定位并添加失败回归再修复。
- [ ] 新增Python结果收集和export-role契约及测试（先RED）：完成轮次/文件验证、缺结果拒绝、参考范围、ZIP SHA与现有格式、不覆盖和取消临时清理。
- [ ] `tools/training_cli.py`产生`result.json`并增加`export-role --work --name --reference --output`；独立`tools/training_export.py`负责打包。

## Task 2 — C#训练任务与VM

Files: Core/TrainingJob.cs、TrainingViewModel.cs；Infrastructure/TrainingService.cs；tests/TrainingServiceTests.cs、TrainingViewModelTests.cs。

Interfaces: `ITrainingService.ListAsync(datasetId,ct)`, `StartAsync(datasetId,name,engine,progress,ct) -> TrainingJob`, `ExportRoleAsync(jobId,referenceId,output,progress,ct) -> string`；`TrainingJob`保存受管ID、素材ID、名字、状态/完成轮次与可用参考，路径仅由服务构造。VM提供Initialize/Start/Export/Cancel/Close，运行任务可等待。

- [ ] RED测试：只读已校对导出、资源缺失、单任务锁、启动/取消/坏任务恢复、结果检查、导出边界；VM取消/关闭/错误/选择恢复与按钮状态。
- [ ] 服务用ProcessRunner受控Job执行明确CLI参数（init/features/train/export），固定超时、有界输出；任务元数据原子写，失败/取消可见，产物保留；CLI只用随程序发布的脚本与profile。
- [ ] GREEN服务/VM测试，现有协议/素材测试保持通过。

## Task 3 — 最小训练窗口与真实GUI

Files: Desktop/TrainingWindow.xaml(.cs)、DatasetWindow/MainWindow入口及App烟测；Infrastructure.csproj发布训练脚本和profile。

- [ ] 素材窗口“训练新版本”保存/冻结已校对项，打开训练窗口，字段为任务名和资源目录；固定8轮、开始/取消/任务结果/参考选择/保存角色包。
- [ ] MainWindow在打开素材入口前停止并等待已有推理Job，父子窗口关闭顺序有界且不丢校对；模态阻止训练中合成。
- [ ] 真WPF烟测训练/导出；用现有RoleLibrary导入新ZIP并以固定E参考真实生成、播放，旧E和源SHA不变。

## Task 4 — 回归、复审、交付

- [ ] 全构建/测试/自包含发布；独立只读复审，重要问题RED→GREEN解决。
- [ ] 0.1.6客户端累积更新、备份应用日常目录、全包SHA；记录原始基包清单沿用。
- [ ] 更新README/CHANGES/TRAINING_CLI/TEST_REPORT/OFFLINE_PACKAGE等，明确训练与质量范围。打开更新后窗口供用户使用；不自动提交、推送或替换E选择。

## Uncertainties

上游训练真实运行可能暴露适配差异，先实跑后接UI。训练数据虽被用户标记已校对，相似度仍须听选；新模型和参考变化不能混为同条件结论。

## 本轮执行结果

Task1–3完成，137项C#/52项Python、真实CLI8轮/完整续训到10轮、独立GUI8轮及角色导出/导入/播放、实际取消完成。续训日志误报和首次关闭重入均有失败证据与修复；后者新增真实WPF回归及正常模式三窗口一次关闭/重开。独立复审及关闭修复针对性复核无剩余Important。

Task4现已完成：自动审批拒绝的强制结束操作未执行，用户保存关闭后已将最终修正版另行备份安装回原目录。40627文件/11520017274字节全SHA与原目录真实三窗口首次关闭回归通过，正式入口已打开；独立修正版476文件保留备用。首候选全包SHA和旧更新另存，最终验收单独记录。完整记录见TEST_REPORT/OFFLINE_PACKAGE；无自动提交或推送。本轮已授权范围无剩余必做工作。
