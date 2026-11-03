# Third-party inventory and release status

Offline binary candidate status: **private local validation only; public redistribution of the complete bundle is not yet cleared**.

VoiceLite's MIT license covers its original client code. The offline candidate preserves engine LICENSE and runtime/package notices in their original locations. These records are an inventory, not a completed license determination.

The source repository contains the original client, tests, packaging tools and documentation. It does not contain engine copies, model weights, runtime distributions or user audio. The outstanding redistribution checks below concern the complete offline bundle and its third-party components.

| Component | Observed version / source | Observed licensing evidence | Outstanding before public release |
|---|---|---|---|
| GPT-SoVITS | local GPT-SoVITS-v2pro-20261006 snapshot | root LICENSE: MIT, Copyright 2024 RVC-Boss | Original commit/tag and provenance of local runtime/acceleration modifications |
| GPT/SoVITS model files | v2ProPlus + s1v3 + speaker encoder | https://huggingface.co/lj1995/GPT-SoVITS/blob/main/README.md labels repository MIT | Per-file provenance/hashes and any inherited model terms |
| Chinese RoBERTa / HuBERT | files bundled in local pretrained_models | Local model/config files | Original model cards and notices must be resolved separately |
| G2PW | local GPT_SoVITS/text/G2PWModel | Required by this snapshot's hardcoded Chinese text frontend | Model origin and notices must be resolved separately |
| fastText language identification | local fast_langdetect/lid.176.bin | Required by LangSegment; source/staged SHA-256 matched | Track original model terms and notices separately |
| Python | 3.12.10 | runtime/LICENSE.txt | Preserve notices |
| PyTorch | 2.7.1+cu118 | installed distribution METADATA says BSD-3-Clause; license resources in package | CUDA/cuDNN and compiled extension redistribution inventory |
| FFmpeg / ffprobe | bundled binaries; see audit | https://www.ffmpeg.org/legal.html ; license varies with build options | Record both builds; obtain matching notices/source obligations |
| .NET desktop runtime | 8.0.33, self-contained | Microsoft runtime LICENSE and ThirdPartyNotices distributed by SDK | Preserve published runtime notices |
| CommunityToolkit.Mvvm | 8.4.0 | https://github.com/CommunityToolkit/dotnet/blob/main/License.md | Preserve MIT notice |
| Other Python packages | local runtime contains 197 distributions | dist-info metadata/license files copied with runtime | Complete per-package inventory and missing-license resolution |

Official sources checked 2026-11-02. No user audio, training data or third-party voice preset is included. The Windows SAPI reference used for tests remains in ignored developer artifacts and is not a default voice.

Release is also pending a clean Windows machine test with network disabled and no developer caches. A successful test on the development laptop is not that acceptance result.
