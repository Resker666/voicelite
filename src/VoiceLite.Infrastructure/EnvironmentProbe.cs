using VoiceLite.Core;
using System.Security.Cryptography;
using System.Diagnostics;
namespace VoiceLite.Infrastructure;

public sealed class EnvironmentProbe : IEnvironmentProbe
{
    public async Task<ProbeReport> InspectAsync(EngineOptions options, CancellationToken cancellationToken = default)
    {
        var items = new List<ProbeItem>();
        if (string.IsNullOrWhiteSpace(options.EngineRoot) || !Directory.Exists(options.EngineRoot))
            return new([new("引擎目录", false, "未找到引擎。完整离线包应包含 engine 文件夹；开发时可选择已有整合包。")]);
        foreach (var relative in EngineLayout.RequiredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(Path.Combine(options.EngineRoot, relative));
            items.Add(new(relative, file.Exists && file.Length > 0, file.Exists && file.Length > 0 ? "已找到" : "缺少资源，请重新获取完整离线包。"));
        }
        var api = Path.Combine(options.EngineRoot, "api_v2.py");
        if (File.Exists(api))
        {
            await using var stream = File.OpenRead(api);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            items.Add(new("引擎版本", hash == EngineLayout.ApiSha256, hash == EngineLayout.ApiSha256 ? "匹配已审计的本地 API 快照" : "API 与本版本验证的快照不同，请使用匹配的离线包。"));
        }
        if (options.UseGpu && items.All(x => x.Passed))
        {
            var gpuTool = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (!File.Exists(gpuTool)) items.Add(new("NVIDIA 驱动", false, "未找到 NVIDIA 驱动工具；首版请使用具备可用 NVIDIA 驱动的电脑。CPU 模式尚未验收。"));
            else
            {
                try
                {
                    var info = new ProcessStartInfo(gpuTool);
                    info.ArgumentList.Add("--query-gpu=name,driver_version"); info.ArgumentList.Add("--format=csv,noheader");
                    var gpu = await ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(10), cancellationToken);
                    items.Add(new("NVIDIA 驱动", gpu.ExitCode == 0 && !string.IsNullOrWhiteSpace(gpu.Output), gpu.ExitCode == 0 ? "已识别 GPU/驱动；CUDA 和显存将在模型加载时验证。" : "无法访问 NVIDIA GPU，请检查驱动。"));
                }
                catch (VoiceLiteException) { items.Add(new("NVIDIA 驱动", false, "显卡检测未完成，请检查 NVIDIA 驱动后重试。")); }
            }
        }
        return new(items);
    }
}
