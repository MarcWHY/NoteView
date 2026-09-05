$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $projectRoot 'artifacts\decay-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $testOutput 'DecayTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 /main:NoteView.DecayTests ('/out:' + $testExe) @references @sources (Join-Path $PSScriptRoot 'DecayTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI tests did not compile.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $testOutput -Recurse -Force
$testProcess = Start-Process -FilePath $testExe -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $testOutput 'layout-test.log') -RedirectStandardError (Join-Path $testOutput 'layout-test-errors.log')
Get-Content -LiteralPath (Join-Path $testOutput 'layout-test.log')
if ($testProcess.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $testOutput 'layout-test-errors.log'); throw 'UI tests failed.' }


