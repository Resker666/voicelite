# 本地离线候选包

## 0.1.6 训练窗口与角色导出更新 — 2026-11-05

**正式入口：`dist/VoiceLite 离线验证 0.1.0/VoiceLite.exe`，EXE为0.1.6.0关闭修正版。** 用户保存并关闭旧窗口后，已完成备份、安装及最终整包SHA校验：**40627文件、11520017274字节**。从该目录执行真实WPF关闭回归，训练/素材/主窗口首次关闭均通过；正常模式已重新打开，默认E与原参考恢复。

本次修正覆盖前备份为 `dist/VoiceLite-0.1.6-close-fix-backup-20261105`。独立备用入口 `dist/VoiceLite-0.1.6-client/VoiceLite.exe` 保留，其476个文件与最终发布SHA一致，读取已有设置中的原推理engine路径，不重复复制模型。

原日常目录曾备份升级到首个0.1.6候选，完整SHA **40627文件、11520011478字节**通过，但最终窗口验收发现首次关闭重入异常，该候选已撤下。最终修正覆盖现已完成；没有强制结束窗口或把首候选的全包SHA冒充最终修正版验收。旧备份 `dist/VoiceLite-0.1.6-home-backup-20261105` 保留，模型与原8.50GB基包未重做。

最终客户端累积更新 `dist/VoiceLite-0.1.6-client-update.zip`：**2319556字节**，SHA256 `b8f9f24143a9bc029c3b8f3118e9588f3273de97b6faddcdecd3649fa0486ba6`，20个安装payload（含通用训练脚本与摘要profile，不含模型/音频）。可从原0.1.0完整包直接更新，窗口关闭后按下节相同工具步骤替换更新目录并选择新的备份名；本机已安装最终修正版，无需重复执行。

首次候选ZIP（2317453字节、SHA52424a…）及其安装/完整SHA记录保留于 `.artifacts/training-016-20261105/rejected-first-update`，不作为最终交付。真实WPF首次关闭回归先触发CLR异常，再由Dispatcher排队实际Close修复；三窗口自动回归与普通模式实际关闭均已通过。

校对完素材后，点击“训练新版本”并选择已有完整整合包，完成独立8轮训练、参考选择和新ZIP保存，再从主窗口导入使用。训练资源默认可复用原 `E:\ai\voice\GPT-SoVITS-v2pro-20261006`；日常推理engine不保证含全部训练/ASR模型。客户端不会下载或安装环境，这个小更新不是新制作的全训练离线大包。资源和恢复边界见 `TRAINING_CLI.md`。

本次新8轮角色包另存 `dist/VoiceLite-hutao-reviewed-e8-20261105.zip`，**305317457字节**，SHA256 `2be06bdec331bd7176463329d888b1187835f891f74f91d37ca989e33926b7e7`。来自18条由用户标记已校对的素材，保留原GPT，主参考为Curiosity，另两辅助；真实GUI导出/导入/合成/播放通过，尚未听选证明更像。该候选已添加本机角色库，名称“已校对素材新版本”，原E角色和默认选择保持。它与角色库/训练工作区/原素材都不随Git迁移，需单独备份。

旧E与新8/10轮的同条件试听在 `.artifacts/training-016-20261105/listening/`，三份15.4秒WAV只对独立副本施加固定增益至约-24dBFS，未削波。三组使用相同原E参考和两条留出辅助，区别于新角色包默认参考；不能把参考变化当作模型改善。原始12段结果、输入SHA与重复一致性记录在同轮comparison目录；听感结论仍待用户反馈。

## 0.1.5 素材管理客户端更新 — 2026-11-05

本机日常目录 `dist/VoiceLite 离线验证 0.1.0` 已备份升级到 **0.1.5.0**，目录名保留。双击其中 `VoiceLite.exe`，点击“训练素材”可导入录音、试听、修改文字、保存校对并导出训练集。原E角色及自定义参考设置继续保留。升级后 **40622文件、11519877733字节**完整SHA通过；备份为 `dist/VoiceLite-0.1.5-home-backup-20261105`。

累积更新：`dist/VoiceLite-0.1.5-client-update.zip`，**2266925字节**，SHA256 `2dcb66633bef19f1d53047e8a1d79aaa285363a034318035d89b71431f701e19`，15个安装payload。原0.1.0完整包可直接应用，不必叠加旧更新；更新不携带模型或用户音频，原8.50GB基包ZIP未重做。关闭程序后，用已有包内Python按下述方式校验、备份安装；本机已执行完毕，无需重复：

```powershell
$py = '<兼容完整整合包>\runtime\python.exe'
& $py -B tools/apply-client-update.py dist/VoiceLite-0.1.5-client-update '<日常VoiceLite目录>' --check
& $py -B tools/apply-client-update.py dist/VoiceLite-0.1.5-client-update '<日常VoiceLite目录>' --backup '<新的备份目录>'
# 如需恢复：
& $py -B tools/apply-client-update.py --rollback '<该备份目录>' '<日常VoiceLite目录>'
```

CPU中文转写已经用本机原完整包的模型真实验证。**日常推理包不包含转写模型**，在素材窗口“离线转写资源”选择已有兼容完整整合包；缺模型会给出提示，不会自动下载。本轮客户端更新不是新制作的、含全部训练/ASR资源的完整离线包，换机器仍需携带所需资源，见 `DATASETS.md` 与 `TRAINING_CLI.md`。

本机 `%LOCALAPPDATA%/VoiceLite/datasets/0ec386011cd743fd8886a136a2f62209` 另导入旧试验的18段原录音副本及机器草稿，共116.0854秒，名称“胡桃素材（待校对）”，**已校对0条**；原录音和主界面设置摘要未变。它属于本机用户数据，未包含在Git或更新包中；切勿把草稿视为人工确认。素材窗口已实际打开并恢复，可逐段试听修正。训练GUI和通用CLI的实际GPU训练验收仍未完成；本轮真实命令见 `TEST_REPORT.md`。

以下为历史版本记录，文件数量和SHA各自对应当时版本。

## 0.1.4 角色包客户端更新

支持从 GUI 导入兼容角色ZIP、选择角色并自动填入参考录音/文字/语言，记住上次角色；可切回自定义参考输入。角色权重存放在本机数据目录 roles，客户端累积更新不包含私人角色或录音。用法见 `ROLE_PACKAGES.md`。

本机日常目录仍为 `dist/VoiceLite 离线验证 0.1.0`，EXE已升级到 **0.1.4.0**。备份在 `dist/VoiceLite-0.1.4-home-backup-20261104`；升级后 **40621文件、11519749847字节**完整SHA通过。已有引擎和底模未替换。用户首选的 E 角色通过实际 GUI 按钮导入到 `%LOCALAPPDATA%/VoiceLite/roles`，关闭/重开后自动恢复，原自定义参考和两条辅助仍保留。

客户端更新ZIP：`dist/VoiceLite-0.1.4-client-update.zip`，**2213533字节**，SHA256 `5179589bb1e901ae66e425c8fd6e4170d99291c5b547a5aefba7a55501c03585`，14个payload。对原0.1.0基包可直接应用，不需先叠加旧更新；校验、备份和回滚使用下文相同工具命令。本机已完成应用，未自动提交或推送。

E角色ZIP另为 `dist/VoiceLite-hutao-E-experimental-20261104.zip`，304630055字节，SHA256 `f03c25f373daf093e89488390c17d2e1b78c9c26cc67f805f3f2199140c481ae`。它是本机实验资产，需单独携带；Git和客户端更新不包含它。没有完成公开再分发核验。

0.1.4的105项C#与30项Python回归、真实GPU角色生成/导出/播放和默认底模回退均已执行；CPU与独立电脑断网验收仍按用户要求后置。以下为历史版本记录。

## 0.1.3 辅助参考客户端更新

0.1.3增加可选辅助录音的添加、多选、试听、移除及设置记忆；最多3条且应来自同一声音。仅主参考需要文字，更换主参考会清空辅助列表。模型未改变，音色效果由用户试听决定。

当前日常目录仍为 `dist/VoiceLite 离线验证 0.1.0`，EXE已升级到**0.1.3.0**。升级前备份在 `dist/VoiceLite-0.1.3-home-backup-20261103`；升级后40621文件、11519703265字节完整SHA校验通过。该安装的Mock启动/恢复两条辅助/播放也通过。独立发布的同版本客户端已完成带两条辅助的NVIDIA真实生成、导出和实际播放，见测试报告。

更新ZIP：`dist/VoiceLite-0.1.3-client-update.zip`，**2193475字节**，SHA256：`04c5abb6c04339e70da36da379f8f75281819998da766a33331610327610e224`。含14个安装payload，不含模型或用户录音；可按下节相同校验/备份命令应用0.1.3更新。原8.50GB基包ZIP保留，无需叠加旧更新。

本机已将用户采用的3.03秒参考另存至`%LOCALAPPDATA%/VoiceLite/voice-presets/`，避免清理开发产物时丢失。此项是本机用户数据，不属于更新包或默认音色。

本次升级前的首次校验发现0.1.2包装脚本CRLF与原基包/当前源LF不同，安装按预期拒绝，未修改日常目录。修复了累积补丁遗漏“新版本恢复原基包字节”的客户端组件：现在始终包含受管VoiceLite.*和engine_runner.py。失败候选保留在忽略的`.artifacts/voicelite-013-20261103/rejected-first-update`，不是交付包。

下面保留0.1.2及更早历史。

## 0.1.2 累积客户端更新

0.1.2包含已有OGG导入/试听修复及“自动 / NVIDIA / CPU”设备选择。原0.1.0基包可以直接升级，不需要先应用0.1.1；引擎和模型保持不变，原8.50GB ZIP保留。

公司电脑已应用至 `dist/离线基包/VoiceLite-0.1.0-win-x64-offline-local/VoiceLite 离线验证 0.1.0`，目录名保留，EXE版本为0.1.2.0。更新/回滚/再次更新均实际通过，主备份在 `dist/VoiceLite-0.1.2-backup`。更新后40621个文件完整SHA校验通过；40139个引擎文件与更新前一致。更新ZIP大小2179813字节，SHA256为 `15d3400cd1ce3e0436abb960ab1ab601097095786f9ad5ef374b385664c143d3`。

同日回到家里后，已将 `dist/VoiceLite 离线验证 0.1.0` 从0.1.1升级到0.1.2，保留目录名。家里的备份为 `dist/VoiceLite-0.1.2-home-backup-20261103`；40621个文件完整SHA校验及更新后客户端Mock生成/导出/播放均通过。原8.50GB ZIP仍为历史基包，重新解压后需要应用0.1.2更新。

更新包为 `dist/VoiceLite-0.1.2-client-update.zip`。先关闭目标目录中的客户端和引擎，解压更新包到独立目录。使用完整包内Python执行校验和更新：

```powershell
$package = '<现有完整包目录，包含VoiceLite.exe和engine>'
$update = '<0.1.2更新包解压目录>'
$python = Join-Path $package 'engine/runtime/python.exe'
& $python -B tools/apply-client-update.py $update $package --check
& $python -B tools/apply-client-update.py $update $package --backup '<新的备份目录>'
```

备份目录应位于完整包和更新目录之外，且此前不存在。更新前核对引擎profile/API、文件哈希及活动进程；只替换客户端、说明和清单，保存旧文件，更新失败时自动恢复。保留备份，手动恢复命令为：

```powershell
& $python -B tools/apply-client-update.py --rollback '<备份目录>' $package
```

也可使用更新包内的 `apply-client-update.py`，替换命令中的脚本路径。回滚会拒绝覆盖升级后再次被人工修改的文件。

CPU代码与自动化测试通过，真实合成尚未验收：公司电脑在加载阶段触发系统内存保护，按用户要求延期。2026-11-03回到家里后，新发布的0.1.2客户端使用现有包内引擎完成NVIDIA真实生成、导出和播放，输出5.42秒WAV；本次开发机回归不代替干净Windows断网验收。具体命令与范围见 [测试报告](TEST_REPORT.md)。

下面保留0.1.1与原始基包的历史信息；旧路径是当时开发机上的位置。

> **0.1.1 OGG 修复：**现有目录内的 VoiceLite.exe 已更新到0.1.1，支持OGG导入和自动转换试听。下面8.50GB ZIP是0.1.0历史基包，重新解压它时请叠加 `dist/VoiceLite-0.1.1-ogg-update.zip`（约2.18MB）；模型保持不变，不必重新下载。见 [OGG_FIX.md](OGG_FIX.md)。

状态：**可在本机运行的完整离线候选；未获准公开再分发，不是干净 Windows 验收完成版。**

## 启动与文件

本轮可直接运行：`dist/VoiceLite 离线验证 0.1.0/VoiceLite.exe`。保持整个目录，双击 EXE；不需要命令行、Python/Conda/.NET 安装。当前验证 Windows 11 x64 + RTX 4070 Laptop 8GB，需系统具备可用 NVIDIA 驱动。

完整 ZIP：`dist/VoiceLite-0.1.0-win-x64-offline-local.zip`。

| 项目 | 实际值 |
|---|---|
| ZIP 字节数 | 8503049681（约 8.50 GB / 7.92 GiB） |
| ZIP SHA-256 | `984865d52d683691a2be2ceef343c11338beb37df8f4bfbba6834de1382184d4` |
| 源资源覆盖 | 40139 / 40139 个引擎文件，missing=0，unexpected=0 |
| 引擎资源字节数 | 11342914956 |
| 整体哈希覆盖 | 40619 个文件，11519655164 字节；package-files.json 自身另计 |
| 整体哈希校验 | 全部通过；.artifacts/package-verify.log |
| ZIP 条目检查 | 清单全部 40619 项存在且解压长度匹配 |
| ZIP 旁文件 | .zip.sha256、.zip.metadata.json |

下载 ZIP 和解压目录需要同时容纳约 8.5 GB 与 11.5 GB，另留生成结果空间。源仓库不包含这些资源，Git 忽略 dist、工具缓存、权重和音频。

早期缺失资源的 `VoiceLite-local-win-x64-0.1.0.zip` 已被最终包替代，不作为交付物。

## 包内结构

- VoiceLite.exe 及相邻 .NET 运行文件：自包含桌面程序。
- engine/：原推理源码、便携 Python/Torch、FFmpeg、固定 v2ProPlus 组合、RoBERTa/HuBERT、G2PW、fastText 语言识别与文本资源。
- engine_runner.py：在上游 API 之外管理离线约束并委托 api_v2.py，不替换推理算法。
- START_HERE.txt：三步中文使用说明。
- THIRD_PARTY_NOTICES.md、VoiceLite-LICENSE.txt、notices/ 与引擎内原许可：分发清单和现有许可副本。
- engine-files.json / package-files.json：逐文件大小及 SHA-256。
- LOCAL_VALIDATION_ONLY.txt：明确本候选尚未通过公开分发审查。

没有打包测试参考录音、生成 WAV 或个人音色预设。测试采用 Windows 已安装的 SAPI 声音，在忽略的 .artifacts 下保存证据。

## 实际功能验证

最终包已移动到含中文及空格的目录，自动定位其中的引擎。在新数据/缓存、移除 Python/.NET 路径依赖、清理代理和拒绝外部 Python socket/DNS 的条件下：真实中文 WAV 5.14 秒、32kHz、单声道，生成→保存→MediaPlayer 播放完成，全流程 34.466 秒。进程退出后没有本应用残留模型进程。

这不是 OS 网络沙箱，也不是干净操作系统测试。已有 Windows 驱动、系统组件和开发软件仍存在；公开发行前必须用独立干净 Windows 真正断网验收。

当前仍缺原整合包准确来源/commit 与全部第三方（尤其模型、G2PW、fastText、CUDA/cuDNN、加速扩展、不同 FFmpeg 构建及 197 个 Python 依赖）的分发核查，故打包默认阻止公开发行，只允许显式本地验证候选。

8.50 GB ZIP 超过 GitHub 当前单个 Release 附件小于 2 GiB 的限制；公开下载渠道尚未配置。见 [GitHub 官方限制](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)。没有创建远程仓库、上传或发布。
