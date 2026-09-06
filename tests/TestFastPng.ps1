param([switch]$Benchmark)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\fast-png-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$testExe = Join-Path $testOutput 'FastPngEncoderTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /unsafe /codepage:65001 ('/out:' + $testExe) @references (Join-Path $projectRoot 'src\FastPngEncoder.cs') (Join-Path $PSScriptRoot 'FastPngEncoderTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fast PNG tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'NoteView.exe.config') -Destination ($testExe + '.config') -Force
$arguments = if ($Benchmark) { '--benchmark' } else { '--test' }
$testProcess = Start-Process -FilePath $testExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $testOutput 'result.log') -RedirectStandardError (Join-Path $testOutput 'errors.log')
Get-Content -LiteralPath (Join-Path $testOutput 'result.log')
if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $testOutput 'errors.log'); throw 'Fast PNG tests failed.' }
