using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;

namespace VoiceLite.WorkflowProbe;

internal sealed record QualityPlan
{
    public required string EngineRoot { get; init; }
    public required string ReferencePath { get; init; }
    public required string PromptText { get; init; }
    public required string Text { get; init; }
    public required string DataRoot { get; init; }
    public string PromptLanguage { get; init; } = "zh";
    public string TextLanguage { get; init; } = "zh";
    public int Seed { get; init; } = 20261103;
    public IReadOnlyList<string> AuxiliaryReferences { get; init; } = [];
}

internal static class QualityComparison
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task RunAsync(string planPath, bool compareReferences = false)
    {
        var plan = JsonSerializer.Deserialize<QualityPlan>(await File.ReadAllTextAsync(planPath))
            ?? throw new InvalidOperationException("Comparison plan is missing.");
        WorkflowChecks.Require(!string.IsNullOrWhiteSpace(plan.Text) && plan.Text.Length <= 2000
            && !string.IsNullOrWhiteSpace(plan.PromptText), "Supply synthesis text and the reference's actual transcript.");
        WorkflowChecks.Require(plan.PromptLanguage is "zh" or "en" && plan.TextLanguage is "zh" or "en", "Unsupported language.");
        WorkflowChecks.Require(plan.Seed >= 0, "The comparison requires a fixed, nonnegative seed.");
        var auxiliaryInputs = compareReferences ? (plan.AuxiliaryReferences ?? []) : [];
        WorkflowChecks.Require(!compareReferences || auxiliaryInputs.Count is >= 1 and <= 3, "Supply 1-3 auxiliary reference recordings.");
        var (engine, data) = PhysicalDirectories.ValidateExperiment(plan.EngineRoot, plan.DataRoot);
        var reference = Path.GetFullPath(plan.ReferencePath);
        var originalHash = await HashAsync(reference);
        var auxiliarySources = new List<(string Path, string Hash)>();
        foreach (var path in auxiliaryInputs)
        {
            var full = Path.GetFullPath(path);
            WorkflowChecks.Require(!full.Equals(reference, StringComparison.OrdinalIgnoreCase)
                && !auxiliarySources.Any(x => x.Path.Equals(full, StringComparison.OrdinalIgnoreCase)), "Use different auxiliary recordings.");
            auxiliarySources.Add((full, await HashAsync(full)));
        }
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "comparison-input.json"), JsonSerializer.Serialize(plan, JsonOptions));
        await using var manager = new EngineProcessManager();
        var client = new TtsApiClient();
        var samples = new List<QualitySample>();
        string? normalized = null;
        var normalizedAuxiliary = new List<string>();
        Exception? failure = null;
        double startupSeconds = 0, warmupSeconds = 0, auxiliaryWarmupSeconds = 0;
        bool referenceUnchanged = false;
        bool auxiliaryUnchanged = false;
        try
        {
            var options = await new EngineDeviceResolver().ResolveAsync(engine, data, ComputeDevice.Nvidia);
            normalized = await new AudioImportService().ImportAsync(reference, Path.Combine(engine, "runtime/ffmpeg.exe"), Path.Combine(data, "references"), default);
            foreach (var source in auxiliarySources)
                normalizedAuxiliary.Add(await new AudioImportService().ImportAsync(source.Path, Path.Combine(engine, "runtime/ffmpeg.exe"), Path.Combine(data, "references"), default));
            Console.WriteLine("Loading the audited NVIDIA engine for the comparison...");
            var clock = Stopwatch.StartNew();
            var session = await manager.StartAsync(options);
            startupSeconds = clock.Elapsed.TotalSeconds;
            Task<byte[]> GenerateAsync(double temperature, bool auxiliary = false) => client.GenerateAsync(session,
                new(plan.Text, plan.TextLanguage, normalized, plan.PromptText, plan.PromptLanguage) {
                    UseGpu = true, Seed = plan.Seed, Temperature = temperature,
                    AuxiliaryReferencePaths = auxiliary ? normalizedAuxiliary : []
                }, options.RequestTimeout ?? TimeSpan.FromMinutes(3), default);
            // Reference features and AR acceleration are initialized lazily by this snapshot.
            Console.WriteLine("WARM-UP same input; output discarded before comparison samples.");
            clock.Restart();
            _ = await GenerateAsync(1);
            warmupSeconds = clock.Elapsed.TotalSeconds;
            var variants = compareReferences
                ? new[] { ("A-single", 1.0, false), ("A-repeat", 1.0, false), ("D-multi", 1.0, true), ("D-repeat", 1.0, true) }
                : new[] { ("A-default", 1.0, false), ("A-repeat", 1.0, false), ("B-temperature-0.8", 0.8, false), ("C-temperature-0.6", 0.6, false) };
            bool auxiliaryWarmed = false;
            foreach (var sample in variants)
            {
                if (sample.Item3 && !auxiliaryWarmed)
                {
                    Console.WriteLine("WARM-UP auxiliary reference condition; output discarded.");
                    clock.Restart(); _ = await GenerateAsync(1, true);
                    auxiliaryWarmupSeconds = clock.Elapsed.TotalSeconds; auxiliaryWarmed = true;
                }
                Console.WriteLine($"BEGIN {sample.Item1}; seed={plan.Seed}; temperature={sample.Item2}");
                clock.Restart();
                var bytes = await GenerateAsync(sample.Item2, sample.Item3);
                var wave = WaveInfo.Inspect(bytes);
                var output = Path.Combine(data, sample.Item1 + ".wav");
                var temporary = output + ".partial";
                try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, output); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                samples.Add(new(sample.Item1, sample.Item2, wave.DurationSeconds, wave.SampleRate,
                    clock.Elapsed.TotalSeconds, Convert.ToHexString(SHA256.HashData(bytes)), Path.GetFileName(output), sample.Item3 ? normalizedAuxiliary.Count : 0));
                Console.WriteLine($"PASS {sample.Item1}; duration={wave.DurationSeconds:F2}s");
            }
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            try { await manager.StopAsync(); }
            catch (Exception exception) { failure ??= exception; }
            var cleanup = await ComparisonCleanup.CompleteAsync(
                (normalized is null ? Enumerable.Empty<string>() : [normalized]).Concat(normalizedAuxiliary),
                new[] { (reference, originalHash) }.Concat(auxiliarySources).ToArray());
            referenceUnchanged = cleanup.PrimaryUnchanged;
            auxiliaryUnchanged = cleanup.AuxiliaryUnchanged;
            if (cleanup.Errors.Count > 0) failure ??= new AggregateException("Reference cleanup or verification failed.", cleanup.Errors);
            var report = new {
                passed = failure is null && samples.Count == 4, similarity_improvement_verified = false,
                comparison_kind = compareReferences ? "single_vs_multiple_references" : "temperature",
                requested_device = "NVIDIA CUDA", seed = plan.Seed, startup_seconds = startupSeconds,
                warmup_seconds = warmupSeconds, warmup_output_discarded = true,
                auxiliary_warmup_seconds = auxiliaryWarmupSeconds,
                reference_sha256 = originalHash, reference_unchanged = referenceUnchanged,
                auxiliary_reference_sha256 = auxiliarySources.Select(x => x.Hash).ToArray(), auxiliary_references_unchanged = auxiliaryUnchanged,
                same_parameter_repeat_byte_identical = samples.Count >= 2 && samples[0].Sha256 == samples[1].Sha256,
                auxiliary_repeat_byte_identical = compareReferences && samples.Count == 4 ? (bool?)(samples[2].Sha256 == samples[3].Sha256) : null,
                note = "Fixed seed reduces random variation; GPU execution may still vary. Listener comparison is required; no winner selected automatically.",
                samples, error = failure?.ToString()
            };
            await File.WriteAllTextAsync(Path.Combine(data, "comparison-report.json"), JsonSerializer.Serialize(report, JsonOptions));
        }
        if (failure is not null) throw new InvalidOperationException("Quality comparison failed; see comparison-report.json.", failure);
        Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(data, "comparison-report.json")));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed record QualitySample(string Label, double Temperature, double DurationSeconds,
        int SampleRate, double GenerationSeconds, string Sha256, string File, int AuxiliaryReferenceCount);
}
