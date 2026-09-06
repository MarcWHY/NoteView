param([switch]$Test, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $projectRoot 'dist' } elseif ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory)) }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /unsafe+ /codepage:65001 ('/out:' + (Join-Path $output 'NoteView.exe')) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'NoteView.exe.config') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $output 'README.md') -Force
foreach ($notice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $output -Force }
if (Test-Path -LiteralPath (Join-Path $projectRoot 'docs')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $output -Recurse -Force }
if ($Test) {
    $testSources = @((Join-Path $projectRoot 'tests\MusicTheoryTests.cs'))
    & $compiler /nologo /target:exe /platform:x64 /codepage:65001 ('/out:' + (Join-Path $output 'NoteView.Tests.exe')) /reference:System.Core.dll (Join-Path $projectRoot 'src\MusicTheory.cs') @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $output 'NoteView.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Output ('Built: ' + (Join-Path $output 'NoteView.exe'))
