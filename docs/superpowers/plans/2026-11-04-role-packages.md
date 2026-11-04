# 角色包导入与选择实现计划

用户已选择 E（原 GPT + 微调 SoVITS），并明确要求执行“导入角色包 → 选择角色 → 生成”。沿用当前工作区、WPF/MVVM、受控 loopback 引擎和离线依赖；不自动提交或推送。使用 writing-plans、test-driven-development 的步骤在本会话实现，最终做一次独立复审。

## 设计与范围

支持已导出的 `voicelite-role-experiment-0` ZIP：根 `character.json`、两组权重、主参考、最多三条辅助及可选 README。导入成功后复制到数据目录 `roles/<随机ID>`，保留 manifest；列表显示名称/版本。选择角色自动填入参考、文字和语言，合成文字保留；切回“自定义参考声音”恢复原自定义输入。生成/导入期间禁用角色切换，导入可取消，关闭等待收尾。设置只记录选中角色 ID，旧设置兼容。

两种引擎接法：运行中调用 set_weights，或者启动配置指定两组权重。选择后者：`EngineOptions.Weights` 参与已有 options 相等判断，切换角色/切回底模自动结束旧 Job 并启动新进程，避免半切换两组权重。已有 api_v2 启动配置支持该方式，无新增服务端接口。

只支持已审计 v2ProPlus/API SHA；仅提取 manifest 声明的权重和参考文件。校验受限大小、必填字段、唯一相对路径、文件长度/SHA、Windows 路径别名、ZIP 链接、未知文件；失败/取消清理本次 staging，不覆盖旧角色。角色文件加载可能执行模型内容，界面注明仅导入来源可信的包，SHA 只用于完整性。生成前通过库 ID 重新验证文件，拒绝缺失、损坏或路径链接。

## Task 1: 角色库与数据契约

Files: `Core/VoiceRole.cs`, `Core/Contracts.cs`, `Infrastructure/RoleLibrary.cs`, `tests/VoiceLite.Tests/RolePackageTests.cs`。

接口：`ModelWeights(GptPath, SovitsPath)`；`VoiceRole` 包含 ID、名称/版本、参考/文字/语言/辅助和权重；`IRoleLibrary.ListAsync/ImportAsync/ValidateAsync` 返回安装后的角色。`EngineOptions.Weights` 可空，`GenerationInput.RoleId` 可空。

- 先写可编译的反射入口行为测试，看到新功能缺失失败；再实现。
- 真 ZIP 往返导入/列表/验证；覆盖越界/绝对路径/大小写重复/链接、文件 SHA 和长度不符、版本不兼容、未声明文件、已安装缺失/改动/链接及取消不留 staging。测试仅使用微小假权重，不运行模型。
- 已有 E ZIP 用同一库真实导入与逐文件 SHA 校验。

## Task 2: 生成加载角色权重

Files: `GenerationService.cs`, `EngineLayout.cs`, `EnvironmentProbe.cs`, `RoleGenerationTests.cs`。

- 测试先失败：选中角色 → 校验角色 → 引用角色自带参考 → 启动 options 指向两组角色权重 → 生成；切回自定义 options 权重为 null；角色校验失败不得启动/发送生成；Mock 不加载角色模型。
- 服务通过 RoleId 校验，读取角色默认参考，给 EngineOptions.Weights 赋值；现有管理器按 options 切换进程。配置和资源检查反映两组角色权重，日志记录角色 ID 和实际配置权重路径。

## Task 3: WPF/MVVM 与设置

Files: `MainViewModel.cs`, `MainWindow.xaml/.cs`, `App.xaml.cs`, `SettingsStore.cs`, `RoleViewModelTests.cs`。

- 测试先失败：导入自动选中、填入主/辅助/文字/语言、保留合成文字；切回自定义恢复；忙时不能切换；导入失败/取消旧选择保留；设置旧版兼容且选中 ID 恢复、缺失角色提示并回退。
- 界面增加角色下拉与“导入角色包”；选中角色时参考卡只读，其他生成/试听/保存流程复用。导入的 Busy/Cancel 和关闭等待覆盖新任务；新增 `--role-package` / `--role` 用于同路径烟测，不绕过验证。

## Task 4: 交付验证

- 新增回归与全 C#、Python 测试通过，`dotnet build` 0 警告/错误；版本 0.1.4，更新项目引用 lock 后 locked restore。
- 发布客户端，用 E 包真实 GUI 导入/生成/导出/播放；新数据目录/重启恢复角色；自定义和 Mock 回归；检查截图、进程退出与临时文件清理。
- 一次独立复审后处理重要问题，必要的修复先 RED→GREEN。使用原打包/应用工具制作累积客户端更新，备份后安装到本机日常目录（若程序占用则等用户关闭），不更换引擎底模或打包用户模型。
- 更新使用说明、TEST_REPORT、IMPLEMENTATION_PROGRESS 和训练记录中的 GUI 状态。记录实际命令/失败/结果，无真实运行的验收不得标记通过。

## Review Focus

1. ZIP 路径/重复/链接/膨胀不能向角色库外写入，拒绝前后均不影响已有角色。
2. 生成前角色文件损坏或被替换链接，不得启动模型或落入未验证路径。
3. 默认/角色 A/角色 B 往返时两组权重同时属于所选角色，不沿用上一角色。
4. 导入/生成中关闭、取消、失败只清理本次 staging/进程，任务全部收尾。
5. 设置引用失效、旧版设置、角色/自定义往返保留输入，Mock 明确标记未运行模型。

Ruling: 用户已明确要求直接在当前工作区执行，已有未提交训练记录保留，不另建工作树、不自动 commit。设计和执行选择来自已接受的界面流程与本地锁定源码，无额外确认步骤。
