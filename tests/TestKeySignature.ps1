$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$testOutput = Join-Path $projectRoot 'artifacts\key-signature-tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$sources = @((Join-Path $projectRoot 'src\KeySignature.cs'), (Join-Path $PSScriptRoot 'KeySignatureTests.cs'))
$testExe = Join-Path $testOutput 'KeySignatureTests.exe'
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:' + $testExe) @sources
if ($LASTEXITCODE -ne 0) { throw 'Key signature tests did not compile.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Key signature tests failed.' }
