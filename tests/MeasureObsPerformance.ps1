param([Parameter(Mandatory=$true)][string]$AssemblyPath, [Parameter(Mandatory=$true)][string]$Label)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Label must be a simple directory name.' }
$sourceAssembly = (Resolve-Path -LiteralPath $AssemblyPath).Path
$outputDirectory = Join-Path $projectRoot ('artifacts\performance-' + $Label)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceAssembly -Destination (Join-Path $outputDirectory 'NoteView.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $outputDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'NoteView.exe.config') -Destination (Join-Path $outputDirectory 'ObsPerformanceBench.exe.config') -Force
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$references += '/reference:' + (Join-Path $outputDirectory 'NoteView.exe')
$executable = Join-Path $outputDirectory 'ObsPerformanceBench.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 ('/out:' + $executable) @references (Join-Path $PSScriptRoot 'ObsPerformanceBench.cs')
if ($LASTEXITCODE -ne 0) { throw 'Performance harness compilation failed.' }
& $executable | Tee-Object -FilePath (Join-Path $outputDirectory 'results.csv')
if ($LASTEXITCODE -ne 0) { throw 'Performance harness failed.' }
