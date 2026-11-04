namespace VoiceLite.Infrastructure;

public static class EngineLayout
{
    public const string Version = "v2ProPlus";
    public const string ApiSha256 = "44E067698A61014CDFF3BDA1B2FBFF97252440E5879C429D38119FB0010D2B42";
    public static readonly string[] RequiredFiles = [
        "api_v2.py", "tools/portable_runtime.py", "GPT_SoVITS/TTS_infer_pack/TTS.py",
        "runtime/python.exe", "runtime/ffmpeg.exe", "runtime/ffprobe.exe",
        "GPT_SoVITS/pretrained_models/s1v3.ckpt", "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth",
        "GPT_SoVITS/pretrained_models/sv/pretrained_eres2netv2w24s4ep4.ckpt",
        "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large/config.json",
        "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large/tokenizer.json",
        "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large/pytorch_model.bin",
        "GPT_SoVITS/pretrained_models/chinese-hubert-base/config.json",
        "GPT_SoVITS/pretrained_models/chinese-hubert-base/preprocessor_config.json",
        "GPT_SoVITS/pretrained_models/chinese-hubert-base/pytorch_model.bin",
        "GPT_SoVITS/pretrained_models/fast_langdetect/lid.176.bin",
        "GPT_SoVITS/text/G2PWModel/g2pW.onnx", "GPT_SoVITS/text/G2PWModel/config.py",
        "GPT_SoVITS/text/G2PWModel/POLYPHONIC_CHARS.txt", "GPT_SoVITS/text/G2PWModel/MONOPHONIC_CHARS.txt",
        "GPT_SoVITS/text/G2PWModel/char_bopomofo_dict.json", "GPT_SoVITS/text/G2PWModel/bopomofo_to_pinyin_wo_tune_dict.json"
    ];
    internal static Dictionary<string, object> Config(VoiceLite.Core.EngineOptions options) => new() {
        ["custom"] = new Dictionary<string, object> {
            ["version"] = Version, ["device"] = options.UseGpu ? "cuda" : "cpu", ["is_half"] = options.UseGpu,
            ["t2s_weights_path"] = options.Weights?.GptPath ?? Path.Combine(options.EngineRoot, "GPT_SoVITS/pretrained_models/s1v3.ckpt"),
            ["vits_weights_path"] = options.Weights?.SovitsPath ?? Path.Combine(options.EngineRoot, "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"),
            ["bert_base_path"] = Path.Combine(options.EngineRoot, "GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large"),
            ["cnhuhbert_base_path"] = Path.Combine(options.EngineRoot, "GPT_SoVITS/pretrained_models/chinese-hubert-base")
        }
    };
}
