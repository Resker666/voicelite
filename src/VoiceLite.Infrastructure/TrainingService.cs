using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceLite.Core;

namespace VoiceLite.Infrastructure;

public sealed class TrainingService : ITrainingService
{
    private readonly string root;
    private readonly IDatasetService datasets;
    private readonly Func<string, string[], IProgress<string>, CancellationToken, Task> runCommand;
    public TrainingService(string dataRoot) : this(dataRoot, new DatasetService(dataRoot), RunCommandAsync) { }
    internal TrainingService(string dataRoot, IDatasetService datasets, Func<string, string[], IProgress<string>, CancellationToken, Task> runCommand)
    { root = Path.GetFullPath(Path.Combine(dataRoot, "training")); this.datasets = datasets; this.runCommand = runCommand; }
    private static void Id(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new VoiceLiteException("训练任务或素材标识无效。"); }
    private string Folder(string id) { Id(id); return Path.Combine(root, id); }
    private FileStream Lease()
    {
        Directory.CreateDirectory(root); DatasetPaths.Plain(root);
        var path = Path.Combine(root, ".active.lock"); if (File.Exists(path)) DatasetPaths.Plain(path);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new VoiceLiteException("已有训练或角色导出任务正在运行，请等待或取消该任务。", exception); }
    }
    private bool Active()
    {
        var path = Path.Combine(root, ".active.lock"); if (!File.Exists(path)) return false;
        try { DatasetPaths.Plain(path); using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
    }
    private async Task SaveAsync(TrainingJob job)
    {
        var folder = Folder(job.Id); DatasetPaths.Plain(folder);
        var path = Path.Combine(folder, "job.json"); if (File.Exists(path)) DatasetPaths.Plain(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(job, DatasetPaths.Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<TrainingJob> ReadAsync(string id, CancellationToken token)
    {
        var path = Path.Combine(Folder(id), "job.json"); DatasetPaths.Plain(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new VoiceLiteException("训练任务清单过大。");
        var job = JsonSerializer.Deserialize<TrainingJob>(await File.ReadAllTextAsync(path, token), DatasetPaths.Json);
        if (job is null || job.Id != id || !Guid.TryParseExact(job.DatasetId, "N", out _) || string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.EngineRoot) || job.References is null || job.References.Count > 500 || job.References.Any(x => x is null)
            || job.Name.Length > 100 || job.Name.Any(char.IsControl) || !Enum.IsDefined(job.State) || job.EpochsCompleted is < 0 or > 100)
            throw new VoiceLiteException("训练任务清单损坏。");
        return job;
    }
    public async Task<IReadOnlyList<TrainingJob>> ListAsync(string datasetId, CancellationToken cancellationToken = default)
    {
        Id(datasetId); var jobs = new List<TrainingJob>(); if (!Directory.Exists(root)) return jobs;
        DatasetPaths.Plain(root); bool active = Active();
        foreach (var folder in Directory.GetDirectories(root).OrderDescending(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested(); var id = Path.GetFileName(folder); if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                var job = await ReadAsync(id, cancellationToken); if (job.DatasetId != datasetId) continue;
                if (job.State == TrainingJobState.Preparing && !active) job = job with { State = TrainingJobState.Interrupted, Error = "上次任务未正常结束，输入、日志与检查点已保留；可新建任务重试。" };
                if (job.State == TrainingJobState.Completed)
                {
                    try { job = await ReadResultAsync(job, cancellationToken); }
                    catch (Exception exception) when (exception is not OperationCanceledException) { job = job with { State = TrainingJobState.Failed, References = [], Error = "训练结果缺失或损坏，请检查日志并新建任务重试。" }; }
                }
                jobs.Add(job);
            }
            catch (Exception exception) when (exception is VoiceLiteException or IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return jobs.OrderByDescending(x => x.CreatedUtc).ToArray();
    }
    public async Task<TrainingJob> StartAsync(string datasetId, string name, string engineRoot, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Id(datasetId);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.Any(char.IsControl)) throw new VoiceLiteException("请填写80字以内的训练版本名称。");
        if (string.IsNullOrWhiteSpace(engineRoot)) throw new VoiceLiteException("请选择包含训练资源的完整GPT-SoVITS目录。");
        engineRoot = Path.GetFullPath(engineRoot);
        if (DatasetPaths.IsInside(engineRoot, root)) throw new VoiceLiteException("训练工作目录不能位于原引擎目录内。");
        using var lease = Lease();
        var dataset = await datasets.LoadAsync(datasetId, cancellationToken);
        if (dataset.Clips.Count(x => x.Included && x.Reviewed) < 2) throw new VoiceLiteException("至少需要两条已启用且逐字校对的录音；请先保存校对。");
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < 6L * 1024 * 1024 * 1024) throw new VoiceLiteException("训练工作盘至少需要6 GiB可用空间，请先腾出空间。");
        var job = new TrainingJob(Guid.NewGuid().ToString("N"), datasetId, name.Trim(), engineRoot, TrainingJobState.Preparing, 0, []);
        var folder = Folder(job.Id); Directory.CreateDirectory(folder); DatasetPaths.Plain(folder); await SaveAsync(job);
        var work = Path.Combine(folder, "work");
        try
        {
            progress.Report("正在复制并校验已校对素材…");
            var input = await datasets.ExportAsync(datasetId, Path.Combine(folder, "inputs"), cancellationToken);
            await runCommand(engineRoot, ["init", "--engine", engineRoot, "--dataset", input, "--work", work], progress, cancellationToken);
            progress.Report("正在准备文字与声音特征…");
            await runCommand(engineRoot, ["features", "--work", work], progress, cancellationToken);
            progress.Report("正在训练新版本（8轮），完成后请试听确认效果…");
            await runCommand(engineRoot, ["sovits", "--work", work, "--epochs", "8"], progress, cancellationToken);
            job = await ReadResultAsync(job, cancellationToken); await SaveAsync(job); return job;
        }
        catch (OperationCanceledException) { await SaveAsync(job with { State = TrainingJobState.Cancelled, Error = "任务已取消，输入、日志与检查点保留。" }); throw; }
        catch (Exception exception)
        {
            var error = exception is VoiceLiteException ? exception.Message : "训练未完成，请检查完整资源、显存与任务日志。";
            await SaveAsync(job with { State = TrainingJobState.Failed, Error = error });
            throw new VoiceLiteException(error, exception);
        }
    }
    private async Task<TrainingJob> ReadResultAsync(TrainingJob job, CancellationToken token)
    {
        var work = Path.Combine(Folder(job.Id), "work"); var resultFile = Path.Combine(work, "result.json"); DatasetPaths.Plain(resultFile);
        if (new FileInfo(resultFile).Length > 2 * 1024 * 1024) throw new VoiceLiteException("训练结果清单过大。");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, token)); var json = result.RootElement;
        var runPath = Path.Combine(work, "run.json"); DatasetPaths.Plain(runPath);
        if (new FileInfo(runPath).Length > 2 * 1024 * 1024) throw new VoiceLiteException("训练运行清单过大。");
        using var run = JsonDocument.Parse(await File.ReadAllTextAsync(runPath, token));
        if (json.GetProperty("schema").GetString() != "voicelite-training-result-1" || json.GetProperty("epochs_completed").GetInt32() != 8 || run.RootElement.GetProperty("epochs_completed").GetInt32() != 8)
            throw new VoiceLiteException("训练尚未完成目标8轮。");
        var weightFolder = Path.GetFullPath(Path.Combine(work, "weights/SoVITS"));
        var weight = Path.GetFullPath(Path.Combine(work, json.GetProperty("sovits_path").GetString()!)); DatasetPaths.Plain(weight);
        if (Path.GetDirectoryName(weight) != weightFolder || !weight.EndsWith(".pth", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(weight).Contains("_e8_", StringComparison.Ordinal)
            || new FileInfo(weight).Length != json.GetProperty("sovits_bytes").GetInt64()) throw new VoiceLiteException("目标轮次的模型文件无效。");
        await using (var stream = File.OpenRead(weight))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(json.GetProperty("sovits_sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new VoiceLiteException("模型摘要校验失败。");
        foreach (var name in new[] { "G_233333333333.pth", "D_233333333333.pth" })
        { var path = Path.Combine(work, "features/logs_s2_v2ProPlus", name); DatasetPaths.Plain(path); if (new FileInfo(path).Length < 1) throw new VoiceLiteException("完整训练检查点缺失。"); }
        var references = json.GetProperty("references").EnumerateArray().Select(x => new TrainingReference(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString()!, x.GetProperty("seconds").GetDouble(), x.GetProperty("text").GetString()!, x.GetProperty("language").GetString()!)).ToArray();
        if (references.Length > 500 || references.Any(x => !Guid.TryParseExact(x.Id,"N",out _) || !double.IsFinite(x.Seconds) || x.Seconds is < 3 or > 10 || string.IsNullOrWhiteSpace(x.Text) || x.Language is not ("zh" or "en"))) throw new VoiceLiteException("训练参考清单无效。");
        return job with { State = TrainingJobState.Completed, EpochsCompleted = 8, References = references, Error = "" };
    }
    public async Task<string> ExportRoleAsync(string jobId, string referenceId, string output, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        using var lease = Lease(); var job = await ReadAsync(jobId, cancellationToken);
        if (job.State != TrainingJobState.Completed) throw new VoiceLiteException("请先完成训练，再导出角色包。");
        job = await ReadResultAsync(job, cancellationToken);
        if (!job.References.Any(x => x.Id == referenceId)) throw new VoiceLiteException("请选择该版本的一条3–10秒参考录音。");
        output = Path.GetFullPath(output); var parent = Path.GetDirectoryName(output)!; DatasetPaths.Plain(parent);
        if (File.Exists(output) || Directory.Exists(output) || Path.GetExtension(output).ToLowerInvariant() != ".zip" || DatasetPaths.IsInside(root, output) || DatasetPaths.IsInside(job.EngineRoot, output)) throw new VoiceLiteException("请选择新的ZIP文件名，并保存在训练库与原引擎目录之外。");
        var staging = Path.Combine(Folder(jobId), "export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging); DatasetPaths.Plain(staging);
        var temporary = Path.Combine(parent, "." + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            progress.Report("正在核验并打包角色权重和参考录音…"); var archive = Path.Combine(staging, "role.zip");
            await runCommand(job.EngineRoot, ["export-role", "--work", Path.Combine(Folder(jobId), "work"), "--name", job.Name, "--reference", referenceId, "--output", archive], progress, cancellationToken);
            DatasetPaths.Plain(archive);
            if (new FileInfo(archive).Length is < 1 or > 1024L * 1024 * 1024) throw new VoiceLiteException("导出的角色包大小无效。");
            byte[] expected;
            await using (var input = File.OpenRead(archive)) expected = await SHA256.HashDataAsync(input, cancellationToken);
            await using (var input = File.OpenRead(archive))
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            { await input.CopyToAsync(destination, cancellationToken); await destination.FlushAsync(cancellationToken); }
            await using (var copied = File.OpenRead(temporary))
                if (!(await SHA256.HashDataAsync(copied, cancellationToken)).SequenceEqual(expected)) throw new VoiceLiteException("角色包复制校验失败。");
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, output); return output;
        }
        catch (Exception exception) when (exception is not (VoiceLiteException or OperationCanceledException)) { throw new VoiceLiteException("角色包未能保存，请检查剩余空间与文件权限。", exception); }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            foreach (var file in Directory.GetFiles(staging)) { DatasetPaths.Plain(file); File.Delete(file); }
            Directory.Delete(staging);
        }
    }
    private static async Task RunCommandAsync(string engineRoot, string[] arguments, IProgress<string> progress, CancellationToken token)
    {
        var python = Path.Combine(engineRoot, "runtime/python.exe"); var api = Path.Combine(engineRoot, "api_v2.py");
        if (!File.Exists(python) || !File.Exists(api)) throw new VoiceLiteException("训练资源不完整，请选择兼容的完整GPT-SoVITS整合包；程序不会下载模型。");
        DatasetPaths.Plain(python); DatasetPaths.Plain(api);
        await using (var stream = File.OpenRead(api)) if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(EngineLayout.ApiSha256, StringComparison.OrdinalIgnoreCase)) throw new VoiceLiteException("训练资源版本不兼容，请使用已锁定的完整整合包。");
        var info = new ProcessStartInfo(python) { WorkingDirectory = AppContext.BaseDirectory };
        foreach (var argument in new[] { "-B", "-u", Path.Combine(AppContext.BaseDirectory, "training_cli.py") }.Concat(arguments)) info.ArgumentList.Add(argument);
        var timeout = arguments[0] switch { "sovits" => TimeSpan.FromHours(2), "init" => TimeSpan.FromMinutes(3), _ => TimeSpan.FromMinutes(15) };
        var work = arguments[Array.IndexOf(arguments, "--work") + 1];
        var logDirectory = Path.Combine(Path.GetDirectoryName(work)!, "client-logs"); Directory.CreateDirectory(logDirectory); DatasetPaths.Plain(logDirectory);
        var log = Path.Combine(logDirectory, arguments[0] + "-" + Guid.NewGuid().ToString("N") + ".log");
        ProcessRunner.Result result;
        try
        {
            result = await ProcessRunner.RunAsync(info, timeout, token, line => { if (line.StartsWith("Running GPT_SoVITS/prepare_datasets/", StringComparison.Ordinal)) progress.Report("正在提取训练特征，请等待…"); });
            await File.WriteAllTextAsync(log, result.Output + Environment.NewLine + result.Error);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(log, "Process stopped: " + exception.GetType().Name);
            if (exception is OperationCanceledException) throw;
            throw new VoiceLiteException("训练进程未能完成，请检查资源、显存或超时；日志已保留。", exception);
        }
        if (result.ExitCode != 0) throw new VoiceLiteException("训练步骤未完成，请检查完整资源、NVIDIA显存和任务日志。已保留输入与检查点。");
    }
}
