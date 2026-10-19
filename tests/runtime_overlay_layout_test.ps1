$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$xamlPath = Join-Path $projectRoot 'IskraStudio\MainWindow.xaml'
[xml]$xaml = Get-Content -LiteralPath $xamlPath -Raw
$namespaces = [System.Xml.XmlNamespaceManager]::new($xaml.NameTable)
$namespaces.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$namespaces.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')

foreach ($name in 'RuntimeLaunchOverlay', 'DiagnosticsOverlay', 'RuntimeToastOverlay') {
    $element = $xaml.SelectSingleNode("//w:*[@x:Name='$name']", $namespaces)
    if ($null -eq $element) {
        throw "Expected workspace overlay '$name' in MainWindow.xaml."
    }
    $rowSpanAttribute = $element.Attributes | Where-Object { $_.LocalName -eq 'Grid.RowSpan' -or $_.LocalName -eq 'RowSpan' } | Select-Object -First 1
    if ($null -ne $rowSpanAttribute) {
        $rowSpan = [int]$rowSpanAttribute.Value
    } else {
        $rowSpan = 1
    }
    if ($rowSpan -lt 2) {
        throw "Workspace overlay '$name' must cover the toolbar and workspace rows; found RowSpan=$rowSpan."
    }
}

$badge = $xaml.SelectSingleNode("//w:*[@x:Name='DiagnosticsBadgeButton']", $namespaces)
if ($null -eq $badge -or $badge.GetAttribute('Grid.Row', '') -ne '1') {
    throw 'The diagnostics badge must sit in the workspace row, clear of the toolbar.'
}

$runtimeCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.Runtime.cs') -Raw
if ($runtimeCode -notmatch 'Panel\.SetZIndex\(WorkspaceView, modalVisible \? 30 : 0\)') {
    throw 'Modal runtime and diagnostics overlays must cover the floating action buttons.'
}

Write-Output 'Passed: overlays span the workspace, the badge clears the toolbar, and modal overlays cover floating buttons.'

$detailCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.ObjectDetail.cs') -Raw
if ($detailCode -match 'var floatingAdd = new Button' -or $detailCode -notmatch 'CreateProjectButton\.Visibility = detailTab == ObjectDetailTab\.Scripts \? Visibility\.Collapsed : Visibility\.Visible') {
    throw 'Object asset addition must use the shared floating action group.'
}

$mainWindowCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.xaml.cs') -Raw
if ($mainWindowCode -notmatch 'if \(showingDetail\)\s*\{\s*AddToCurrentDetail\(\);\s*return;') {
    throw 'The shared plus button must route to the active object resource action.'
}

Write-Output 'Passed: object resource addition uses the same centered plus/run group and routes by active tab.'

foreach ($name in 'CreateProjectOverlay', 'EntityDialogOverlay', 'RenameConfirmOverlay', 'DeleteEntryOverlay') {
    $dialog = $xaml.SelectSingleNode("//w:*[@x:Name='$name']", $namespaces)
    if ($null -eq $dialog -or $null -eq $dialog.SelectSingleNode('.//w:ScrollViewer', $namespaces)) {
        throw "Dialog '$name' must scroll when its contents exceed the available window height."
    }
}

$sceneEditorCode = Get-Content -LiteralPath (Join-Path $projectRoot 'IskraStudio\MainWindow.SceneEditor.cs') -Raw
if ($sceneEditorCode -notmatch 'Style = \(Style\)FindResource\("ProjectCardButton"\)' -or
    $sceneEditorCode -notmatch 'AutomationProperties\.SetName\(openButton, automationName\)' -or
    $sceneEditorCode -notmatch 'TextTrimming = TextTrimming\.CharacterEllipsis' -or
    $sceneEditorCode -notmatch 'ToolTip = name') {
    throw 'Object rows must open through an accessible keyboard-focusable button and truncate long names with a tooltip.'
}

$projectRows = $xaml.SelectNodes("//w:*[@x:Name='ProjectItems']//w:TextBlock[@Text='{Binding Name}']", $namespaces)
if ($projectRows.Count -eq 0 -or $projectRows[0].GetAttribute('TextTrimming') -ne 'CharacterEllipsis') {
    throw 'Project names must truncate instead of overflowing their row.'
}

Write-Output 'Passed: dialogs scroll, object rows support keyboard activation, and long names truncate.'
