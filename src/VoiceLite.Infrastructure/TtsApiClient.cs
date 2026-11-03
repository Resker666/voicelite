using VoiceLite.Core;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
namespace VoiceLite.Infrastructure;

public sealed class TtsApiClient(HttpMessageHandler? handler = null) : ITtsApiClient
{
    private readonly HttpMessageHandler? injectedHandler = handler;
    public async Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(request.PromptText) || !Path.IsPathFullyQualified(request.ReferencePath))
            throw new VoiceLiteException("请填写合成文字、参考文字并选择有效录音。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var client = LoopbackHttp.Create(session, injectedHandler);
            using var message = new HttpRequestMessage(HttpMethod.Post, "tts") { Content = JsonContent.Create(request) };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            session.VerifyOwnership();
            if (!response.IsSuccessStatusCode)
            {
                await using var errorStream = await response.Content.ReadAsStreamAsync(deadline.Token);
                var errorBuffer = new byte[8192]; int used = 0, read;
                while (used < errorBuffer.Length && (read = await errorStream.ReadAsync(errorBuffer.AsMemory(used), deadline.Token)) > 0) used += read;
                try
                {
                    using var error = JsonDocument.Parse(Encoding.UTF8.GetString(errorBuffer, 0, used));
                    if (error.RootElement.TryGetProperty("Exception", out var detail) && detail.ValueKind == JsonValueKind.String)
                    {
                        var diagnostic = detail.GetString()!;
                        if (diagnostic.Contains("DefaultCPUAllocator", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("MemoryError", StringComparison.Ordinal))
                            throw new VoiceLiteException("系统内存不足，请关闭其他占用内存的程序，缩短文字后重新生成。");
                        if (diagnostic.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
                            throw new VoiceLiteException(request.UseGpu ? "显存不足，请关闭其他占用显卡的程序，缩短文字后重新生成。" : "系统内存不足，请关闭其他占用内存的程序，缩短文字后重新生成。");
                    }
                }
                catch (JsonException) { }
                throw new VoiceLiteException($"引擎合成失败（HTTP {(int)response.StatusCode}）。请检查参考录音、模型与{(request.UseGpu ? "显存" : "系统内存")}；详细状态见诊断日志。");
            }
            if (response.Content.Headers.ContentType?.MediaType is not ("audio/wav" or "audio/x-wav"))
                throw new VoiceLiteException("引擎未返回 WAV 音频，结果没有保存。");
            const int limit = 64 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new VoiceLiteException("返回音频过大，请缩短文字后重试。");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (output.Length + count > limit) throw new VoiceLiteException("返回音频过大，请缩短文字后重试。");
                output.Write(buffer, 0, count);
            }
            var data = output.ToArray();
            WaveInfo.Inspect(data);
            return data;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new VoiceLiteException(request.UseGpu ? "合成超时，请缩短文字或检查显存后重试。" : "CPU 合成超时，请缩短文字或检查系统可用内存后重试。"); }
        catch (HttpRequestException exception) { throw new VoiceLiteException("无法连接本应用的引擎，可能已异常退出，请重新生成。", exception); }
    }
}
