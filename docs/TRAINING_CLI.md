# 本地训练与角色导出（0.1.6）

`tools/training_cli.py`、`training_stage.py`、`training_process.py` 和 `training_export.py` 调用锁定版GPT-SoVITS原始预处理与SoVITS训练，不重写算法，不下载或安装依赖。沿用用户选出的E组合：保留原GPT，微调SoVITS。0.1.6已提供对应GUI的新建任务与角色导出。

**本轮真实验收：**18条由用户标记已校对的录音通过正式CLI完成四项特征、8轮训练；第9轮模型已训练完成但被旧日志判断误报，修复后以实际G/D恢复日志和完整检查点证据核对，并通过正式CLI继续到10轮。独立WPF闭环另完成8轮、角色ZIP导出/导入、真实合成与播放。没有独立认证转写或音色质量，原E模型保留；历史实验见 `CHARACTER_TRAINING.md`，本轮命令/失败修正见 `TEST_REPORT.md`。

## GUI日常步骤

1. 在“训练素材”里听音逐字校对并保存，至少两条启用且已校对；未校对项不会进入训练。
2. 点击“训练新版本”，填写版本名称，选择兼容完整整合包。默认复用素材窗口的资源目录。
3. 点击“开始训练新版本”，等待特征提取与8轮训练。进入素材窗口会先停止本应用推理引擎，训练窗口阻止同时生成；其它应用占显存仍会影响训练。
4. 完成后在训练记录下选择一条3–10秒录音作为主参考，保存新的角色ZIP。系统选取至多两条同语言辅助参考；文字来自冻结的已校对输入。
5. 关闭训练/素材窗口，回主窗口“导入角色包”并试听。旧E等角色仍保留，不会被新任务覆盖。

任务保存于 `%LOCALAPPDATA%\VoiceLite\training\<任务ID>`。重新打开同一素材集可查看成功/失败/取消记录、导出完成版本或打开任务日志。每次新任务冻结独立输入；取消/关闭等待自有进程树退出，保留输入和检查点。GUI暂不恢复执行中断任务，可新建任务重试；需要同数据续训时使用下方开发者命令。没有合适3–10秒参考时训练结果保留，但不能直接导出角色包。

## 资源与输入

- Windows与兼容的原完整整合包；`packaging/training-profile.json`锁定222个源码/配置文件、4个底模和API SHA。原包无可核验Git commit/tag，继续按文件摘要锁定。
- 预处理和训练要求一个可用NVIDIA CUDA设备，使用原包内Python/Torch；不承诺所有素材在8GB显存下都能训练。输入校验/初始化本身不运行GPU模型。
- 输入来自素材窗口“导出训练集”的目录。所有条目必须已启用且标记已校对，语言为zh/en，文字单行且不含 `|`。音频必须匹配SHA且为2–18秒、32kHz单声道PCM16。
- 完整训练另需 `s2Dv2ProPlus.pth` 等资源；当前日常推理包不保证包含全部训练文件。请选择匹配的原完整整合包，不覆盖现有环境。

## 命令

在仓库根目录，用已有整合包的Python执行，变量均替换为自己的路径：

```powershell
$python = '<兼容完整整合包>\runtime\python.exe'
$engine = '<兼容完整整合包>'
$dataset = '<从素材窗口导出的目录>'
$work = '<全新的独立训练目录>'
& $python -B tools/training_cli.py validate --dataset $dataset
& $python -B tools/training_cli.py init --engine $engine --dataset $dataset --work $work
& $python -B tools/training_cli.py features --work $work
& $python -B tools/training_cli.py sovits --work $work --epochs 8
# 同一份数据恢复完整G/D与优化器，额外训练到第9轮：
& $python -B tools/training_cli.py sovits --work $work --epochs 9 --resume
# 完成后以result.json中references列表的一条ID导出；必须使用新的ZIP文件名：
& $python -B tools/training_cli.py export-role --work $work --name '<角色名称>' --reference '<参考片段ID>' --output '<训练目录之外的新角色.zip>'
```

`init`先验证数据和上游，再复制已审计源码及独立素材，生成模型配置、输入清单、日志目录和运行记录。已有训练目录永不覆盖。运行时/模型/G2PW/原生组件通过本机目录junction复用；**链接不是只读权限隔离**，运行后可按摘要复核已记录原文件。

`features`执行原四项预处理，逐项检查文字、语义、HuBERT、音频、SV；仅中文需要BERT文件。验收完成才允许训练。`sovits`使用batch2、FP16、gradient checkpoint与原v2ProPlus结构；保存推理权重及完整检查点。每个阶段用挂起创建后加入Windows Job再恢复执行；有超时与日志体积限制，Ctrl+C/异常会回收自己拥有的进程树，不结束未知进程。

## 恢复与版本边界

每次 `init`冻结 `dataset.json`、`train.list` 和实际上游输入清单的摘要。修改/新增录音或更正文字后，**重新导出并新建训练目录**，不能靠更换清单继续使用旧特征。旧版本生成、缺少冻结摘要的工作目录也需重新初始化。

`--resume`只用于同一份冻结数据；必须同时有完整G/D优化器检查点，目标轮数严格增加，恢复前保留检查点备份。已有检查点但没传resume时拒绝隐式继续。工作区/配置输出不允许回到原整合包或输入素材目录；完整G/D损坏或恢复未被日志证明时不报告成功。

`result.json`记录目标轮次推理权重SHA和可用参考；缺目标权重/检查点时不报告完成。训练阶段在退出前检查G/D均包含优化器且对应目标轮次；续训同时核对原上游G/D加载行、起始轮次与训练后验证标记，拒绝底模回退。角色ZIP只包含原GPT、所选SoVITS与参考及兼容清单，不含完整数据集或优化器，导出前重验冻结数据与权重，写临时文件并核对包内SHA后提交。

当前尚未接入跨素材版本续训、GUI中断恢复与训练质量自动评估。请保留旧模型并用固定留出文案试听；没有“每多训一次必然更像”的保证。推理权重在 `weights/SoVITS`，恢复状态在 `features/logs_s2_v2ProPlus`；两者用途不同。工作区内的 `client-logs` / `work/logs` 记录失败阶段；可用“查看任务日志”打开目录。6 GiB是启动时的最低空闲检查，不承诺任意规模素材都能在该空间/8GB显存中成功。

不要直接递归打包工作区中的 `engine-source`，其junction会指向已有大包。迁移应携带原素材/导出、运行记录、配置和完整检查点，并准备匹配运行时；素材、模型、运行时和私有日志均不随Git提交。
