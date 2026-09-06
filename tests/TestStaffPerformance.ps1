param([string]$Label = 'current', [string]$StaffSource = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\staff-performance-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @('MusicTheory.cs','KeySignature.cs','StaffView.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
if ($StaffSource) { $sources[2] = $StaffSource }
$testExe = Join-Path $testOutput 'StaffPerformanceTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /optimize+ /target:exe /platform:x64 /codepage:65001 /main:NoteView.StaffPerformanceTests ('/out:' + $testExe) @references @sources (Join-Path $PSScriptRoot 'StaffPerformanceTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Staff performance tests did not compile.' }
if (-not (Test-Path -LiteralPath (Join-Path $testOutput 'assets\Bravura.otf'))) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
}
$process = Start-Process -FilePath $testExe -ArgumentList $Label -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $testOutput ($Label + '.log')) -RedirectStandardError (Join-Path $testOutput ($Label + '-errors.log'))
Get-Content -LiteralPath (Join-Path $testOutput ($Label + '.log'))
if ($process.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $testOutput ($Label + '-errors.log')); throw 'Staff performance tests failed.' }
