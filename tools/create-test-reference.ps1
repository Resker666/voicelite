$ErrorActionPreference = 'Stop'
$outputRoot = Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts\smoke'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Add-Type -AssemblyName System.Speech
$synthesizer = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voices = @($synthesizer.GetInstalledVoices() | Where-Object Enabled)
    if (!$voices.Count) { throw 'No installed Windows speech voice.' }
    $voice = $voices | Where-Object { $_.VoiceInfo.Culture.Name -eq 'zh-CN' } | Select-Object -First 1
    if (!$voice) { $voice = $voices | Select-Object -First 1 }
    $synthesizer.SelectVoice($voice.VoiceInfo.Name)
    $text = if ($voice.VoiceInfo.Culture.Name -like 'zh*') { '这是一段用于本地软件测试的参考语音，希望大家使用愉快。' } else { 'This is a reference recording for a local software test. We want everyone to use this application easily and create their own audio.' }
    $synthesizer.Rate = -1
    $synthesizer.SetOutputToWaveFile((Join-Path $outputRoot 'reference.wav'))
    $synthesizer.Speak($text)
    [PSCustomObject]@{ voice=$voice.VoiceInfo.Name; language=$(if ($voice.VoiceInfo.Culture.Name -like 'zh*') {'zh'} else {'en'}); text=$text } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'reference.json') -Encoding utf8
} finally { $synthesizer.Dispose() }
