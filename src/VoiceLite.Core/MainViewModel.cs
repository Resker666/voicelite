using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace VoiceLite.Core;

public sealed class MainViewModel : ObservableObject
{
    private readonly IGenerationService service;
    private string engineRoot = "", referencePath = "", promptText = "", text = "", promptLanguage = "zh", textLanguage = "zh";
    private bool isMock, isBusy;
    private ComputeDevice device;
    private string actualDevice = "";
    private CancellationTokenSource? environmentCancellation;
    private string status = "准备好后，输入文字并点击生成。", error = "", resultPath = "", resultDescription = "生成后可在这里试听并保存 WAV。";
    public string EngineRoot { get => engineRoot; set => SetProperty(ref engineRoot, value); }
    public string ReferencePath { get => referencePath; set => SetProperty(ref referencePath, value); }
    public string PromptText { get => promptText; set => SetProperty(ref promptText, value); }
    public string Text { get => text; set => SetProperty(ref text, value); }
    public string PromptLanguage { get => promptLanguage; set => SetProperty(ref promptLanguage, value); }
    public string TextLanguage { get => textLanguage; set => SetProperty(ref textLanguage, value); }
    public bool IsMock { get => isMock; set { if (SetProperty(ref isMock, value)) OnPropertyChanged(nameof(ModeDescription)); } }
    public ComputeDevice Device { get => device; set { if (SetProperty(ref device, value)) { ActualDevice = ""; OnPropertyChanged(nameof(ModeDescription)); } } }
    public string ActualDevice { get => actualDevice; private set { if (SetProperty(ref actualDevice, value)) OnPropertyChanged(nameof(ModeDescription)); } }
    public string ModeDescription => IsMock ? "模拟演示：仅生成提示音，未运行真实声音模型。" : $"真实模型模式 · {(ActualDevice.Length > 0 ? ActualDevice : Devices.First(x => x.Code == Device).Name)} · 音频在本机处理";
    public IReadOnlyList<DeviceChoice> Devices { get; } = [new("自动", ComputeDevice.Auto), new("NVIDIA", ComputeDevice.Nvidia), new("CPU", ComputeDevice.Cpu)];
    public bool IsBusy { get => isBusy; private set {
        if (!SetProperty(ref isBusy, value)) return;
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(HasResult));
        GenerateCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    } }
    public bool CanEdit => !IsBusy;
    public bool HasResult => !IsBusy && ResultPath.Length > 0;
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string ResultPath { get => resultPath; private set { if (SetProperty(ref resultPath, value)) OnPropertyChanged(nameof(HasResult)); } }
    public string ResultDescription { get => resultDescription; private set => SetProperty(ref resultDescription, value); }
    public IReadOnlyList<LanguageChoice> Languages { get; } = [new("中文 / 中英混合", "zh"), new("英文", "en")];
    public IAsyncRelayCommand GenerateCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public MainViewModel(IGenerationService service)
    {
        this.service = service;
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => IsBusy);
    }
    public void CancelCurrentOperation()
    {
        Status = "正在取消并回收后台任务…";
        environmentCancellation?.Cancel(); GenerateCommand.Cancel();
    }
    public async Task CheckEnvironmentAsync(Func<CancellationToken, Task<string>> inspect)
    {
        if (IsBusy) return;
        Error = ""; IsBusy = true; Status = "正在检查计算设备与引擎资源…";
        using var cancellation = new CancellationTokenSource();
        environmentCancellation = cancellation;
        try { Status = await inspect(cancellation.Token); }
        catch (OperationCanceledException) { Status = "环境检查已取消，后台任务已收尾。"; }
        catch (VoiceLiteException exception) { Error = exception.Message; Status = "环境检查未通过。"; }
        catch (Exception exception) { Error = $"环境检查失败（{exception.GetType().Name}），请检查引擎与运行权限。"; Status = "环境检查未完成。"; }
        finally { environmentCancellation = null; IsBusy = false; }
    }
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        Error = ""; ResultPath = ""; ActualDevice = "";
        if (string.IsNullOrWhiteSpace(Text)) { Error = "请先输入要合成的文字。"; return; }
        if (!IsMock && (string.IsNullOrWhiteSpace(ReferencePath) || string.IsNullOrWhiteSpace(PromptText))) { Error = "请选择参考录音，并填写录音中实际说出的文字。"; return; }
        if (!IsMock && string.IsNullOrWhiteSpace(EngineRoot)) { Error = "没有找到离线引擎，请展开运行设置检查引擎目录。"; return; }
        IsBusy = true; Status = "正在准备…";
        try
        {
            var input = new GenerationInput(EngineRoot, ReferencePath, PromptText, PromptLanguage, Text.Trim(), TextLanguage, IsMock, Device);
            var progress = new Progress<string>(message => { if (IsBusy) Status = message; });
            var result = await service.GenerateAsync(input, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ResultPath = result.FilePath;
            ActualDevice = result.DeviceName;
            ResultDescription = $"{(result.IsMock ? "模拟提示音" : "合成语音")} · {result.Info.DurationSeconds:F2} 秒 · {result.Info.SampleRate / 1000.0:F0} kHz · WAV";
            Status = result.IsMock ? "模拟演示已完成：未运行真实模型，请勿当作声音克隆结果。" : "生成完成，可以试听或保存 WAV。";
        }
        catch (OperationCanceledException) { Status = "已取消，后台任务已收尾。"; }
        catch (VoiceLiteException exception) { Error = exception.Message; Status = "生成未完成，请按提示调整后重试。"; }
        catch (Exception exception) { Error = $"操作未完成（{exception.GetType().Name}），请检查运行环境和文件权限。"; Status = "可重试；技术状态请查看诊断日志。"; }
        finally { IsBusy = false; }
    }
    public void ReportError(string message) => Error = message;
}
public sealed record LanguageChoice(string Name, string Code);
public sealed record DeviceChoice(string Name, ComputeDevice Code);
