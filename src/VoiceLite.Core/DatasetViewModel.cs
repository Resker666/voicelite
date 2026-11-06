using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceLite.Core;

public sealed class DatasetViewModel(IDatasetService store, IOfflineTranscriber transcriber) : ObservableObject
{
    private TrainingDataset? selectedDataset;
    private TrainingClipRow? selectedClip;
    private CancellationTokenSource? cancellation;
    private bool isBusy, isDirty, closing;
    private string engineRoot = "", asrEngineRoot = "", newDatasetName = "新声音素材", status = "新建或选择素材集，导入2–18秒短句录音。", error = "", lastExportPath = "";
    public ObservableCollection<TrainingDataset> Datasets { get; } = [];
    public ObservableCollection<TrainingClipRow> Clips { get; } = [];
    public IReadOnlyList<LanguageChoice> Languages { get; } = [new("中文", "zh"), new("英文", "en")];
    public Task Operation { get; private set; } = Task.CompletedTask;
    public string EngineRoot { get => engineRoot; set => SetProperty(ref engineRoot, value); }
    public string AsrEngineRoot { get => asrEngineRoot; set => SetProperty(ref asrEngineRoot, value); }
    public string NewDatasetName { get => newDatasetName; set => SetProperty(ref newDatasetName, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string LastExportPath { get => lastExportPath; private set => SetProperty(ref lastExportPath, value); }
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanEditClip)); OnPropertyChanged(nameof(CanOperateDataset)); } } }
    public bool CanEdit => !IsBusy && !closing;
    public bool CanOperateDataset => CanEdit && SelectedDataset is not null;
    public bool CanEditClip => CanEdit && SelectedClip is not null;
    public bool IsDirty { get => isDirty; private set { if (SetProperty(ref isDirty, value)) OnPropertyChanged(nameof(Summary)); } }
    public string Summary => $"{Clips.Count}条 · 启用{Clips.Count(x => x.Included)}条 · 已校对{Clips.Count(x => x.Included && x.Reviewed)}条 · {(IsDirty ? "有未保存修改" : "已保存")}";
    public TrainingDataset? SelectedDataset { get => selectedDataset; private set { if (SetProperty(ref selectedDataset, value)) OnPropertyChanged(nameof(CanOperateDataset)); } }
    public TrainingClipRow? SelectedClip
    {
        get => selectedClip;
        set { if (SetProperty(ref selectedClip, value)) OnPropertyChanged(nameof(CanEditClip)); }
    }

    public Task InitializeAsync(string? selectedId = null, CancellationToken cancellationToken = default) => RunAsync(async token => {
        var datasets = await store.ListAsync(token);
        Datasets.Clear(); foreach (var item in datasets) Datasets.Add(item);
        var chosen = datasets.FirstOrDefault(x => x.Id == selectedId) ?? datasets.FirstOrDefault();
        if (chosen is not null) Apply(await store.LoadAsync(chosen.Id, token));
        Status = chosen is null ? "先新建一个素材集，再批量导入同一声音的短句。" : "素材集已打开，自动转写的文字仍需逐字校对。";
    }, "正在打开素材库…", cancellationToken);

    public Task CreateAsync() => RunAsync(async token => {
        await SaveCoreAsync(token);
        Apply(await store.CreateAsync(NewDatasetName, token));
        Status = "素材集已创建，可以导入录音。";
    }, "正在新建素材集…");

    public Task OpenAsync(string id) => RunAsync(async token => {
        await SaveCoreAsync(token);
        Apply(await store.LoadAsync(id, token));
        Status = "素材集已打开。";
    }, "正在保存并打开素材集…");

    public Task ReloadAsync() => RunAsync(async token => { Apply(await store.LoadAsync(RequireDataset().Id, token)); Status = "已重新载入保存的素材。"; }, "正在重新载入…");

    public Task ImportAsync(IReadOnlyList<string> sources) => RunAsync(async token => {
        await SaveCoreAsync(token);
        var before = RequireDataset();
        var result = await store.ImportAsync(before.Id, sources, Path.Combine(EngineRoot, "runtime/ffmpeg.exe"), Progress(), token);
        Apply(result.Dataset);
        Error = string.Join(Environment.NewLine, result.Issues.Take(10).Select(x => $"{x.FileName}：{x.Message}"));
        Status = $"已导入{result.Dataset.Clips.Count - before.Clips.Count}条，跳过{result.Issues.Count}条。原录音保持不变。";
    }, "正在检查并导入录音…");

    public Task SaveAsync() => RunAsync(async token => { await SaveCoreAsync(token); Status = "素材与校对记录已保存。"; }, "正在保存校对…");

    public Task TranscribeAsync() => RunAsync(async token => {
        await SaveCoreAsync(token); var dataset = RequireDataset();
        var pending = Clips.Where(x => x.Included && !x.Reviewed && string.IsNullOrWhiteSpace(x.Text)).ToArray();
        if (pending.Length == 0) { Status = "没有待填写的启用项，已有文字未覆盖。"; return; }
        var inputs = new List<TranscriptionInput>();
        foreach (var row in pending) inputs.Add(new(row.Id, await store.GetAudioPathAsync(dataset.Id, row.Id, token), row.WaveSha256));
        var result = await transcriber.TranscribeAsync(AsrEngineRoot, inputs, Progress(), token);
        token.ThrowIfCancellationRequested();
        foreach (var draft in result.Drafts)
        {
            var row = Clips.FirstOrDefault(x => x.Id == draft.Id);
            if (row is null || !row.Included || row.Reviewed || !string.IsNullOrWhiteSpace(row.Text)) continue;
            row.Text = draft.Text; row.Language = draft.Language;
        }
        await SaveCoreAsync(token);
        Error = string.Join(Environment.NewLine, result.Issues.Take(10).Select(x => x.Message));
        Status = $"已生成{result.Drafts.Count}条待校对草稿，请试听并逐字校对。";
    }, "正在准备离线中文转写…");

    public Task ExportAsync(string parent) => RunAsync(async token => {
        await SaveCoreAsync(token);
        LastExportPath = await store.ExportAsync(RequireDataset().Id, parent, token);
        Status = $"训练集已导出：{LastExportPath}";
    }, "正在校验并导出已校对素材…");

    public async Task<string> SelectedAudioAsync(CancellationToken cancellationToken = default)
    {
        var row = SelectedClip ?? throw new VoiceLiteException("请先选择一条素材。");
        return await store.GetAudioPathAsync(RequireDataset().Id, row.Id, cancellationToken);
    }

    private TrainingDataset RequireDataset() => SelectedDataset ?? throw new VoiceLiteException("请先新建或选择一个素材集。");
    private IProgress<string> Progress() => new Progress<string>(message => { if (IsBusy) Status = message; });
    private void Edited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrainingClipRow.Text) or nameof(TrainingClipRow.Language) or nameof(TrainingClipRow.Reviewed) or nameof(TrainingClipRow.Included))
        { IsDirty = true; OnPropertyChanged(nameof(Summary)); }
    }
    private void Apply(TrainingDataset dataset)
    {
        var selectedId = SelectedClip?.Id;
        foreach (var row in Clips) row.PropertyChanged -= Edited;
        Clips.Clear();
        foreach (var clip in dataset.Clips) { var row = new TrainingClipRow(clip); row.PropertyChanged += Edited; Clips.Add(row); }
        var old = Datasets.FirstOrDefault(x => x.Id == dataset.Id);
        if (old is null) Datasets.Add(dataset); else Datasets[Datasets.IndexOf(old)] = dataset;
        SelectedDataset = dataset;
        SelectedClip = Clips.FirstOrDefault(x => x.Id == selectedId) ?? Clips.FirstOrDefault();
        IsDirty = false; OnPropertyChanged(nameof(Summary));
    }
    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsDirty || SelectedDataset is null) return;
        Apply(await store.SaveAsync(SelectedDataset with { Clips = Clips.Select(x => x.ToClip()).ToArray() }, cancellationToken));
    }
    private Task RunAsync(Func<CancellationToken, Task> action, string initialStatus, CancellationToken external = default)
    {
        if (IsBusy || closing) return Task.CompletedTask;
        return Operation = RunCoreAsync(action, initialStatus, external);
    }
    private async Task RunCoreAsync(Func<CancellationToken, Task> action, string initialStatus, CancellationToken external)
    {
        Error = ""; IsBusy = true; Status = initialStatus;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(external); cancellation = source;
        try { await action(source.Token); }
        catch (OperationCanceledException) { Status = "已取消，已保存的素材与校对记录保留。"; }
        catch (VoiceLiteException exception) { Error = exception.Message; Status = "操作未完成，请按提示调整后重试。"; }
        catch (Exception) { Error = "操作未完成，请检查文件权限与资源目录。"; Status = "素材数据已保留，可重新载入后重试。"; }
        finally { cancellation = null; IsBusy = false; OnPropertyChanged(nameof(SelectedDataset)); }
    }
    public void Cancel() => cancellation?.Cancel();
    public void ReportError(string message) => Error = message;
    public async Task<bool> CloseAsync()
    {
        if (closing) return false;
        closing = true; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanEditClip)); OnPropertyChanged(nameof(CanOperateDataset));
        Cancel(); await Operation;
        try { await SaveCoreAsync(CancellationToken.None); return true; }
        catch (VoiceLiteException exception) { Error = exception.Message; return false; }
        catch (Exception) { Error = "校对未能保存，请检查权限，或重新载入后关闭。"; return false; }
        finally { closing = false; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanEditClip)); OnPropertyChanged(nameof(CanOperateDataset)); }
    }
}
