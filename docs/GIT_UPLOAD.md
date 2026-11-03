# 首次上传 Git 仓库的文件范围

源码仓库与完整离线下载包分开维护。源码用于审阅、贡献和构建；普通用户将来下载完整离线包，解压后运行 VoiceLite.exe。

## 上传哪些

| 路径 | 内容 |
|---|---|
| `src/` | WPF 客户端、核心契约与服务实现，包含项目文件和 `packages.lock.json` |
| `tests/` | 单元、集成及回归测试，包含项目文件和依赖锁文件 |
| `tools/` | SDK 引导、引擎包装、审计、验证及打包脚本，以及测试用小型引擎源码 |
| `packaging/` | 引擎文件需求、固定 API 哈希和离线包说明模板，不含实际模型 |
| `docs/` | 上游审计、测试报告、离线包说明、修复说明及设计记录 |
| `VoiceLite.sln`、`Directory.Build.props`、`global.json` | 解决方案、统一构建选项和 SDK 版本 |
| `.gitignore`、`README.md`、`LICENSE`、`THIRD_PARTY_NOTICES.md` | 忽略规则、使用说明及许可证信息 |

`VoiceLite_Development_Plan.md`、`CODEX_START_HERE.md`、`docs/IMPLEMENTATION_PROGRESS.md` 和 `docs/superpowers/` 是需求或开发过程记录，可一起保留；运行客户端不依赖这些文件。审计报告中的本地路径属于开发机执行记录，不是其他用户需要照搬的安装路径。

## 留在本地的内容

- `dist/`：完整离线包、客户端更新包和发布目录。
- `.tools/`：本地 .NET SDK、NuGet 缓存和 CLI 数据。
- `.artifacts/`：测试输出、参考音频、生成音频、截图和诊断记录。
- `bin/`、`obj/`、`TestResults/`、IDE 和 Python 缓存。
- 引擎整合包、Python 运行环境、模型权重、FFmpeg 等二进制。
- 用户参考音频、合成结果、私人设置、日志、凭据和签名私钥。

`.gitignore` 已排除上述常见目录和格式。不要使用 `git add -f` 绕过这些规则；未来若加入测试素材，应单独审查授权和大小后再决定。

现有 GPT-SoVITS 整合包和用户音频位于本源码工作区之外，不需要复制进来。源码中只记录所需引擎资源与哈希；克隆仓库不会自动获得模型。没有兼容引擎时，可构建客户端并使用明确标记的模拟演示。

## 首次提交前检查

在项目根目录运行：

```powershell
git status --short
git add --dry-run .
```

确认清单只有上面列出的源码和文档后再暂存、查看并提交：

```powershell
git add .
git diff --cached --stat
git diff --cached --name-only
git commit -m "Initial VoiceLite client"
```

本次检查时本地分支为 `main`，尚无提交，也未配置远程。填写你实际创建的空仓库地址后才能上传；若远程已有 README 或其他提交，应先检查其历史再整合，不要强制推送覆盖：

```powershell
git remote add origin '替换为你的仓库地址'
git push -u origin main
```

本次只整理上传范围，没有执行暂存、提交或推送。

## 普通用户下载包

完整离线包应通过单独的发行附件或下载渠道提供，不进入 Git 提交历史。当前候选包尚待第三方许可核查、上游快照来源补证与干净 Windows 断网验收；这些限制针对包含引擎、模型及运行时的完整分发包，不能把本地候选包标成已通过公开发行验收。

相关证据见 [上游审计](UPSTREAM_AUDIT.md)、[测试报告](TEST_REPORT.md)、[离线包说明](OFFLINE_PACKAGE.md) 和 [第三方清单](../THIRD_PARTY_NOTICES.md)。
