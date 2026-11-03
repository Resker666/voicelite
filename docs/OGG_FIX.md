# OGG import follow-up — 2026-11-02

用户提供 E:/ai/voice/hutao 文件夹并报告 OGG 无法导入。只读检查：39 个文件都可读取音频元数据，均为 Vorbis；20 个为 3–10 秒、10 个不足 3 秒、9 个超过 10 秒。Hello.ogg 为 19.054667 秒，About_the_Vision.ogg 为 6.28 秒。

根因：MainWindow 文件筛选与拖入白名单、AudioImportService 校验均仅接受 WAV/MP3。现有 FFmpeg 支持 OGG，但请求在解码前即被拒绝。

范围：统一允许 WAV/MP3/OGG；生成前继续转换到单声道 PCM16 WAV，保留 3–10 秒上游限制；OGG 试听也经过 FFmpeg，避免依赖 Windows 对 OGG 的原生解码；取消/退出须等待试听转换收尾。不修改用户的原文件，不加入自动 ASR 或音频裁剪。

验证：先加入格式策略、真实 Vorbis/Opus 导入、预览临时文件生命周期和原有长 WAV 试听回归，运行观察失败；随后最小实现并运行整套测试、构建、真实用户 OGG 解码/试听。用户音频不进入源码或离线包。最后更新可运行目录与补丁分发说明。

验证记录：新格式/预览回归先出现 6 个预期失败，另一个运行中取消回归先失败；初版实现 45/45 通过。独立审查又指出关闭期间新试听竞态和 OGG 预览误用推理时长校验。新增 4 项回归先失败后修复，最终 **C# 49/49，build 0 warnings/errors**（.artifacts/tests/ogg-final.trx、ogg-build.log）。

版本：0.1.1。用户确认关闭窗口后，已更新原路径 `dist/VoiceLite 离线验证 0.1.0/VoiceLite.exe`，目录名称沿用原名，程序 FileVersion 已为 0.1.1.0。

实际文件验证：About_the_Vision.ogg（元数据6.28秒）WPF播放完成，报告时长6.2947秒；Hello.ogg（元数据19.0547秒）WPF完整播放19.0733秒。两次源文件 SHA-256 均未变化，临时 previews 目录均清空。报告在 .artifacts/ogg-user-preview、ogg-hello-preview。测试中的生成按钮使用明确标识的 Mock，验证的是真实 OGG 转换与播放，没有声称使用这些角色录音完成真实克隆。测试、原录音及其转换副本均未加入源码或离线更新包。

生成与试听分开：试听 OGG 不受 3–10 秒限制（最长5分钟，过长明确报错）；用于声音生成的参考依然必须3–10秒。原有 WAV/MP3 原生试听保持。PlaybackQueue 在关闭前锁定新请求，取消并等待转换、播放器与临时文件清理。

离线更新包：`dist/VoiceLite-0.1.1-ogg-update.zip`，2175392字节（约2.18MB），SHA-256 `548e08d77be800c751e8e4aa813a9f576bb87238f26501fd660bd7b6ac6d3d87`。包含8个改变的客户端文件、说明和更新后的整体清单，不含模型。原8.50GB ZIP保持为0.1.0历史基包，使用它时需叠加本更新；当前工作目录已自动应用，无须重下模型。引擎哈希继承先前已验证且未修改的模型集合，当前更新单独校验客户端文件。

最终核对：Python工具测试9/9；8个已安装客户端文件逐项hash与更新清单匹配；更新ZIP不含engine文件；无生产NotImplemented桩。原独立reviewer对修复范围再次只读核对，结论“no remaining scoped blocker”。
