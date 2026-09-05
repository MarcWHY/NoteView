$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\staff-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @((Join-Path $projectRoot 'src\MusicTheory.cs'), (Join-Path $projectRoot 'src\KeySignature.cs'), (Join-Path $projectRoot 'src\StaffView.cs'), (Join-Path $PSScriptRoot 'StaffLayoutTests.cs'))
$testExe = Join-Path $testOutput 'StaffLayoutTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 /main:NoteView.StaffLayoutTests ('/out:' + $testExe) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Staff layout tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Staff layout tests failed.' }
