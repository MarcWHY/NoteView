$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\obs-worker-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Xml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @('MusicTheory.cs','KeySignature.cs','StaffView.cs', 'LayoutBoard.cs','AppSettings.cs','HarmonyVisuals.cs','ObsBackgroundComposer.cs','FastPngEncoder.cs','ObsFrameRenderer.cs','ObsFrameWorker.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$sources += Join-Path $PSScriptRoot 'ObsFrameWorkerTests.cs'
$testExe = Join-Path $testOutput 'ObsFrameWorkerTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /unsafe+ /codepage:65001 /main:NoteView.ObsFrameWorkerTests ('/out:' + $testExe) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'OBS worker tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'OBS worker tests failed.' }
