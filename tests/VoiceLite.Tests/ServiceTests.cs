using System.Net;
using System.Text;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class ServiceTests
{
    [Fact] public void Wave_reports_duration_and_rejects_truncated_data()
    {
        var wav = Fixtures.Wav();
        var info = WaveInfo.Inspect(wav);
        Assert.Equal(32000, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Equal(1, info.DurationSeconds);
        Assert.Throws<VoiceLiteException>(() => WaveInfo.Inspect(wav.AsSpan(0, 50)));
    }
    [Theory]
    [InlineData("{\"error\":\"not audio\"}")]
    [InlineData("RIFF0000WAVE")]
    public void Wave_rejects_invalid_output(string content) =>
        Assert.Throws<VoiceLiteException>(() => WaveInfo.Inspect(Encoding.UTF8.GetBytes(content)));

    [Fact] public async Task Missing_engine_reports_blockers_without_creating_it()
    {
        var missing = Path.Combine(Path.GetTempPath(), "VoiceLite-missing-" + Guid.NewGuid());
        var report = await new EnvironmentProbe().InspectAsync(new(missing, missing + "-data"));
        Assert.False(report.IsReady);
        Assert.Contains(report.Items, x => !x.Passed);
        Assert.False(Directory.Exists(missing));
    }
    [Fact] public async Task Client_emits_real_upstream_contract_and_returns_verified_wave()
    {
        string? body = null;
        Uri? url = null;
        var handler = new RecordingHandler(async request => {
            body = await request.Content!.ReadAsStringAsync(); url = request.RequestUri;
            Assert.Equal(HttpMethod.Post, request.Method);
            return Fixtures.Response(Fixtures.Wav(), "audio/wav");
        });
        var result = await new TtsApiClient(handler).GenerateAsync(new Lease(), Fixtures.Request(), TimeSpan.FromSeconds(5), default);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("你好", json.RootElement.GetProperty("text").GetString());
        Assert.Equal("zh", json.RootElement.GetProperty("text_lang").GetString());
        Assert.Equal("C:\\参考 声音.wav", json.RootElement.GetProperty("ref_audio_path").GetString());
        Assert.Equal("参考文字", json.RootElement.GetProperty("prompt_text").GetString());
        Assert.Equal("zh", json.RootElement.GetProperty("prompt_lang").GetString());
        Assert.False(json.RootElement.GetProperty("streaming_mode").GetBoolean());
        Assert.Equal("wav", json.RootElement.GetProperty("media_type").GetString());
        Assert.Equal("/tts", url!.AbsolutePath);
        Assert.Equal(Fixtures.Wav(), result);
    }
    [Fact] public async Task Unknown_port_owner_gets_no_request()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Fixtures.Response(Fixtures.Wav(), "audio/wav")));
        await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(handler).GenerateAsync(new Lease(false), Fixtures.Request(), TimeSpan.FromSeconds(5), default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task Client_refuses_non_loopback_even_with_a_lease()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Fixtures.Response(Fixtures.Wav(), "audio/wav")));
        await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(handler).GenerateAsync(new Lease(true, new Uri("http://192.0.2.1:9880")), Fixtures.Request(), TimeSpan.FromSeconds(1), default));
        Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData(200, "application/json")] [InlineData(400, "application/json")] [InlineData(302, "text/html")]
    public async Task Client_does_not_accept_errors_as_wave(int status, string type)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"message\":\"tts failed\"}", Encoding.UTF8, type) }));
        await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(handler).GenerateAsync(new Lease(), Fixtures.Request(), TimeSpan.FromSeconds(1), default));
    }
    [Fact] public async Task Client_honors_cancel_and_timeout()
    {
        // The handler below models an external server that never replies, honoring transport cancellation.
        var slow = new NeverReplyHandler();
        using var cancel = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TtsApiClient(slow).GenerateAsync(new Lease(), Fixtures.Request(), TimeSpan.FromSeconds(5), cancel.Token));
        await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(slow).GenerateAsync(new Lease(), Fixtures.Request(), TimeSpan.FromMilliseconds(50), default));
    }
    [Fact] public async Task Gpu_oom_is_explained_without_exposing_engine_paths()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) {
            Content = new StringContent("{\"message\":\"tts failed\",\"Exception\":\"CUDA out of memory at E:/private/audio.wav\"}", Encoding.UTF8, "application/json")
        }));
        var exception = await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(handler).GenerateAsync(new Lease(), Fixtures.Request(), TimeSpan.FromSeconds(2), default));
        Assert.Contains("显存不足", exception.Message);
        Assert.DoesNotContain("private", exception.Message);
    }
}

internal static class Fixtures
{
    internal static TtsRequest Request() => new("你好", "zh", "C:\\参考 声音.wav", "参考文字", "zh");
    internal static byte[] Wav(int seconds = 1)
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        var bytes = seconds * 32000 * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(32000); writer.Write(64000);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
        writer.Write(new byte[bytes]); return buffer.ToArray();
    }
    internal static HttpResponseMessage Response(byte[] bytes, string type)
    {
        var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        result.Content.Headers.ContentType = new(type); return result;
    }
}
internal sealed class Lease(bool owned = true, Uri? endpoint = null) : IEngineSession
{
    public Uri Endpoint => endpoint ?? new("http://127.0.0.1:19880");
    public void VerifyOwnership() { if (!owned) throw new VoiceLiteException("未知进程"); }
}
internal sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    internal int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return respond(request); }
}
internal sealed class NeverReplyHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
}
