using VoiceLite.Core;
using System.Security.Cryptography;
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
        items.Add(new("计算设备", true, options.UseGpu ? "已选择 NVIDIA CUDA；实际 CUDA 可用性由包内 PyTorch 检测。" : "已选择 CPU / FP32，不要求 NVIDIA 驱动。"));
        return new(items);
    }
}
