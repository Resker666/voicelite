# 角色微调实验记录 — 2026-11-04

0.1.5已新增素材管理与逐字校对窗口，并整理通用训练CLI。下面保留历史试验事实；本机18条旧机器草稿已复制进“胡桃素材（待校对）”，没有自动标成人工确认，也没有重新训练。新流程见 [素材说明](DATASETS.md) 与 [训练适配器](TRAINING_CLI.md)，其通用GPU阶段仍待实际运行验收。

已在本机完成一次真实 GPT-SoVITS v2ProPlus 微调、完整检查点续训和真实 API 对照。用户试听 D/E/F 后反馈 **“E更像”**，本轮首选为 **原 GPT 底模 + 微调 9 轮的 SoVITS**。这是该次试听的主观选择，不代表所有文案相似度、发音和语气均已验收。实验时客户端为 0.1.3；随后用户授权接入角色使用流程，**0.1.4 已支持从 GUI 导入、选择角色包并真实生成**，仍未实现训练界面。

## 范围与素材

用户授权先做小规模训练验证，再依据听感决定界面接入。本次继续复用锁定版上游训练与推理能力，没有重写网络或优化器，没有下载模型、安装依赖或覆盖原 Python 环境。

- 工作区：`E:\develop\github\voicelite`；源码基线 `beca7fb`。
- 原整合包：`E:\ai\voice\GPT-SoVITS-v2pro-20261006`。该包没有可核验的 Git commit/tag，继续用文件 SHA 锁定。
- 私有实验根目录：`E:\develop\github\voicelite\.artifacts\character-training-20261104`。下文实验路径均相对该目录；它和 `dist` 均被 Git 忽略。
- 原录音：`E:\ai\voice\hutao`，共 39 个 OGG。四个完整原录音留出：About_Hu_Tao_-_Name、About_the_Vision、Good_Morning、Good_Night。主参考来自留出的 Name 录音，辅助参考为 Vision、Good_Morning。
- 其余长度 2–16 秒的 29 段用已有本地 Paraformer 生成转写草稿；排除唱段和明显可疑的 11 条草稿后，使用 **18 段、115.936 秒**。转写尚未逐字人工核对，不能当作正式训练集。
- 上游小样本加载器把 18 条重复到 90 条；仍只有 18 段独立录音，数据量没有增加。

`audio-inventory.json`、`data/transcripts-draft.json`、`data/selection.json`、`data/train-selected.list` 保留来源、选择和机器转写记录。没有把试听文案加入训练标注；四个完整留出录音也没有加入训练。

## 运行方式与配置

复制 222 个已记录 SHA 的上游源码/配置文件到 `engine-source`，通过目录 junction 读取原运行时、底模、G2PW 和原生组件。**junction 不是只读权限隔离**；运行后复核已记录原输入哈希。实验日志、缓存、特征、检查点和推理权重放在实验目录内。

四项预处理直接执行上游 `1-get-text.py`、`2-get-hubert-wav32k.py`、`2-get-sv.py`、`3-get-semantic.py`。最终 18 条文字、BERT、HuBERT、32kHz 音频、SV 和 semantic 特征全部匹配，证据为 `features-verified.json`。

| 项目 | 实际设置或结果 |
|---|---|
| 设备 | Windows，RTX 4070 Laptop，8 GB 显存，单 GPU 顺序执行 |
| 运行库 | Torch 2.7.1+cu118 / CUDA 11.8，Lightning 2.6.5，Transformers 4.57.6，FunASR 1.3.22，NumPy 1.26.4 |
| SoVITS 底模 | `v2Pro/s2Gv2ProPlus.pth`、`s2Dv2ProPlus.pth`，配套已有 SV 模型 |
| GPT 底模 | `s1v3.ckpt`，沿用上游 v2 配置和学习率计划 |
| SoVITS 微调 | batch 2、FP16、gradient checkpoint、学习率 0.0001；先 8 轮，225.875 秒 |
| GPT 微调 | batch 2、16-mixed、单 worker；先 15 轮，72.453 秒 |
| SoVITS 续训 | 从完整 G/D 检查点恢复，8 → 9 轮，66.532 秒 |
| GPT 续训 | 从完整 Lightning 检查点恢复，15 → 16 轮，22.484 秒 |

四个成功训练阶段共约 6 分 27 秒，不含预处理、失败重试、推理和导出。一次 GPT 训练中采样的整板显存为 5268 MiB；这不是精确峰值或最低配置承诺。

`run_stage.py` 只做启动适配：离线模式与本地缓存、阻断外部 Python socket；单 GPU 时使用 Lightning 单设备模式，为原 bucket sampler 指定既有参数 `num_replicas=1, rank=0`；仅允许本机自建检查点中的标准库 `WindowsPath` 类型，保持 weights-only 加载。未编辑上游训练算法。`experiment.py` 用 Windows Job 在子进程恢复执行前接管进程树，记录命令、退出码并处理超时、结束和失败清理。

## 续训证据与产物

`logs/resume-sovits.log` 记录恢复 G、D 与优化器，并从 epoch 9 开始；没有退回重新加载底模训练。`logs/resume-gpt-allowed-path.log` 记录 `Restoring states` 和 `Restored all states`，从 `epoch=14-step=165.ckpt` 恢复。最终完整 GPT 检查点为 `epoch=15-step=176.ckpt`：epoch 从 0 开始，global_step 是上游手动优化器步数，不能将 45 个 batch/轮直接当成 global_step。

这次续训使用同一数据集，验证的是**能够恢复训练状态**。尚未验证新增素材后的训练策略，也没有证据支持“每灌一次素材就一定越来越像”。后续应人工校对并累计新旧语料，保留旧模型，在相同留出文案上比较，再决定是否替换角色版本。

| 产物 | 实验内路径 | 字节 |
|---|---|---:|
| SoVITS 推理权重 | `weights/SoVITS/hutao_pilot_e9_s414.pth` | 172765683 |
| GPT 推理权重 | `weights/GPT/hutao_pilot-e16.ckpt` | 155313696 |
| SoVITS 完整 G 检查点 | `features/logs_s2_v2ProPlus/G_233333333333.pth` | 1177882925 |
| SoVITS 完整 D 检查点 | `features/logs_s2_v2ProPlus/D_233333333333.pth` | 758457133 |
| GPT 完整检查点 | `features/logs_s1_v2ProPlus/ckpt/epoch=15-step=176.ckpt` | 931418111 |

完整检查点均检查到非空优化器状态。最终推理权重浮点值均有限；SoVITS 的 678 个可比浮点张量中 672 个变化，GPT 的 295 个均变化。这证明权重发生更新，不代表主观相似度已提升。

## D / E / F 真实对照

三组都使用原来认可的 3.03 秒主参考、对应文字“胡桃的胡是胡吃海喝的胡”和相同两条辅助录音；中文、seed 20261103、temperature 1。每组单独启动受控引擎、加载权重并丢弃预热样本。只连接所有权已验证的 `127.0.0.1` API，不与未知端口进程通信。

| 条件 | GPT | SoVITS | 三句 WAV 时长（秒） |
|---|---|---|---|
| D-base | 原底模 | 原底模 | 5.32 / 4.16 / 5.22 |
| E-sovits | 原底模 | 微调 9 轮 | 5.32 / 4.16 / 5.22 |
| F-both | 微调 16 轮 | 微调 9 轮 | 5.08 / 4.88 / 5.38 |

三句文案：

1. 你好，今天我们一起出去走走，顺便看看有什么有趣的事情吧。
2. 旅行者，忙完今天的事情，就一起去喝杯茶吧。
3. 明天早上八点半，请在门口等我，记得带上雨伞。

每组再重复第一句，共 12 段 32kHz PCM16 WAV。三组各自重复样本与第一句 SHA 一致；全部通过 FFmpeg 解码和非静音检查。原始结果在 `comparison/*-01.wav`、`*-02.wav`、`*-03.wav`、`*-repeat.wav`；`comparison-report.json` 保留完整条件和哈希。

供聊天试听的 `comparison/D-listen.mp3`、`E-listen.mp3`、`F-listen.mp3` 只在第一句原 WAV 上施加固定增益，使解码后 RMS 接近 -24 dBFS（实测 -23.997 / -23.990 / -23.997）。原始微调输出比 D 响约 5 dB，调整试听音量可减少判断干扰；不宣称等同于人耳感知响度完全一致。原 WAV 未改，命令和 SHA 在 `listening-previews.json`。

用户反馈“E更像”，对应这组三段第一句试听。已记入 `listener-preference.json`；未收到关于吞字或怪音的进一步反馈。E 为后续迭代基线，保留 D/F 和所有原始技术报告，不把主观选择改写为客观相似度评分。

## 实验角色包与使用边界

听选后的 **E 首选包**：`E:\develop\github\voicelite\dist\VoiceLite-hutao-E-experimental-20261104.zip`，**304630055 字节（约 290.5 MiB）**。

SHA256：`f03c25f373daf093e89488390c17d2e1b78c9c26cc67f805f3f2199140c481ae`。

ZIP 共 7 个文件：原 GPT 底模、微调 SoVITS 权重、主/两条辅助参考、`character.json` 和 `README.txt`。包内五个模型/音频文件的 SHA、ZIP CRC 和 E 组合选择校验通过；两组权重与实际试听条件完全相同。`character.json` 版本为 `20261104-E1`，标记 E 首选、用户反馈、GPT 未使用微调、SoVITS 9 轮和仍未完成的公开分发核验。

原先的 F 包 `dist/VoiceLite-hutao-experimental-20261104.zip`（304633151 字节，SHA256 `ff4cb93863cb724785d28f84f81109b14c5696644ac3ac3604b06e6df11503fe`）保留未改，作为两组都微调的对照，不再作为首选包。E 导出结果见 `selected-e-export.json`；原 `final-audit.json` 保留初次实验事实。

**VoiceLite 0.1.4 可直接点击“导入角色包”选择 E ZIP。** 已完成真实 GUI 生成/导出/播放及重启恢复；角色复制到本机库，自动填入参考与文字，后续在下拉框选择即可。详细用法见 `ROLE_PACKAGES.md`。0.1.3 及实验包 README 中针对旧客户端的“不支持导入”说明是实验阶段事实。仍需要已有兼容整合包，角色包本身不是完整运行环境。

角色包不含优化器、完整训练集或运行时，不能仅靠它继续本次完整检查点训练。换电脑续训要另外保存 `data`、`features`（含完整检查点）、`configs`、实验启动脚本和清单，并准备相同兼容整合包。不要直接递归打包 `engine-source`，其 junction 指向已有的大型运行时/模型目录。Git 只承载源码与记录，不能恢复这些私有训练资产。

本次素材及底模公开分发核验尚未完成，包留在忽略目录，没有推送或发布。原始输入的复核范围为 **39 个录音、4 个底模、222 个上游源码/配置文件**，SHA 全部不变；详情见 `final-audit.json`。没有借本次训练改动日常客户端或替换日常底模。

## 验证状态与下一步

真实训练、完整检查点续训、权重更新、受控 API 加载、12 段 WAV、重复控制和实验导出均通过；用户主观选 E 更像。后续 0.1.4 的角色 GUI 导入也已真实通过。逐字转写质量、更多文案的发音/怪音、增量加入新素材后的收益及公开分发均未验收。

下一步以 E 为基线继续日常合成体验；继续训练前校对文字、扩充干净语料，并在相同留出文案上复测。训练实验脚本位于被忽略的 `.artifacts`，是本机可验证试验，不是已交付到开源仓库的通用训练功能；角色导入和选择则已进入 0.1.4 产品代码。实际命令及失败修正见 `TEST_REPORT.md` 最新章节。
