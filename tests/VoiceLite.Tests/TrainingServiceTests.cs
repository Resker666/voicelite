using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class TrainingServiceTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static ITrainingService Service(string root, IDatasetService datasets, Func<string, string[], IProgress<string>, CancellationToken, Task> runner)
    {
        var type = typeof(DatasetService).Assembly.GetType("VoiceLite.Infrastructure.TrainingService"); Assert.NotNull(type);
        return (ITrainingService)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [root, datasets, runner], null)!;
    }
    private static Task RunFake(string engine, string[] args, IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); string Arg(string key) => args[Array.IndexOf(args, key) + 1];
        var work = Arg("--work"); Directory.CreateDirectory(work);
        if (args[0] == "sovits")
        {
            var data = JsonSerializer.Deserialize<TrainingDataset>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(work)!, "input.json")), Json)!;
            var weight = Path.Combine(work, "weights/SoVITS/test_e8_s1.pth"); Directory.CreateDirectory(Path.GetDirectoryName(weight)!); File.WriteAllText(weight, "trained");
            var checkpointFolder = Path.Combine(work, "features/logs_s2_v2ProPlus"); Directory.CreateDirectory(checkpointFolder);
            foreach (var name in new[] { "G_233333333333.pth", "D_233333333333.pth" }) File.WriteAllText(Path.Combine(checkpointFolder, name), "checkpoint");
            File.WriteAllText(Path.Combine(work, "run.json"), "{\"epochs_completed\":8}");
            File.WriteAllText(Path.Combine(work, "result.json"), JsonSerializer.Serialize(new {
                schema="voicelite-training-result-1", epochs_completed=8, sovits_path="weights/SoVITS/test_e8_s1.pth",
                sovits_bytes=new FileInfo(weight).Length, sovits_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(weight))),
                references=data.Clips.Select(x => new { id=x.Id, name=x.SourceName, text=x.Text, language=x.Language, seconds=x.DurationSeconds }) }));
        }
        if (args[0] == "export-role") File.WriteAllText(Arg("--output"), "exported ZIP stand-in");
        return Task.CompletedTask;
    }
    [Fact] public async Task Start_freezes_reviewed_data_runs_stages_and_restores_completed_job()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset(); var calls = new List<string>();
        var service = Service(folder.Root, store, async (engine, args, progress, token) => { calls.Add(args[0]); await RunFake(engine, args, progress, token); });
        var job = await service.StartAsync(store.Data.Id, "新版本", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None);
        Assert.Equal(new[] { "init", "features", "sovits" }, calls); Assert.Equal(1, store.Exports); Assert.Equal(0, store.Saves);
        Assert.Equal("Completed", (string)job.State.ToString()); Assert.Equal(8, (int)job.EpochsCompleted);
        var restored = await service.ListAsync(store.Data.Id, CancellationToken.None);
        Assert.Equal(1, (int)restored.Count); Assert.Equal(2, ((TrainingJob)restored[0]).References.Count);
    }
    [Fact] public async Task Insufficient_reviewed_material_never_starts_a_worker()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset();
        store.Data = store.Data with { Clips = store.Data.Clips.Select(x => x with { Reviewed=false }).ToArray() };
        var service = Service(folder.Root, store, (_, _, _, _) => throw new Xunit.Sdk.XunitException("Unexpected worker"));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await service.StartAsync(store.Data.Id, "新版本", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None));
        Assert.Equal(0, store.Exports);
    }
    [Fact] public async Task Cancellation_retains_input_records_and_releases_the_single_task_lock()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset(); using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service(folder.Root, store, async (_, _, _, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        Task running = service.StartAsync(store.Data.Id, "取消测试", Path.Combine(folder.Root, "engine"), new Progress<string>(), cancel.Token);
        await started.Task;
        var competing = Service(folder.Root, store, RunFake);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await competing.StartAsync(store.Data.Id, "冲突", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var jobs = await competing.ListAsync(store.Data.Id, CancellationToken.None);
        Assert.Equal("Cancelled", (string)jobs[0].State.ToString());
        var next = await competing.StartAsync(store.Data.Id, "重试", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None);
        Assert.Equal("Completed", (string)next.State.ToString());
    }
    [Fact] public async Task Exit_zero_without_model_is_failed_and_bad_jobs_do_not_hide_others()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset();
        var service = Service(folder.Root, store, (_, _, _, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await service.StartAsync(store.Data.Id, "空结果", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None));
        var bad = Path.Combine(folder.Root, "training", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad, "job.json"), "null");
        var jobs = await service.ListAsync(store.Data.Id, CancellationToken.None);
        Assert.Equal(1, (int)jobs.Count); Assert.Equal("Failed", (string)jobs[0].State.ToString());
    }
    [Fact] public async Task Export_never_overwrites_an_existing_file_or_accepts_a_foreign_reference()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset(); var service = Service(folder.Root, store, RunFake);
        var job = await service.StartAsync(store.Data.Id, "新版本", Path.Combine(folder.Root, "engine"), new Progress<string>(), CancellationToken.None);
        var target = Path.Combine(folder.Root, "keep.zip"); File.WriteAllText(target, "keep");
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await service.ExportRoleAsync((string)job.Id, store.Data.Clips[0].Id, target, new Progress<string>(), CancellationToken.None));
        Assert.Equal("keep", File.ReadAllText(target));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await service.ExportRoleAsync((string)job.Id, Guid.NewGuid().ToString("N"), Path.Combine(folder.Root, "other.zip"), new Progress<string>(), CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(folder.Root, "other.zip")));
    }
    [Fact] public async Task Missing_reference_collection_is_quarantined()
    {
        using var folder = new EngineFixture(); var store = new MemoryDataset(); var service=Service(folder.Root,store,RunFake);
        var id=Guid.NewGuid().ToString("N"); var directory=Path.Combine(folder.Root,"training",id); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"job.json"),JsonSerializer.Serialize(new { id,datasetId=store.Data.Id,name="坏记录",engineRoot="engine",state=3,epochsCompleted=0,references=(object?)null }));
        var jobs=await service.ListAsync(store.Data.Id,CancellationToken.None); Assert.Equal(0,(int)jobs.Count);
    }
    [Fact] public async Task Oversized_run_record_cannot_be_loaded_as_a_completed_job()
    {
        using var folder=new EngineFixture();var store=new MemoryDataset();var service=Service(folder.Root,store,RunFake);
        var job=await service.StartAsync(store.Data.Id,"新版本",Path.Combine(folder.Root,"engine"),new Progress<string>(),CancellationToken.None);
        File.WriteAllText(Path.Combine(folder.Root,"training",(string)job.Id,"work/run.json"),JsonSerializer.Serialize(new { epochs_completed=8,padding=new string('x',2*1024*1024) }));
        var jobs=await service.ListAsync(store.Data.Id,CancellationToken.None);Assert.Equal("Failed",(string)jobs[0].State.ToString());
    }
    [Fact] public async Task History_selects_newest_creation_time_instead_of_random_id_order()
    {
        using var folder=new EngineFixture();var store=new MemoryDataset();var service=Service(folder.Root,store,RunFake);
        foreach(var item in new[] { (new string('0',32),"最新",2), (new string('f',32),"较早",1) })
        {
            var directory=Path.Combine(folder.Root,"training",item.Item1);Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"job.json"),JsonSerializer.Serialize(new { id=item.Item1,datasetId=store.Data.Id,name=item.Item2,engineRoot="engine",state=3,epochsCompleted=0,references=Array.Empty<object>(),createdUtc=new DateTimeOffset(2026,11,5,item.Item3,0,0,TimeSpan.Zero) }));
        }
        var jobs=await service.ListAsync(store.Data.Id,CancellationToken.None);Assert.Equal("最新",(string)jobs[0].Name);
    }
    private sealed class MemoryDataset : IDatasetService
    {
        public int Exports, Saves;
        public TrainingDataset Data = new(Guid.NewGuid().ToString("N"), "测试素材", 1, Enumerable.Range(0,2).Select(_ => { var id=Guid.NewGuid().ToString("N"); return new TrainingClip(id,"wavs/"+id+".wav","test.wav",new string('a',64),new string('b',64),3,"校对文字","zh",true,true); }).ToArray());
        public Task<TrainingDataset> LoadAsync(string id,CancellationToken ct=default)=>Task.FromResult(Data);
        public Task<IReadOnlyList<TrainingDataset>> ListAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<TrainingDataset>>([Data]);
        public Task<string> ExportAsync(string id,string parent,CancellationToken ct=default) { Exports++; Directory.CreateDirectory(parent); File.WriteAllText(Path.Combine(Directory.GetParent(parent)!.FullName,"input.json"),JsonSerializer.Serialize(Data,Json)); return Task.FromResult(parent); }
        public Task<TrainingDataset> SaveAsync(TrainingDataset data,CancellationToken ct=default) { Saves++; return Task.FromResult(data); }
        public Task<TrainingDataset> CreateAsync(string name,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<DatasetImportResult> ImportAsync(string id,IReadOnlyList<string> sources,string ffmpeg,IProgress<string> progress,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<string> GetAudioPathAsync(string id,string clip,CancellationToken ct=default)=>throw new NotSupportedException();
    }
}
