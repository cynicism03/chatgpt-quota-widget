$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:USERPROFILE '.chatgpt-quota-widget'
$expected = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.chatgpt-quota-widget'))
if ([IO.Path]::GetFullPath($destination) -ne $expected) { throw 'Unexpected installation path.' }
$exe = Join-Path $destination 'QuotaFloat.exe'
$taskName = 'ChatGPTQuotaFloat-Watchdog'
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Disable-ScheduledTask -TaskName $taskName | Out-Null
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
if (Test-Path -LiteralPath $exe) { & $exe --stop; Start-Sleep -Seconds 3 }
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ChatGPTQuotaFloat' -ErrorAction SilentlyContinue
# Remove only known installed files. Keep user settings and unrelated files.
foreach ($name in @('QuotaFloat.exe', 'Uninstall.ps1', 'Uninstall.cmd')) {
    $file = Join-Path $destination $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
Write-Host 'Uninstalled. Downloaded files and widget position settings were preserved.'
