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
    private bool mayClose;
    public MainWindow(MainViewModel vm, IEngineManager manager, SettingsStore store, string dataRoot)
    {
        this.vm = vm; this.manager = manager; this.store = store; this.dataRoot = dataRoot;
        InitializeComponent(); DataContext = vm;
        EngineSettings.IsExpanded = string.IsNullOrWhiteSpace(vm.EngineRoot) && !vm.IsMock;
    }
    private void SelectEngine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择 GPT-SoVITS 引擎目录" };
        if (dialog.ShowDialog(this) == true) vm.EngineRoot = dialog.FolderName;
    }
    private void SelectReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 3–10 秒参考录音", Filter = ReferenceAudioFormats.FileDialogFilter };
        if (dialog.ShowDialog(this) == true) SetReference(dialog.FileName);
    }
    private void SetReference(string path) { StopPlayback(); vm.ReferencePath = path; vm.PromptText = ""; }
    private void Reference_DragOver(object sender, DragEventArgs e) { e.Effects = vm.CanEdit && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Reference_Drop(object sender, DragEventArgs e)
    {
        if (vm.CanEdit && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
        {
            if (ReferenceAudioFormats.IsSupported(files[0]) && File.Exists(files[0])) SetReference(files[0]);
            else vm.ReportError(Directory.Exists(files[0]) ? "请打开文件夹，选择其中一条 3–10 秒的参考录音。" : ReferenceAudioFormats.UnsupportedMessage);
        }
    }
    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = await new EnvironmentProbe().InspectAsync(new(vm.EngineRoot, dataRoot));
            MessageBox.Show(this, report.IsReady ? "引擎入口和必要资源已找到；模型加载和真实合成将在生成时验证。" : string.Join("\n", report.Items.Where(x => !x.Passed).Select(x => $"{x.Name}：{x.Message}")), "环境检查");
        }
        catch (Exception exception) { vm.ReportError($"环境检查失败（{exception.GetType().Name}），请检查目录权限。"); }
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
            passed = true, mock = vm.IsMock, status = vm.Status, seconds = info.DurationSeconds,
            sample_rate = info.SampleRate, playback_completed = true, playback_checked_seconds = playbackSeconds,
            playback_source_format = Path.GetExtension(playbackCheck ?? exported).ToLowerInvariant(), elapsed_seconds = clock.Elapsed.TotalSeconds
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public async Task CloseSafelyAsync()
    {
        IsEnabled = false;
        cleanup ??= CleanupAsync(); await cleanup;
        if (!mayClose) { mayClose = true; Close(); }
    }
    private async Task CleanupAsync()
    {
        StopPlayback(); vm.GenerateCommand.Cancel();
        try
        {
            try { await playbackQueue.CloseAsync(); } catch (OperationCanceledException) { } catch (Exception exception) { new AppLog(dataRoot).Write("preview-close", exception.GetType().Name); }
            await manager.StopAsync();
            if (vm.GenerateCommand.ExecutionTask is { } pending) await pending;
            await store.SaveAsync(new(vm.EngineRoot, vm.ReferencePath, vm.PromptText, vm.PromptLanguage, vm.TextLanguage));
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
