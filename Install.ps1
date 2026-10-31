$ErrorActionPreference = 'Stop'
# MSIX-hosted shells can virtualize LocalAppData. Scheduled tasks must see the same executable.
$destination = Join-Path $env:USERPROFILE '.chatgpt-quota-widget'
$exe = Join-Path $destination 'QuotaFloat.exe'
$taskName = 'ChatGPTQuotaFloat-Watchdog'
# Stop recovery before replacing the executable.
$oldTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($oldTask) {
    Disable-ScheduledTask -TaskName $taskName | Out-Null
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $exe) {
    & $exe --stop
    Start-Sleep -Seconds 3
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$legacySettings = Join-Path $env:LOCALAPPDATA 'CodexQuotaFloat\settings.json'
$settings = Join-Path $destination 'settings.json'
if (!(Test-Path -LiteralPath $settings) -and (Test-Path -LiteralPath $legacySettings)) {
    Copy-Item -LiteralPath $legacySettings -Destination $settings
}
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($destination)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuotaFloat.exe') -Destination $exe -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.cmd') -Destination $destination -Force
}
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null
New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ChatGPTQuotaFloat' -Value ('"' + $exe + '" --watch') -PropertyType String -Force | Out-Null
try {
    $userId = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $exe -Argument '--watch' -WorkingDirectory $destination
    $logon = New-ScheduledTaskTrigger -AtLogOn -User $userId
    $recovery = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($logon, $recovery) -Principal $principal -Settings $settings -Description 'Show quota with ChatGPT; recover stopped watcher every minute.' -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Write-Host 'Automatic recovery enabled: stopped watchers restart within one minute.'
} catch {
    Write-Warning ('Automatic recovery task could not be registered: ' + $_.Exception.Message)
    Start-Process -FilePath $exe -ArgumentList '--watch' -WindowStyle Hidden
}
Write-Host 'Installed. The widget follows ChatGPT automatically (within 2 seconds).'
Write-Host ('Uninstall: ' + (Join-Path $destination 'Uninstall.cmd'))
