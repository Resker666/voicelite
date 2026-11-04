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

未设置这两个变量时，8项Windows工具边界测试会明确跳过，其余Python测试照常运行；其中一项覆盖下面对照模式的输出目录保护，另一项使用Desktop Mock验证显式传入的无效辅助路径会在推理前被拒绝。

## 固定种子的音色对照

`--quality`模式不打开窗口，使用同一参考、同一句文案和同一种子依次生成温度1.0、同参数重复、0.8、0.6四段WAV。先用同一输入预热并丢弃输出，避免把首次特征/解码器初始化开销放入样本。仅连接本工具拥有的本地引擎，结束后回收；运行前先关闭日常VoiceLite以释放显存。

准备私人JSON清单（不要将用户音频和含个人文本的实验清单提交Git）：

```json
{
  "EngineRoot": "D:/VoiceLite/engine",
  "ReferencePath": "D:/my-audio/reference.wav",
  "PromptText": "参考录音实际说出的文字",
  "Text": "你好，今天我们一起出去走走，顺便看看有什么有趣的事情吧。",
  "DataRoot": "D:/VoiceLite-experiments/new-comparison",
  "Seed": 20261103,
  "PromptLanguage": "zh",
  "TextLanguage": "zh"
}
```

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" tools/VoiceLite.WorkflowProbe/bin/Debug/net8.0-windows/VoiceLite.WorkflowProbe.dll --quality '<私人清单.json>'
```

DataRoot必须是引擎之外的新目录，同样检查路径别名和目录链接。输出包括`A-default.wav`、`A-repeat.wav`、`B-temperature-0.8.wav`、`C-temperature-0.6.wav`、输入副本和`comparison-report.json`。报告记录重复音频的哈希是否相同、参考SHA、参数和耗时；GPU跨设备/跨运行仍可能存在差异。

客户端请求类型新增`seed`、`temperature`，默认值仍为本地锁定API的-1和1。GUI尚未加入这两个控制项。生成成功表示输出有效，哪段更像要由用户试听；工具不自动选择“最好”的温度，也不把单次耗时差异当作温度性能结论。

## 单参考与多参考对照

在相同清单中增加`AuxiliaryReferences`数组，填写1–3条不同的同声线录音路径（每条3–10秒），并为本次实验指定新的`DataRoot`。只有主参考需要`PromptText`，辅助录音不需要填写文字：

```json
"AuxiliaryReferences": ["D:/my-audio/auxiliary-1.ogg", "D:/my-audio/auxiliary-2.wav"]
```

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" tools/VoiceLite.WorkflowProbe/bin/Debug/net8.0-windows/VoiceLite.WorkflowProbe.dll --references '<私人清单.json>'
```

两组都使用同一主参考、参考文字、合成文案、种子和温度1.0。单参考与多参考条件各自预热并丢弃预热输出，随后输出`A-single.wav`、`A-repeat.wav`、`D-multi.wav`、`D-repeat.wav`。报告记录各组重复是否逐字节一致以及全部输入SHA是否保持不变；依旧由用户听选，不自动声明相似度提高。
