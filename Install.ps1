$ErrorActionPreference = 'Stop'
$destination = Join-Path $env:LOCALAPPDATA 'ChatGPTQuotaFloat'
$exe = Join-Path $destination 'QuotaFloat.exe'
if (Test-Path -LiteralPath $exe) {
    & $exe --stop
    Start-Sleep -Seconds 3
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
if ([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($destination)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QuotaFloat.exe') -Destination $exe -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.cmd') -Destination $destination -Force
}
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null
New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ChatGPTQuotaFloat' -Value ('"' + $exe + '" --watch') -PropertyType String -Force | Out-Null
Start-Process -FilePath $exe -ArgumentList '--watch' -WindowStyle Hidden
Write-Host 'Installed. The widget follows ChatGPT automatically (within 2 seconds).'
Write-Host ('Uninstall: ' + (Join-Path $destination 'Uninstall.cmd'))
