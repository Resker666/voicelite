using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
namespace VoiceLite.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly IEngineManager manager;
    private readonly SettingsStore store;
    private readonly string dataRoot;
    private readonly MediaPlayer player = new();
    private readonly PlaybackQueue playbackQueue = new();
    private Task? cleanup;
    private Task environmentCheck = Task.CompletedTask;
    private bool mayClose;
    private DatasetWindow? datasetWindow;
    private TrainingWindow? trainingWindow;
    private bool openingDataset, closeRequested;
    public MainWindow(MainViewModel vm, IEngineManager manager, SettingsStore store, string dataRoot)
    {
        this.vm = vm; this.manager = manager; this.store = store; this.dataRoot = dataRoot;
        InitializeComponent(); DataContext = vm;
        Title = $"VoiceLite {typeof(App).Assembly.GetName().Version?.ToString(3)} · 本地声音克隆";
        EngineSettings.IsExpanded = string.IsNullOrWhiteSpace(vm.EngineRoot) && !vm.IsMock;
        AuxiliarySettings.IsExpanded = vm.AuxiliaryReferences.Count > 0;
    }
    private void SelectEngine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 GPT-SoVITS 引擎目录" };
        if (dialog.ShowDialog(this) == true) vm.EngineRoot = dialog.FolderName;
    }
    private async void ImportRole_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.CanEdit || cleanup is not null) return;
        var dialog = new OpenFileDialog { Title = "导入声音角色包", Filter = "VoiceLite 角色包 (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        StopPlayback();
        await vm.ImportRoleAsync(dialog.FileName);
        AuxiliarySettings.IsExpanded = vm.AuxiliaryReferences.Count > 0;
    }
    private async void Dataset_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.CanEdit || cleanup is not null || datasetWindow is not null || openingDataset || closeRequested) return;
        openingDataset = true; IsEnabled = false;
        StopPlayback();
        try
        {
            await manager.StopAsync();
            if (closeRequested) return;
            datasetWindow = CreateDatasetWindow(); IsEnabled = true; datasetWindow.ShowDialog();
        }
        catch (Exception exception) { new AppLog(dataRoot).Write("open-materials", exception.GetType().Name); }
        finally { datasetWindow = null; openingDataset = false; if (!closeRequested) IsEnabled = true; }
    }
    private DatasetWindow CreateDatasetWindow() => new(new DatasetViewModel(new DatasetService(dataRoot), new OfflineTranscriber(dataRoot)) { EngineRoot = vm.EngineRoot }, dataRoot) { Owner = this };
    public async Task RunDatasetSmokeAsync(IReadOnlyList<string> sources, string asrEngine)
    {
        datasetWindow = CreateDatasetWindow(); datasetWindow.Show();
        try { await datasetWindow.RunSmokeAsync(sources, asrEngine); }
        finally { await datasetWindow.CloseSafelyAsync(); datasetWindow = null; }
    }
    public async Task<string> RunTrainingSmokeAsync(string datasetId, string engine, string output)
    {
        StopPlayback(); await manager.StopAsync();
        var model = new TrainingViewModel(new TrainingService(dataRoot), datasetId) { EngineRoot = engine, Name = "已校对素材新版本" };
        trainingWindow = new(model, dataRoot) { Owner = this }; trainingWindow.Show();
        try { return await trainingWindow.RunSmokeAsync(output); }
        finally { await trainingWindow.CloseSafelyAsync(); trainingWindow = null; }
    }
    public async Task RunWindowCloseSmokeAsync()
    {
        async Task CloseOnceAsync(Window child)
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            child.Closed += (_, _) => closed.TrySetResult(); child.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            child.Close();
            try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            finally { if (!closed.Task.IsCompleted) child.Close(); }
        }
        await CloseOnceAsync(new TrainingWindow(new TrainingViewModel(new TrainingService(dataRoot), Guid.NewGuid().ToString("N")), dataRoot) { Owner = this });
        await CloseOnceAsync(CreateDatasetWindow());
        var mainClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Closed += (_, _) => mainClosed.TrySetResult();
        Close(); await mainClosed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "window-close-smoke.json"), JsonSerializer.Serialize(new { passed = true, first_close_training = true, first_close_dataset = true, first_close_main = true, real_wpf = true, model_run = false }));
    }
    private void Role_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => StopPlayback();
    private void SelectReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 3–10 秒参考录音", Filter = ReferenceAudioFormats.FileDialogFilter };
        if (dialog.ShowDialog(this) == true) SetReference(dialog.FileName);
    }
    private void SetReference(string path) { StopPlayback(); vm.ReferencePath = path; vm.PromptText = ""; }
    private void AddAuxiliary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择同一声音的辅助录音（最多3条）", Filter = ReferenceAudioFormats.FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) vm.AddAuxiliaryReferences(dialog.FileNames);
    }
    private async void PlayAuxiliary_Click(object sender, RoutedEventArgs e)
    { if (AuxiliaryList.SelectedItem is AudioReferenceChoice item) await PlayWithMessageAsync(item.Path); }
    private void RemoveAuxiliary_Click(object sender, RoutedEventArgs e)
    { if (AuxiliaryList.SelectedItem is AudioReferenceChoice item) vm.AuxiliaryReferences.Remove(item); }
    private void Reference_DragOver(object sender, DragEventArgs e) { e.Effects = vm.CanEditReferences && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Reference_Drop(object sender, DragEventArgs e)
    {
        if (vm.CanEditReferences && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
        {
            if (ReferenceAudioFormats.IsSupported(files[0]) && File.Exists(files[0])) SetReference(files[0]);
            else vm.ReportError(Directory.Exists(files[0]) ? "请打开文件夹，选择其中一条 3–10 秒的参考录音。" : ReferenceAudioFormats.UnsupportedMessage);
        }
    }
    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.CanEdit || cleanup is not null) return;
        environmentCheck = vm.CheckEnvironmentAsync(async token =>
        {
            var options = await new EngineDeviceResolver().ResolveAsync(vm.EngineRoot, dataRoot, vm.Device, token);
            var report = await new EnvironmentProbe().InspectAsync(options, token);
            var device = options.UseGpu ? "NVIDIA CUDA" : "CPU（FP32）";
            if (!report.IsReady) throw new VoiceLiteException(string.Join("；", report.Items.Where(x => !x.Passed).Select(x => $"{x.Name}：{x.Message}")));
            return $"计算设备：{device}。必要资源已找到，真实合成将在生成时验证。";
        });
        await environmentCheck;
    }
    private async void PlayReference_Click(object sender, RoutedEventArgs e) => await PlayWithMessageAsync(vm.ReferencePath);
    private async void PlayResult_Click(object sender, RoutedEventArgs e) => await PlayWithMessageAsync(vm.ResultPath);
    private async Task PlayWithMessageAsync(string path)
    {
        try { await PlayAndWaitAsync(path); }
        catch (OperationCanceledException) { }
        catch (VoiceLiteException exception) { vm.ReportError(exception.Message); }
        catch (Exception) { vm.ReportError("无法播放这个文件，请确认录音有效，或尝试重新生成。"); }
    }
    private void StopPlayback() { playbackQueue.Cancel(); player.Stop(); player.Close(); }
    private Task<double> PlayAndWaitAsync(string path)
    {
        StopPlayback();
        return playbackQueue.EnqueueAsync(token => PlayPreparedAsync(path, token));
    }
    private async Task<double> PlayPreparedAsync(string path, CancellationToken cancellationToken)
    {
        using var prepared = await new AudioPreviewService().PrepareAsync(path,
            Path.Combine(vm.EngineRoot, "runtime/ffmpeg.exe"), Path.Combine(dataRoot, "previews"), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        double seconds = 0;
        EventHandler ended = (_, _) => completion.TrySetResult();
        EventHandler<ExceptionEventArgs> failed = (_, _) => { if (!opening.TrySetException(new VoiceLiteException("系统播放器无法打开音频。"))) completion.TrySetException(new VoiceLiteException("播放失败。")); };
        EventHandler opened = (_, _) => { seconds = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0; opening.TrySetResult(); player.Play(); };
        player.MediaEnded += ended; player.MediaFailed += failed; player.MediaOpened += opened;
        try { player.Open(new Uri(prepared.FilePath)); await PlaybackAwaiter.WaitAsync(opening.Task, completion.Task, TimeSpan.FromSeconds(15), cancellationToken); return seconds; }
        finally
        {
            player.MediaEnded -= ended; player.MediaFailed -= failed; player.MediaOpened -= opened;
            player.Close();
        }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "WAV 音频 (*.wav)|*.wav", DefaultExt = ".wav", AddExtension = true, OverwritePrompt = true, FileName = $"VoiceLite-{DateTime.Now:yyyyMMdd-HHmmss}.wav" };
        if (dialog.ShowDialog(this) != true) return;
        try { await WaveExporter.SaveAsync(vm.ResultPath, dialog.FileName, protectedReferencePath: vm.ReferencePath); }
        catch (VoiceLiteException exception) { vm.ReportError(exception.Message); }
        catch (Exception) { vm.ReportError("保存失败，请选择可写入的目录，并确认磁盘空间足够。"); }
    }
    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        var logs = Path.Combine(dataRoot, "logs"); Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo(logs) { UseShellExecute = true });
    }
    public async Task RunSmokeAsync(string? playbackCheck = null)
    {
        var clock = Stopwatch.StartNew();
        await vm.GenerateCommand.ExecuteAsync(null);
        if (!vm.HasResult || vm.Error.Length > 0) throw new VoiceLiteException(vm.Error.Length > 0 ? vm.Error : "No UI result");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (vm.AuxiliaryReferences.Count > 0) { AuxiliarySettings.IsExpanded = true; AuxiliarySettings.BringIntoView(); }
        if (!vm.SelectedRole.IsCustom) ContentScroll.ScrollToTop();
        UpdateLayout();
        var screenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        screenshot.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using (var stream = File.Create(Path.Combine(dataRoot, "ui.png"))) encoder.Save(stream);
        var exported = Path.Combine(dataRoot, "ui-export.wav");
        await WaveExporter.SaveAsync(vm.ResultPath, exported);
        var playbackSeconds = await PlayAndWaitAsync(playbackCheck ?? exported);
        var info = WaveInfo.Inspect(await File.ReadAllBytesAsync(exported));
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new {
            passed = true, mock = vm.IsMock, actual_device = vm.ActualDevice, requested_device = vm.Device.ToString(), client_version = typeof(App).Assembly.GetName().Version?.ToString(3), status = vm.Status, seconds = info.DurationSeconds,
            sample_rate = info.SampleRate, auxiliary_reference_count = vm.AuxiliaryReferences.Count, selected_role_id = vm.SelectedRole.Id, selected_role_name = vm.SelectedRole.Name,
            role_weights = vm.SelectedRole.Weights, playback_completed = true, playback_checked_seconds = playbackSeconds,
            playback_source_format = Path.GetExtension(playbackCheck ?? exported).ToLowerInvariant(), elapsed_seconds = clock.Elapsed.TotalSeconds
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public async Task CloseSafelyAsync()
    {
        closeRequested = true;
        if (trainingWindow is not null) await trainingWindow.CloseSafelyAsync();
        if (datasetWindow is not null && !await datasetWindow.CloseSafelyAsync()) { closeRequested = false; return; }
        IsEnabled = false;
        cleanup ??= CleanupAsync(); await cleanup;
        await Dispatcher.InvokeAsync(() => { if (!mayClose) { mayClose = true; Close(); } });
    }
    private async Task CleanupAsync()
    {
        StopPlayback(); vm.CancelCurrentOperation();
        try
        {
            try { await playbackQueue.CloseAsync(); } catch (OperationCanceledException) { } catch (Exception exception) { new AppLog(dataRoot).Write("preview-close", exception.GetType().Name); }
            await environmentCheck;
            await vm.RoleOperation;
            await manager.StopAsync();
            if (vm.GenerateCommand.ExecutionTask is { } pending) await pending;
            var custom = vm.CustomReferenceDefaults;
            await store.SaveAsync(new(vm.EngineRoot, custom.ReferencePath, custom.PromptText, custom.PromptLanguage, custom.TextLanguage, vm.Device) {
                AuxiliaryReferencePaths = custom.AuxiliaryReferencePaths, SelectedRoleId = vm.SelectedRole.Id
            });
        }
        catch (Exception exception) { new AppLog(dataRoot).Write("close", exception.GetType().Name); }
        finally { await manager.DisposeAsync(); player.Close(); }
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (mayClose) return;
        e.Cancel = true; await CloseSafelyAsync();
    }
}
