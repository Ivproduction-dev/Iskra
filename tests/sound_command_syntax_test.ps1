$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimePath = Join-Path $projectRoot 'IskraStudio\ProjectRuntime.cs'
$runtimeCode = Get-Content -LiteralPath $runtimePath -Raw
$patternMatch = [regex]::Match($runtimeCode, 'private static readonly Regex AudioPattern = new\(\s*"(?<pattern>[^"]+)"')
if (-not $patternMatch.Success) {
    throw 'Could not find the production sound-command pattern.'
}

$pattern = [regex]::new($patternMatch.Groups['pattern'].Value.Replace('\\', '\'), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$ordinary = $pattern.Match('Воспроизвести звук. тема.mp3')
$waiting = $pattern.Match('Воспроизвести звук ждать. игрок.тема.mp3')
if (-not $ordinary.Success -or $ordinary.Groups['wait'].Success -or $ordinary.Groups['arg'].Value -ne 'тема.mp3') {
    throw 'Ordinary playback must accept a local sound filename without waiting.'
}
if (-not $waiting.Success -or -not $waiting.Groups['wait'].Success -or $waiting.Groups['arg'].Value -ne 'игрок.тема.mp3') {
    throw 'Waiting playback must accept a cross-object sound reference.'
}

if ($runtimeCode -notmatch 'case "play-sound"\s*:\s*case "play-sound-wait"' -or
    $runtimeCode -notmatch 'await ended\.Task\.WaitAsync\(cancellationToken\)') {
    throw 'Both sound commands must run, and the waiting command must pause until playback completes.'
}

Write-Output 'Passed: ordinary and waiting sound commands parse and use their distinct playback behavior.'

$objectDetailCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.ObjectDetail.cs') -Raw
if ($objectDetailCode -notmatch 'e\.ClickCount != 2' -or
    $objectDetailCode -notmatch 'Clipboard\.SetText\(Path\.GetFileName\(path\)\)') {
    throw 'Double-clicking a sound name must copy only its filename.'
}

Write-Output 'Passed: double-clicking a sound copies only its filename.'
