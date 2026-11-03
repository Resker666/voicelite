namespace VoiceLite.Core;

public static class ReferenceAudioFormats
{
    public const string FileDialogFilter = "参考录音 (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg";
    public const string HelpText = "选择 3–10 秒的清晰录音，支持 WAV、MP3、OGG。";
    public const string UnsupportedMessage = "请选择 WAV、MP3 或 OGG 参考录音。";
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".ogg";
}
