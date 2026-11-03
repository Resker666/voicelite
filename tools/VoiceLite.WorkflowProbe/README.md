# 本机真实取消与恢复验收

这是手动运行的验收工具，复用产品的 WPF 窗口、ViewModel、受控引擎和 HTTP 客户端。它会打开专用测试窗口，在真实 `/tts` 请求未完成时执行取消、主动结束自己启动的引擎、重新生成以及关闭窗口。正常 `dotnet test VoiceLite.sln` 不会运行模型。

需要 Windows x64、可用 NVIDIA CUDA、匹配当前 API 哈希的完整引擎、项目固定的 .NET SDK，以及一段3–10秒的 WAV 参考录音。使用与录音同名的 JSON 旁文件填写实际参考文字，例如 `reference.wav` 对应 `reference.json`：

```json
{ "text": "这里填写录音实际说出的文字", "language": "zh" }
```

工具固定使用 NVIDIA，设备不可用时失败，不自动改用 CPU。测试数据必须是引擎之外的新目录；目录链接被拒绝，路径比较前通过 Windows 目录句柄规范化别名。当前工具要求本地驱动器路径。

## 运行

在仓库根目录，按主 README 配置项目内 `DOTNET_ROOT`、`DOTNET_CLI_HOME` 和 `NUGET_PACKAGES` 后执行：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" restore tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --locked-mode --source "$env:NUGET_PACKAGES"
& "$env:DOTNET_ROOT\dotnet.exe" build tools/VoiceLite.WorkflowProbe/VoiceLite.WorkflowProbe.csproj --no-restore --disable-build-servers -m:1
& "$env:DOTNET_ROOT\dotnet.exe" tools/VoiceLite.WorkflowProbe/bin/Debug/net8.0-windows/VoiceLite.WorkflowProbe.dll '<完整engine目录>' '<参考.wav>' '<新的测试数据目录>'
```

工具在同一测试窗口中按顺序验证：

1. 真实 `/tts` 请求发起1.5秒后仍未完成时，执行与取消按钮绑定相同的命令；等待引擎退出、端口释放和临时文件清理。
2. 取消后重新启动引擎，生成有效 WAV 并导出。
3. 真实请求未完成时核对端口归属、PID及可执行文件，持有原进程句柄，主动终止本次测试的引擎；检查界面错误与清理结果。
4. 异常后重新启动引擎，再次生成并导出有效 WAV。
5. 在真实请求未完成时调用窗口关闭，执行实际 WPF `OnClosing` 路径，确认取消状态、窗口关闭、进程退出和端口释放。

结果保存在测试目录的 `workflow-report.json`；两次成功的导出为 `after-cancel.wav`、`after-crash.wav`。参考录音执行前后做 SHA-256 比较。每个场景有时限；失败时收尾并留下诊断。需要物理鼠标点击或其他设备覆盖时，应另外记录，不能把本工具结果描述为这些验证。

## 工具自身的路径边界回归

下列测试使用临时假引擎，不加载模型。显式提供已构建工具和 SDK 后，Python 测试覆盖普通嵌套路径、末尾斜杠、目录 junction、两种扩展路径别名组合及合法外部目录：

```powershell
$env:VOICELITE_WORKFLOW_PROBE_DOTNET = "$env:DOTNET_ROOT\dotnet.exe"
$env:VOICELITE_WORKFLOW_PROBE_DLL = "$PWD\tools\VoiceLite.WorkflowProbe\bin\Debug\net8.0-windows\VoiceLite.WorkflowProbe.dll"
& '<引擎目录>\runtime\python.exe' -B -m unittest discover -s tools -p 'test_*.py' -v
```

未设置这两个变量时，6项Windows工具边界测试会明确跳过，其余Python测试照常运行。
