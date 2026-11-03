namespace VoiceLite.Infrastructure;

public sealed class AppLog(string dataRoot)
{
    private readonly object sync = new();
    public string FilePath { get; } = Path.Combine(dataRoot, "logs", "voicelite.log");
    public void Write(string category, string message)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2 * 1024 * 1024) File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    // Upstream may print entire input sentences. Keep only known diagnostic categories.
    internal static string? EngineDiagnostic(string? line, bool useGpu = true)
    {
        if (line is null) return null;
        if (line.Contains("DefaultCPUAllocator", StringComparison.OrdinalIgnoreCase) || line.Contains("MemoryError", StringComparison.Ordinal)) return "系统内存不足，请关闭其他占用内存的程序并缩短文字。";
        if (line.Contains("out of memory", StringComparison.OrdinalIgnoreCase)) return useGpu ? "显存不足，请关闭其他占用显卡的程序并缩短文字。" : "系统内存不足，请关闭其他占用内存的程序并缩短文字。";
        if (line.Contains("ModuleNotFoundError", StringComparison.Ordinal)) return "引擎缺少 Python 依赖，请重新获取完整离线包。";
        if (line.Contains("FileNotFoundError", StringComparison.Ordinal)) return "引擎资源缺失，请检查完整离线包。";
        if (line.Contains("CUDA", StringComparison.Ordinal) && line.Contains("Error", StringComparison.Ordinal)) return "CUDA 初始化失败，请检查 NVIDIA 驱动。";
        if (line.Contains("Traceback", StringComparison.Ordinal)) return "引擎发生 Python 异常。";
        return null;
    }
}
