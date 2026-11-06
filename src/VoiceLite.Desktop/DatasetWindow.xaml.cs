using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VoiceLite.Core;
using VoiceLite.Infrastructure;

namespace VoiceLite.Desktop;

public partial class DatasetWindow : Window
{
    private readonly DatasetViewModel vm;
    private readonly string dataRoot;
    private readonly DatasetUiStateStore stateStore;
    private readonly MediaPlayer player = new();
    private readonly PlaybackQueue playbackQueue = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task initialization = Task.CompletedTask;
    private Task<bool>? closing;
    private bool mayClose;
    private TrainingWindow? trainingWindow;
    public DatasetWindow(DatasetViewModel vm, string dataRoot)
    {
        this.vm = vm; this.dataRoot = dataRoot; stateStore = new(dataRoot);
        InitializeComponent(); DataContext = vm;
        Loaded += async (_, _) => { initialization = InitializeAsync(); await initialization; };
    }
    private async Task InitializeAsync()
    {
        try
        {
            var saved = await stateStore.LoadAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            vm.AsrEngineRoot = string.IsNullOrWhiteSpace(saved.AsrEngineRoot) ? vm.EngineRoot : saved.AsrEngineRoot;
            await vm.InitializeAsync(saved.SelectedDatasetId, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { vm.ReportError("素材窗口未能读取设置，请检查数据目录权限。"); }
    }
    private async void Dataset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (vm.CanEdit && e.AddedItems.Count == 1 && e.AddedItems[0] is TrainingDataset item && item.Id != vm.SelectedDataset?.Id)
        { StopPlayback(); await vm.OpenAsync(item.Id); }
    }
    private void Clip_SelectionChanged(object sender, SelectionChangedEventArgs e) => StopPlayback();
    private async void Create_Click(object sender, RoutedEventArgs e) { StopPlayback(); await vm.CreateAsync(); }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入同一声音的2–18秒短句", Filter = ReferenceAudioFormats.FileDialogFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) { StopPlayback(); await vm.ImportAsync(dialog.FileNames); }
    }
    private void SelectAsr_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择含离线中文转写模型的完整整合包" };
        if (dialog.ShowDialog(this) == true) vm.AsrEngineRoot = dialog.FolderName;
    }
    private async void Transcribe_Click(object sender, RoutedEventArgs e) { StopPlayback(); await vm.TranscribeAsync(); }
    private async void Save_Click(object sender, RoutedEventArgs e) => await vm.SaveAsync();
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (vm.IsDirty && MessageBox.Show(this, "重新载入会丢弃尚未保存的校对修改。是否继续？", "重新载入", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        StopPlayback(); await vm.ReloadAsync();
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择训练集导出位置（会新建独立目录）" };
        if (dialog.ShowDialog(this) == true) await vm.ExportAsync(dialog.FolderName);
    }
    private async void Train_Click(object sender, RoutedEventArgs e)
    {
        if (!vm.CanOperateDataset || trainingWindow is not null || closing is not null) return;
        StopPlayback(); await vm.SaveAsync();
        if (vm.Error.Length > 0 || vm.SelectedDataset is null || closing is not null) return;
        var datasetName = vm.SelectedDataset.Name;
        var training = new TrainingViewModel(new TrainingService(dataRoot), vm.SelectedDataset.Id) { EngineRoot = vm.AsrEngineRoot, Name = datasetName[..Math.Min(datasetName.Length, 60)] + " 新版本" };
        trainingWindow = new(training, dataRoot) { Owner = this };
        try { trainingWindow.ShowDialog(); } finally { trainingWindow = null; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { StopPlayback(); vm.Cancel(); }
    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        try { await PlaySelectedAsync(); }
        catch (OperationCanceledException) { }
        catch (VoiceLiteException exception) { vm.ReportError(exception.Message); }
        catch (Exception) { vm.ReportError("无法播放这条素材，请检查音频完整性。"); }
    }
    private void StopPlayback() { playbackQueue.Cancel(); player.Stop(); player.Close(); }
    private Task<double> PlaySelectedAsync()
    {
        StopPlayback();
        return playbackQueue.EnqueueAsync(async token => {
            var path = await vm.SelectedAudioAsync(token);
            token.ThrowIfCancellationRequested();
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            double seconds = 0;
            EventHandler open = (_, _) => { seconds = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0; opened.TrySetResult(); player.Play(); };
            EventHandler finish = (_, _) => ended.TrySetResult();
            EventHandler<ExceptionEventArgs> fail = (_, _) => { if (!opened.TrySetException(new VoiceLiteException("播放器无法打开素材。"))) ended.TrySetException(new VoiceLiteException("素材播放失败。")); };
            player.MediaOpened += open; player.MediaEnded += finish; player.MediaFailed += fail;
            try { player.Open(new Uri(path)); await PlaybackAwaiter.WaitAsync(opened.Task, ended.Task, TimeSpan.FromSeconds(15), token); return seconds; }
            finally { player.MediaOpened -= open; player.MediaEnded -= finish; player.MediaFailed -= fail; player.Close(); }
        });
    }
    public async Task<bool> CloseSafelyAsync()
    {
        if (closing is not null) return await closing;
        closing = CloseCoreAsync(); var result = await closing;
        if (!result) closing = null;
        return result;
    }
    private async Task<bool> CloseCoreAsync()
    {
        IsEnabled = false; StopPlayback(); lifetime.Cancel();
        try
        {
            if (trainingWindow is not null) await trainingWindow.CloseSafelyAsync();
            await initialization;
            if (!await vm.CloseAsync()) { IsEnabled = true; return false; }
            await stateStore.SaveAsync(new(vm.SelectedDataset?.Id ?? "", vm.AsrEngineRoot));
            try { await playbackQueue.CloseAsync(); } catch (OperationCanceledException) { }
            player.Close();
            await Dispatcher.InvokeAsync(() => { mayClose = true; Close(); }); return true;
        }
        catch (Exception) { vm.ReportError("素材设置未能保存，请检查数据目录权限后重试关闭。"); IsEnabled = true; return false; }
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (mayClose) return;
        e.Cancel = true; await CloseSafelyAsync();
    }

    public async Task RunSmokeAsync(IReadOnlyList<string> sources, string asrEngine)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await initialization;
        var clock = Stopwatch.StartNew();
        var hashes = sources.ToDictionary(x => x, x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
        vm.NewDatasetName = "素材流程自动化验收"; vm.AsrEngineRoot = asrEngine;
        await vm.CreateAsync(); await vm.ImportAsync(sources);
        if (vm.Error.Length > 0 || vm.Clips.Count != sources.Count) throw new VoiceLiteException("素材导入验收失败：" + vm.Error);
        await vm.TranscribeAsync();
        if (vm.Error.Length > 0 || vm.Clips.Any(x => x.Text.Length == 0 || x.Reviewed)) throw new VoiceLiteException("真实草稿验收失败：" + vm.Error);
        var draftCount = vm.Clips.Count;
        vm.SelectedClip = vm.Clips[0]; var played = await PlaySelectedAsync();
        // Test-only UI state exercise, not a claim of human review of the user's corpus.
        vm.SelectedClip.Text = "自动化验收用校对文字"; vm.SelectedClip.Reviewed = true;
        await vm.SaveAsync(); await vm.ReloadAsync();
        if (vm.Error.Length > 0 || vm.Clips[0].Text != "自动化验收用校对文字" || !vm.Clips[0].Reviewed) throw new VoiceLiteException("校对保存/重开验收失败。");
        await vm.ExportAsync(Path.Combine(dataRoot, "dataset-exports"));
        if (vm.Error.Length > 0 || File.ReadAllLines(Path.Combine(vm.LastExportPath, "train.list")).Length != 1) throw new VoiceLiteException("训练集导出验收失败。");
        foreach (var path in sources) if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hashes[path]) throw new VoiceLiteException("原音频发生变化。");
        UpdateLayout();
        var screenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); screenshot.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot));
        using (var output = File.Create(Path.Combine(dataRoot, "dataset-ui.png"))) encoder.Save(output);
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "dataset-smoke.json"), JsonSerializer.Serialize(new {
            passed = true, real_asr = true, device = "CPU", client_version = typeof(App).Assembly.GetName().Version?.ToString(3), clips = vm.Clips.Count,
            dataset_id = vm.SelectedDataset!.Id, draft_count = draftCount, human_transcripts_verified = false,
            test_only_review_flag_roundtrip = true, exported_clips = 1, export_path = vm.LastExportPath,
            playback_completed = true, playback_seconds = played, original_audio_unchanged = true, seconds = clock.Elapsed.TotalSeconds
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
