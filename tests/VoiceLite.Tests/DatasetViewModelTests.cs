using System.Collections;
using VoiceLite.Core;
using Xunit;

namespace VoiceLite.Tests;

public sealed class DatasetViewModelTests
{
    private static dynamic Model(IDatasetService store, IOfflineTranscriber asr)
    {
        var type = typeof(MainViewModel).Assembly.GetType("VoiceLite.Core.DatasetViewModel"); Assert.NotNull(type);
        return Activator.CreateInstance(type, store, asr)!;
    }
    private static object[] Rows(dynamic vm) => Enumerable.Cast<object>((IEnumerable)vm.Clips).ToArray();

    [Fact] public async Task Editing_text_or_language_clears_review_and_close_saves_edits()
    {
        var store = new Store(); dynamic vm = Model(store, new Asr());
        await vm.InitializeAsync(store.FirstId, CancellationToken.None);
        dynamic row = Rows(vm)[1]; Assert.True((bool)row.Reviewed);
        row.Text = "修改后的文字"; Assert.False((bool)row.Reviewed);
        row.Reviewed = true; row.Language = "en"; Assert.False((bool)row.Reviewed);
        Assert.True((bool)vm.IsDirty);
        bool closed = await vm.CloseAsync(); Assert.True(closed);
        Assert.Equal("修改后的文字", store.Documents[store.FirstId].Clips[1].Text);
        Assert.False(store.Documents[store.FirstId].Clips[1].Reviewed);
    }

    [Fact] public async Task Asr_only_fills_empty_enabled_rows_and_never_confirms_or_overwrites_manual_text()
    {
        var store = new Store(); var asr = new Asr(); dynamic vm = Model(store, asr);
        await vm.InitializeAsync(store.FirstId, CancellationToken.None);
        await vm.TranscribeAsync();
        Assert.Single(asr.Inputs!);
        var data = store.Documents[store.FirstId];
        Assert.Equal("机器草稿", data.Clips[0].Text); Assert.False(data.Clips[0].Reviewed);
        Assert.Equal("手工文字", data.Clips[1].Text); Assert.True(data.Clips[1].Reviewed);
        Assert.Equal("旧草稿", data.Clips[2].Text); Assert.Empty(data.Clips[3].Text);
    }

    [Fact] public async Task Closing_cancels_transcription_waits_and_leaves_existing_labels_unchanged()
    {
        var store = new Store(); var asr = new Asr { Wait = true }; dynamic vm = Model(store, asr);
        await vm.InitializeAsync(store.FirstId, CancellationToken.None);
        Task operation = vm.TranscribeAsync(); await asr.Started.Task;
        Assert.True((bool)vm.IsBusy); Assert.False((bool)vm.CanEdit);
        bool closed = await vm.CloseAsync(); await operation;
        Assert.True(closed); Assert.False((bool)vm.IsBusy); Assert.True(asr.Cancelled);
        Assert.Empty(store.Documents[store.FirstId].Clips[0].Text);
    }

    [Fact] public async Task Switching_dataset_saves_the_old_set_before_loading_new_rows()
    {
        var store = new Store(); dynamic vm = Model(store, new Asr());
        await vm.InitializeAsync(store.FirstId, CancellationToken.None);
        dynamic row = Rows(vm)[0]; row.Text = "需要保留的校对";
        await vm.OpenAsync(store.SecondId);
        Assert.Equal("需要保留的校对", store.Documents[store.FirstId].Clips[0].Text);
        Assert.Equal(store.SecondId, (string)vm.SelectedDataset.Id); Assert.Empty(Rows(vm));
    }

    private sealed class Store : IDatasetService
    {
        public string FirstId { get; } = Guid.NewGuid().ToString("N");
        public string SecondId { get; } = Guid.NewGuid().ToString("N");
        public Dictionary<string, TrainingDataset> Documents { get; } = [];
        public Store()
        {
            TrainingClip Clip(string text, bool included = true, bool reviewed = false)
            { var id = Guid.NewGuid().ToString("N"); return new(id, $"wavs/{id}.wav", "sample.wav", new string('0',64), new string('1',64), 3, text, "zh", included, reviewed); }
            Documents[FirstId] = new(FirstId, "声音一", 0, [Clip(""), Clip("手工文字", reviewed: true), Clip("旧草稿"), Clip("", included: false)]);
            Documents[SecondId] = new(SecondId, "声音二", 0, []);
        }
        public Task<IReadOnlyList<TrainingDataset>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingDataset>>(Documents.Values.ToArray());
        public Task<TrainingDataset> LoadAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Documents[id]);
        public Task<TrainingDataset> SaveAsync(TrainingDataset dataset, CancellationToken cancellationToken = default)
        { Documents[dataset.Id] = dataset with { Revision = dataset.Revision + 1 }; return Task.FromResult(Documents[dataset.Id]); }
        public Task<string> GetAudioPathAsync(string id, string clipId, CancellationToken cancellationToken = default) => Task.FromResult($"C:\\datasets\\{id}\\wavs\\{clipId}.wav");
        public Task<TrainingDataset> CreateAsync(string name, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DatasetImportResult> ImportAsync(string id, IReadOnlyList<string> sources, string ffmpeg, IProgress<string> progress, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> ExportAsync(string id, string destinationParent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class Asr : IOfflineTranscriber
    {
        public bool Wait, Cancelled;
        public IReadOnlyList<TranscriptionInput>? Inputs;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TranscriptionResult> TranscribeAsync(string engineRoot, IReadOnlyList<TranscriptionInput> inputs, IProgress<string> progress, CancellationToken cancellationToken = default)
        {
            Inputs = inputs; Started.TrySetResult();
            if (Wait) try { await Task.Delay(Timeout.Infinite, cancellationToken); } catch (OperationCanceledException) { Cancelled = true; throw; }
            return new(inputs.Select(x => new TranscriptionDraft(x.Id, "机器草稿")).ToArray(), []);
        }
    }
}
