using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceLite.Core;

public sealed class TrainingClipRow(TrainingClip clip) : ObservableObject
{
    private string text = clip.Text, language = clip.Language;
    private bool included = clip.Included, reviewed = clip.Reviewed;
    public string Id => clip.Id;
    public string SourceName => clip.SourceName;
    public string Duration => $"{clip.DurationSeconds:F2}秒";
    public string WaveSha256 => clip.WaveSha256;
    public string Text
    {
        get => text;
        set { if (SetProperty(ref text, value ?? "")) { Reviewed = false; Changed(); } }
    }
    public string Language
    {
        get => language;
        set { if (SetProperty(ref language, value)) { Reviewed = false; Changed(); } }
    }
    public bool Included { get => included; set { if (SetProperty(ref included, value)) Changed(); } }
    public bool CanReview => !string.IsNullOrWhiteSpace(Text) && Text.Length <= 2000 && Text.IndexOfAny(['|', '\r', '\n', '\0']) < 0 && Language is "zh" or "en";
    public bool Reviewed
    {
        get => reviewed;
        set
        {
            if (value && !CanReview) { OnPropertyChanged(); return; }
            if (SetProperty(ref reviewed, value)) Changed();
        }
    }
    public string Status => !Included ? "已排除" : Reviewed ? "已校对" : string.IsNullOrWhiteSpace(Text) ? "待填写" : "待校对";
    public TrainingClip ToClip() => clip with { Text = Text, Language = Language, Included = Included, Reviewed = Reviewed };
    private void Changed() { OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(CanReview)); }
}
