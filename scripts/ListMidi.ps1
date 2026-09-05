$ErrorActionPreference = 'Stop'
$midiSource = Join-Path $PSScriptRoot '..\src\MidiInput.cs'
if (-not ('NoteView.MidiInput' -as [type])) {
    Add-Type -Path $midiSource
}
$devices = @([NoteView.MidiInput]::GetDevices())
if ($devices.Count -eq 0) {
    Write-Output 'No Windows MIDI input devices found. Check the piano USB connection.'
} else {
    $devices | Select-Object Id, Name
}
