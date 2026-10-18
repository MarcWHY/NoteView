$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\atmosphere-visual-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Xml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @('MusicTheory.cs','KeySignature.cs','StaffView.cs','LayoutBoard.cs','AppSettings.cs','HarmonicContext.cs','HarmonyColor.cs','HarmonyVisuals.cs','ObsBackgroundComposer.cs','FastPngEncoder.cs','ObsFrameRenderer.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$sources += Join-Path $PSScriptRoot 'AtmosphereVisualTests.cs'
$testExe = Join-Path $testOutput 'AtmosphereVisualTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /unsafe+ /codepage:65001 /main:NoteView.AtmosphereVisualTests ('/out:' + $testExe) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Atmosphere visual tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'NoteView.exe.config') -Destination ($testExe + '.config') -Force
$testProcess = Start-Process -FilePath $testExe -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $testOutput 'result.log') -RedirectStandardError (Join-Path $testOutput 'errors.log')
Get-Content -LiteralPath (Join-Path $testOutput 'result.log')
if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $testOutput 'errors.log'); throw 'Atmosphere visual tests failed.' }
