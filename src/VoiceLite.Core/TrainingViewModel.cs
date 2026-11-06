using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceLite.Core;

public sealed class TrainingViewModel(ITrainingService service, string datasetId) : ObservableObject
{
    private string name = "新声音版本", engineRoot = "", status = "选择完整训练资源，给新版本命名后开始。", error = "", lastExportPath = "";
    private bool isBusy, closing;
    private TrainingJob? selectedJob;
    private TrainingReference? selectedReference;
    private CancellationTokenSource? cancellation;
    public ObservableCollection<TrainingJob> Jobs { get; } = [];
    public Task Operation { get; private set; } = Task.CompletedTask;
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string EngineRoot { get => engineRoot; set => SetProperty(ref engineRoot, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string LastExportPath { get => lastExportPath; private set => SetProperty(ref lastExportPath, value); }
    public bool IsBusy { get => isBusy; private set { if (SetProperty(ref isBusy, value)) Notify(); } }
    public bool CanEdit => !IsBusy && !closing;
    public bool CanStart => CanEdit;
    public bool CanExport => CanEdit && SelectedJob?.State == TrainingJobState.Completed && SelectedReference is not null;
    public TrainingJob? SelectedJob
    {
        get => selectedJob;
        set { if (SetProperty(ref selectedJob, value)) { SelectedReference = value?.References.FirstOrDefault(); Error = value?.Error ?? ""; Notify(); } }
    }
    public TrainingReference? SelectedReference { get => selectedReference; set { if (SetProperty(ref selectedReference, value)) Notify(); } }
    private void Notify() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanExport)); }
    public Task InitializeAsync() => RunAsync(async token => { await ReloadAsync(null, token); Status = "任务记录已载入，可新建版本或导出完成的角色。"; });
    private async Task ReloadAsync(string? selectedId, CancellationToken token)
    {
        var jobs = await service.ListAsync(datasetId, token); Jobs.Clear(); foreach (var job in jobs) Jobs.Add(job);
        SelectedJob = Jobs.FirstOrDefault(x => x.Id == selectedId) ?? Jobs.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(EngineRoot) && SelectedJob is not null) EngineRoot = SelectedJob.EngineRoot;
    }
    public Task StartAsync() => RunAsync(async token => {
        TrainingJob? result = null;
        try { result = await service.StartAsync(datasetId, Name, EngineRoot, Progress(), token); }
        finally { await ReloadAsync(result?.Id, CancellationToken.None); }
        Status = result.References.Count == 0 ? "训练完成，但没有3–10秒参考可导出；请准备合适参考后新建任务。" : "新版本已完成8轮。选择参考录音，保存角色包后可回主窗口导入试听。";
    });
    public Task ExportAsync(string output) => RunAsync(async token => {
        if (SelectedJob?.State != TrainingJobState.Completed || SelectedReference is null) throw new VoiceLiteException("请先选择已完成任务与参考录音。");
        LastExportPath = await service.ExportRoleAsync(SelectedJob.Id, SelectedReference.Id, output, Progress(), token);
        Status = "角色包已保存。回主窗口点击“导入角色包”即可使用；请试听确认效果。";
    });
    private IProgress<string> Progress() => new Progress<string>(message => { if (IsBusy) Status = message; });
    private Task RunAsync(Func<CancellationToken,Task> action)
    {
        if (IsBusy || closing) return Task.CompletedTask;
        return Operation = RunCoreAsync(action);
    }
    private async Task RunCoreAsync(Func<CancellationToken,Task> action)
    {
        IsBusy = true; Error = ""; Status = "正在处理，请等待…";
        using var source = new CancellationTokenSource(); cancellation = source;
        try { await action(source.Token); }
        catch (OperationCanceledException) { Status = "已取消。素材、已保存模型与任务记录保留。"; }
        catch (VoiceLiteException exception) { Error = exception.Message; Status = "任务未完成，请按提示检查后重试。"; }
        catch (Exception) { Error = "任务未完成，请检查资源、磁盘空间和文件权限。"; Status = "已保留原有任务和素材。"; }
        finally { cancellation = null; IsBusy = false; }
    }
    public void Cancel() => cancellation?.Cancel();
    public void ReportError(string message) => Error = message;
    public async Task<bool> CloseAsync()
    {
        if (closing) return false;
        closing = true; Notify(); Cancel(); await Operation; return true;
    }
}
