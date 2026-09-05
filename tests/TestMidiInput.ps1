$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
$testOutput = Join-Path $projectRoot 'artifacts\tests'
[void](New-Item -ItemType Directory -Path $testOutput -Force)
$testExe = Join-Path $testOutput 'MidiInputTests.exe'
$engineSource = Join-Path $projectRoot 'src\MidiInput.cs'
$testSource = Join-Path $PSScriptRoot 'MidiInputTests.cs'
& $compiler /nologo /langversion:5 /target:exe /platform:anycpu "/out:$testExe" $engineSource $testSource
if ($LASTEXITCODE -ne 0) { throw "MIDI test compilation failed with exit code $LASTEXITCODE." }
& $testExe
if ($LASTEXITCODE -ne 0) { throw "MIDI tests failed with exit code $LASTEXITCODE." }
