# GPT-SoVITS 本地基线审计

日期：2026-11-02（Asia/Shanghai）。本文件区分源码观察、本机实测与尚未验证事项。

## 目录与版本身份

- 客户端：`E:\develop\github\voicelite`，开始时仅有两份需求 Markdown，无源码、测试或 `.git`。
- 引擎：`E:\ai\voice\GPT-SoVITS-v2pro-20261006`，包含 portable Python、Torch、FFmpeg、预训练模型；不是带 `.git` 的上游 checkout。
- 原始 commit/tag：**无法从本地资料确认**。文件夹日期不能替代 commit。没有虚构 tag，也没有更新/覆盖上游环境。
- 本地存在 `tools/portable_runtime.py`、加速适配等代码；它与网上 main 不能直接视为同一版本。当前适配固定到以下本地源码快照及离线包逐文件 SHA-256。公开发布仍需补齐来源溯源。

| 文件 | SHA-256 |
|---|---|
| api_v2.py | `44E067698A61014CDFF3BDA1B2FBFF97252440E5879C429D38119FB0010D2B42` |
| GPT_SoVITS/TTS_infer_pack/TTS.py | `A07F54D9BB28FDDEEF6A82F3E24A877241483043905FE455BFE28A5BC32A38F5` |
| tools/portable_runtime.py | `1F739C853946FDFBDC7B973FF4A76ACCEB6F6AD3F63E17CA80377D61A7377F9A` |

完整选定模型哈希和 197 个 Python distribution 的元数据见本地 `.artifacts/upstream-inventory.json`；离线资源哈希见候选包 `engine-files.json`。未将权重、用户音频或运行时提交到源码。

## 本机工具链

| 项目 | 实际结果 |
|---|---|
| OS | Windows 11，内核版本 10.0.26200，x64 |
| GPU | NVIDIA GeForce RTX 4070 Laptop GPU，8188 MiB |
| 驱动 | 610.74（nvidia-smi） |
| Python | 包内 3.12.10，64-bit |
| Torch | 2.7.1+cu118，torch CUDA 11.8；`torch.cuda.is_available()` = true |
| FastAPI / uvicorn | 0.115.14 / 0.50.1 |
| 原系统 dotnet | 只有 6.0.5 运行时，无 SDK |
| 构建 SDK | 官方 8.0.427，下载到 `.tools/dotnet`；SHA-512 校验通过，未进行系统级安装 |
| 自包含桌面运行时 | 8.0.33，随离线候选客户端分发 |

SDK 来源：`https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.427/dotnet-sdk-8.0.427-win-x64.zip`。
SHA-512：`a4dd6eb2bf2a2f6fb10997d3e044a229b23ad96a5c86d20494687e34ee73b537d7bd14488ce14cded6917f6778da4d66360e2734020b7ef487d0f29ab144489d`。

这台机器的成功不代表最低配置，也没有承诺 CPU、所有 NVIDIA GPU 或所有显存容量可用。

## API：以本地文件实际实现为准

`api_v2.py` 接受以下启动参数：

```powershell
# 工作目录为引擎根目录；-c 指向 VoiceLite 自己的数据目录，避免上游修改原配置。
& '.\runtime\python.exe' -B '.\api_v2.py' -a 127.0.0.1 -p <空闲端口> -c <VoiceLite生成的配置.yaml>
```

参数：`-a/--bind_addr` 默认 127.0.0.1；`-p/--port` 默认 9880；`-c/--tts_config` 默认 GPT_SoVITS/configs/tts_infer.yaml。VoiceLite 自动选端口，并在连接前核实监听者为自己持有句柄的 Python PID。

实际路由：GET/POST `/tts`；GET `/control`、`/set_refer_audio`、`/set_gpt_weights`、`/set_sovits_weights`。源码虽在说明中示意 POST control，实际仅注册 GET，客户端不依赖该示意。**没有 `/health`、token 鉴权或取消推理路由。**

就绪使用属于本应用进程的 `/openapi.json` 核对 POST `/tts` 和 TTS_Request 结构；这是 API 契约检查，不等同于模型已成功合成。

首版请求包含 `text`、`text_lang`、`ref_audio_path`、`prompt_text`、`prompt_lang`，固定 `media_type=wav`、`streaming_mode=false`、`text_split_method=cut5`、`batch_size=1`。参考路径是服务端本地绝对路径，非上传接口。首版 UI 要求人工填写参考文字。

本地参数检查依赖当前模型的语言集合；UI 首版提供 `zh`（中英混合）和 `en`，其它语言尚未纳入 UI 验收。参考录音限制由 TTS.py 的 48000–160000 个 16k 样本判断，实际为 **3–10 秒**。

VoiceLite 先用包内 FFmpeg 转成 32kHz、单声道、PCM16 WAV，长度超限直接报错，保留原始文件。非流式 API 返回 `audio/wav`；失败通常为 HTTP 400 JSON。客户端不将错误响应保存为 WAV。

取消会回收应用托管的引擎进程树，再次生成重新加载。Windows 进程以 suspended 状态创建，先加入带 KILL_ON_JOB_CLOSE 的 Job，再恢复执行，避免子进程在管理前运行。生产日志仅记录生命周期、异常类别和安全诊断，不记录上游打印的完整输入文本。

## 所选模型与辅助资源

原 `tts_infer.yaml` 的 custom 配置为 v2ProPlus、CUDA、半精度，使用：

| 资源 | 已存在字节数 |
|---|---:|
| s1v3.ckpt | 155284856 |
| v2Pro/s2Gv2ProPlus.pth | 200125741 |
| sv/pretrained_eres2netv2w24s4ep4.ckpt | 107528697 |
| chinese-roberta-wwm-ext-large/pytorch_model.bin | 651225145 |
| chinese-hubert-base/pytorch_model.bin | 188811417 |
| fast_langdetect/lid.176.bin | 131266198 |

还需 config、tokenizer、HuBERT preprocessor、NLTK、发音词典、运行时 DLL 等。**chinese2.py:28 写死 is_g2pw=True，不能因 portable_runtime 设置 G2PW=0 就排除模型。**最终清单包含完整 G2PWModel（主 ONNX 为 635212732 字节）及其字典。其它模型版本、训练权重、ASR 和 UVR 权重不进入首版候选包。

## 实际基线结果

测试参考来自 Windows 已安装的 Microsoft Huihui Desktop（SAPI），不是用户录音。原始 12.46 秒样本被本地限制发现，重新生成短句后再测试；没有把过长参考直接用于通过结果。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/create-test-reference.ps1
& '<引擎>\runtime\python.exe' -B tools/engine-smoke.py --engine '<引擎>' --reference '.artifacts/smoke/reference.wav'
```

原目录直接 API：启动 32.922 秒；合成 11.672 秒；输出 329004 字节、32000Hz、单声道、PCM16、5.14 秒。SHA-256：`87381275fd4ea1db5624f6dbe8c796fc86231cafdced203a6d6a138cd38809ab`。子进程退出已确认。

Windows SoundPlayer.Load/PlaySync 正常完成；另有 WPF MediaPlayer.MediaEnded 完成证据。这表示系统播放器能解码并完成播放，未把它等同于人工音质或音色相似度评价。

WPF + 原引擎真实 GUI：生成/保存/播放完成，5.10 秒 WAV，完整 UI 流程 26.456 秒；见 `.artifacts/ui-real/ui-smoke.json`。

**离线候选包迁移后的首次验证失败：**模型能加载并通过 OpenAPI 检查，随后合成达到 180 秒超时。根因为误排除写死启用的 G2PW 及代理绕行，失败和部分下载处理记录见 TEST_REPORT.md。补入本地完整资源、清理代理后，最终迁移包在新缓存条件下通过：5.26 秒 WAV、WPF 导出/播放完成，全流程 33.129 秒。仍不把开发机测试称作干净 Windows 真断网验收。

## 版权与公开发行阻塞

本地根 LICENSE 为 MIT，版权行为 `Copyright (c) 2024 RVC-Boss`；保留其许可。模型仓库 [model card](https://huggingface.co/lj1995/GPT-SoVITS/blob/main/README.md) 标注 MIT，但单个本地权重、RoBERTa/HuBERT、speaker encoder 的来源和继承条款仍需逐项核对。

FFmpeg：n4.3.2-160-gfbb9368226，配置包含 --enable-version3；ffprobe：2022-04-07-git-607ecc27ed-full_build-www.gyan.dev，包含 --enable-gpl、--enable-version3。两者构建不同，不可笼统写成全部 MIT。许可要求需结合对应构建核对：[FFmpeg 官方说明](https://www.ffmpeg.org/legal.html)。

Python runtime/LICENSE.txt、PyTorch dist-info BSD-3-Clause 声明均存在；CUDA/cuDNN、加速扩展和其余 Python 依赖仍需完整分发核查。离线包带 `.NET` 和 MVVM 的本地许可副本，详见 THIRD_PARTY_NOTICES.md。

公开包还缺原整合包准确来源/commit、全部组件分发记录、干净 Windows 的真正断网首次启动测试。当前打包器默认阻止公开发行配置；`-LocalValidation` 明确产生私人验证候选，不执行上传或发布。
