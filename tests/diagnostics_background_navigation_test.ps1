$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.Runtime.cs') -Raw
$backgroundNavigation = [regex]::Match(
    $runtimeCode,
    'IskraObject\? item = diagnostic\.ObjectId == IskraObject\.BackgroundId\s*\? new IskraObject \{ Id = IskraObject\.BackgroundId, Name = "Фон" \}'
)
if (-not $backgroundNavigation.Success) {
    throw 'Diagnostics for the background must resolve to the special background object.'
}

if ($runtimeCode -notmatch 'ShowObjectDetail\(scene, item, ObjectDetailTab\.Scripts\)') {
    throw 'Selecting a diagnostic must open its owning object in the Scripts tab.'
}

if ($runtimeCode -notmatch 'ScrollScriptToLineSmoothly\(editor, diagnostic\.Line\)' -or
    $runtimeCode -notmatch 'editor\.ScrollToVerticalOffset\(startOffset \+ \(targetOffset - startOffset\) \* easedProgress\)') {
    throw 'Selecting a diagnostic must animate the editor scroll to the reported line.'
}

Write-Output 'Passed: selecting a background diagnostic opens its Scripts tab and smoothly scrolls to the reported line.'
