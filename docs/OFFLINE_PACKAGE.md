# 本地离线候选包

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
