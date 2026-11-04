using System.IO;
using System.Text.Json;
using System.Windows;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
namespace VoiceLite.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Arg(string name) { int i = Array.IndexOf(e.Args, name); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        bool smoke = e.Args.Contains("--smoke-ui");
        var dataRoot = Arg("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceLite");
        try
        {
            Directory.CreateDirectory(dataRoot);
            var settingsStore = new SettingsStore(dataRoot);
            var settings = await settingsStore.LoadAsync();
            var bundled = Path.Combine(AppContext.BaseDirectory, "engine");
            var engineRoot = Arg("--engine") ?? (Directory.Exists(bundled) ? bundled : settings.EngineRoot);
            var manager = new EngineProcessManager();
            var vm = new MainViewModel(new GenerationService(dataRoot, manager), new RoleLibrary(dataRoot)) {
                EngineRoot = engineRoot, ReferencePath = Arg("--reference") ?? settings.ReferencePath,
                PromptText = settings.PromptText, PromptLanguage = settings.PromptLanguage, TextLanguage = settings.TextLanguage,
                IsMock = e.Args.Contains("--mock"), Device = settings.Device
            };
            var auxiliaryArguments = new List<string>();
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] != "--aux-reference") continue;
                if (++i >= e.Args.Length || e.Args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new VoiceLiteException("--aux-reference需要录音文件路径。");
                auxiliaryArguments.Add(e.Args[i]);
            }
            vm.AddAuxiliaryReferences(auxiliaryArguments.Count > 0 ? auxiliaryArguments
                : Arg("--reference") is null ? settings.AuxiliaryReferencePaths : []);
            if (auxiliaryArguments.Count > 0 && (vm.Error.Length > 0 || vm.AuxiliaryReferences.Count != auxiliaryArguments.Count))
                throw new VoiceLiteException("辅助参考录音未全部加载，请检查文件是否存在、格式有效、没有重复且不超过3条。");
            if (Arg("--device") is { } device)
            {
                vm.Device = device.ToLowerInvariant() switch {
                    "auto" => ComputeDevice.Auto, "nvidia" => ComputeDevice.Nvidia, "cpu" => ComputeDevice.Cpu,
                    _ => throw new VoiceLiteException("无效的 --device 参数，请使用 auto、nvidia 或 cpu。")
                };
            }
            foreach (var flag in new[] { "--role-package", "--role" })
                if (e.Args.Contains(flag) && (Arg(flag) is not { Length: > 0 } argument || argument.StartsWith("--", StringComparison.Ordinal)))
                    throw new VoiceLiteException($"{flag} 需要角色包路径或角色标识。");
            if (Arg("--role-package") is not null && Arg("--role") is not null)
                throw new VoiceLiteException("请只使用 --role-package 或 --role 中的一个参数。");
            var requestedRole = Arg("--role");
            await vm.InitializeRolesAsync(Arg("--role-package") is not null || requestedRole == "custom" ? "" : requestedRole ?? settings.SelectedRoleId);
            if (requestedRole is not null && requestedRole != "custom" && vm.SelectedRole.Id != requestedRole)
                throw new VoiceLiteException("指定角色无法加载，请重新导入角色包。");
            if (Arg("--role-package") is { } rolePackage)
            {
                await vm.ImportRoleAsync(rolePackage);
                if (vm.Error.Length > 0) throw new VoiceLiteException(vm.Error);
            }
            var window = new MainWindow(vm, manager, settingsStore, dataRoot);
            MainWindow = window;
            if (smoke)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window.Loaded += async (_, _) => {
                    int code = 0;
                    try
                    {
                        if (vm.ReferencePath.Length > 0 && File.Exists(Path.ChangeExtension(vm.ReferencePath, ".json")))
                        {
                            using var reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(vm.ReferencePath, ".json")));
                            vm.PromptText = reference.RootElement.GetProperty("text").GetString()!;
                            vm.PromptLanguage = reference.RootElement.GetProperty("language").GetString()!;
                        }
                        vm.Text = "你好，这是 VoiceLite 的真实离线语音合成测试。";
                        await window.RunSmokeAsync(Arg("--playback-check"));
                    }
                    catch (Exception exception)
                    {
                        code = 1;
                        await File.WriteAllTextAsync(Path.Combine(dataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new { passed = false, error = exception.ToString() }));
                    }
                    finally { await window.CloseSafelyAsync(); Shutdown(code); }
                };
            }
            window.Show();
        }
        catch (VoiceLiteException exception)
        {
            if (smoke) await File.WriteAllTextAsync(Path.Combine(dataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new { passed = false, error = exception.Message }));
            else MessageBox.Show(exception.Message, "VoiceLite");
            Shutdown(1);
        }
        catch (Exception exception)
        {
            new AppLog(dataRoot).Write("startup", exception.GetType().Name);
            if (!smoke) MessageBox.Show("VoiceLite 无法启动，请检查数据目录的写入权限。", "VoiceLite");
            Shutdown(1);
        }
    }
}
