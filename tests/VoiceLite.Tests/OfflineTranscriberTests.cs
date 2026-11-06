using System.Reflection;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class OfflineTranscriberTests
{
    private static Type BridgeType { get { var type = typeof(GenerationService).Assembly.GetType("VoiceLite.Infrastructure.OfflineTranscriber"); Assert.NotNull(type); return type; } }

    [Fact] public async Task Missing_resources_fail_before_starting_a_model_process()
    {
        using var folder = new EngineFixture();
        dynamic bridge = Activator.CreateInstance(BridgeType, folder.Root)!;
        var error = await Assert.ThrowsAsync<VoiceLiteException>(async () => await bridge.TranscribeAsync(folder.Root,
            new[] { new TranscriptionInput(Guid.NewGuid().ToString("N"), "missing.wav") }, new Progress<string>(), CancellationToken.None));
        Assert.Contains("转写", error.Message);
        Assert.False(Directory.Exists(Path.Combine(folder.Root, "asr-jobs")));
    }

    [Theory] [InlineData("foreign")] [InlineData("unsafe|text")] [InlineData("")]
    public void Worker_results_reject_unrequested_ids_and_unsafe_drafts(string text)
    {
        var id = Guid.NewGuid().ToString("N");
        var json = System.Text.Json.JsonSerializer.Serialize(new { schema = "voicelite-asr-drafts-1", human_verified = false,
            drafts = new[] { new { id = text == "foreign" ? "foreign" : id, text, language = "zh" } }, issues = Array.Empty<object>() });
        var method = BridgeType.GetMethod("ParseResult", BindingFlags.Static | BindingFlags.NonPublic); Assert.NotNull(method);
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [json, new[] { new TranscriptionInput(id, "C:\\one.wav") }]));
        Assert.IsType<VoiceLiteException>(exception.InnerException);
    }
}
