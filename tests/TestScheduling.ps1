$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\scheduling-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $testOutput 'SchedulingTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /unsafe+ /codepage:65001 /main:NoteView.SchedulingTests ('/out:' + $testExe) @references @sources (Join-Path $PSScriptRoot 'SchedulingTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Scheduling tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Scheduling tests failed.' }
