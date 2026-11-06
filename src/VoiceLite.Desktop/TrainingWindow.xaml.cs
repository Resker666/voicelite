using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VoiceLite.Core;

namespace VoiceLite.Desktop;
public partial class TrainingWindow : Window
{
    private readonly TrainingViewModel vm;
    private readonly string dataRoot;
    private Task initialization = Task.CompletedTask;
    private Task? closing;
    private bool mayClose;
    public TrainingWindow(TrainingViewModel vm, string dataRoot)
    {
        this.vm = vm; this.dataRoot = dataRoot; InitializeComponent(); DataContext = vm;
        Loaded += async (_, _) => { initialization = vm.InitializeAsync(); await initialization; };
    }
    private void Engine_Click(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog { Title = "选择包含完整训练资源的GPT-SoVITS目录" }; if (dialog.ShowDialog(this) == true) vm.EngineRoot = dialog.FolderName; }
    private async void Start_Click(object sender, RoutedEventArgs e) => await vm.StartAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => vm.Cancel();
    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        if (vm.SelectedJob is null || !Guid.TryParseExact(vm.SelectedJob.Id, "N", out _)) return;
        var folder = Path.Combine(dataRoot, "training", vm.SelectedJob.Id);
        try { if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception) { vm.ReportError("无法打开任务目录，请检查文件权限。"); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var safeName = string.Concat((vm.SelectedJob?.Name ?? "新声音").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new SaveFileDialog { Title = "保存新角色包（请选择新的文件名）", FileName = safeName + ".zip", Filter = "VoiceLite角色包 (*.zip)|*.zip", DefaultExt = ".zip" };
        if (dialog.ShowDialog(this) == true) await vm.ExportAsync(dialog.FileName);
    }
    public Task CloseSafelyAsync() => closing ??= CloseCoreAsync();
    private async Task CloseCoreAsync()
    {
        IsEnabled = false; var pending = vm.CloseAsync(); await initialization; await pending;
        // The first Closing callback must return before WPF can accept Close again.
        await Dispatcher.InvokeAsync(() => { mayClose = true; Close(); });
    }
    protected override async void OnClosing(CancelEventArgs e)
    { base.OnClosing(e); if (mayClose) return; e.Cancel = true; await CloseSafelyAsync(); }
    public async Task<string> RunSmokeAsync(string output)
    {
        await initialization; var clock = Stopwatch.StartNew(); await vm.StartAsync();
        if (vm.SelectedJob?.State != TrainingJobState.Completed || !vm.CanExport) throw new VoiceLiteException("真实训练窗口验收失败：" + vm.Error);
        await vm.ExportAsync(output);
        if (vm.LastExportPath != output || vm.Error.Length > 0) throw new VoiceLiteException("真实角色导出失败：" + vm.Error);
        UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(dataRoot, "training-ui.png"))) encoder.Save(file);
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "training-smoke.json"), JsonSerializer.Serialize(new {
            passed = true, real_training = true, client_version = typeof(App).Assembly.GetName().Version?.ToString(3),
            epochs = vm.SelectedJob.EpochsCompleted, job_id = vm.SelectedJob.Id, dataset_id = vm.SelectedJob.DatasetId,
            reference_id = vm.SelectedReference?.Id, export_path = output, export_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
            quality_listener_approved = false, elapsed_seconds = clock.Elapsed.TotalSeconds
        }, new JsonSerializerOptions { WriteIndented = true }));
        return output;
    }
}
