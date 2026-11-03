using System.Diagnostics;
using System.Text.Json;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

public sealed class EngineDeviceResolver : IEngineDeviceResolver
{
    private readonly Func<string, CancellationToken, Task<bool>> probe;
    public EngineDeviceResolver() : this(ProbeCudaAsync) { }
    internal EngineDeviceResolver(Func<string, CancellationToken, Task<bool>> probe) => this.probe = probe;

    public async Task<EngineOptions> ResolveAsync(string engineRoot, string dataRoot, ComputeDevice device, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(device)) throw new VoiceLiteException("无效的计算设备，请重新选择。");
        if (string.IsNullOrWhiteSpace(engineRoot) || !Directory.Exists(engineRoot))
            throw new VoiceLiteException("未找到引擎，请在运行设置中选择完整 engine 目录。");
        bool useGpu = device != ComputeDevice.Cpu && await probe(engineRoot, cancellationToken);
        if (device == ComputeDevice.Nvidia && !useGpu)
            throw new VoiceLiteException("包内 PyTorch 无法使用 NVIDIA CUDA。请检查显卡驱动，或选择自动 / CPU。");
        return new(Path.GetFullPath(engineRoot), Path.GetFullPath(dataRoot),
            StartupTimeout: TimeSpan.FromMinutes(useGpu ? 3 : 5),
            RequestTimeout: TimeSpan.FromMinutes(useGpu ? 3 : 10), UseGpu: useGpu);
    }

    private static async Task<bool> ProbeCudaAsync(string engineRoot, CancellationToken cancellationToken)
    {
        var python = Path.Combine(engineRoot, "runtime/python.exe");
        if (!File.Exists(python)) throw new VoiceLiteException("引擎缺少 Python，请检查离线包是否完整。");
        var info = new ProcessStartInfo(python) { WorkingDirectory = engineRoot };
        info.ArgumentList.Add("-B"); info.ArgumentList.Add("-c");
        info.ArgumentList.Add("import sys,json; sys.path.insert(0,sys.argv[1]); from tools.portable_runtime import _prepare_dlls; _prepare_dlls(); import torch; print(json.dumps({'cuda_available':torch.cuda.is_available()}))");
        info.ArgumentList.Add(Path.GetFullPath(engineRoot));
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        info.Environment["HF_HUB_OFFLINE"] = "1";
        info.Environment["TRANSFORMERS_OFFLINE"] = "1";
        try
        {
            var result = await ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(60), cancellationToken);
            if (result.ExitCode != 0) throw new VoiceLiteException("包内 PyTorch 设备检测失败，请检查引擎运行环境。尚未加载声音模型。");
            var line = result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            using var json = JsonDocument.Parse(line ?? "");
            return json.RootElement.GetProperty("cuda_available").GetBoolean();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new VoiceLiteException("无法读取 PyTorch 设备检测结果，请检查引擎运行环境。", exception); }
        catch (VoiceLiteException exception)
        { throw new VoiceLiteException("计算设备检测未完成。请检查引擎完整性和运行权限后重试。", exception); }
    }
}
