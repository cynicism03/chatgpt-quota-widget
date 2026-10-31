$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path test-results -Force | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /main:QuotaFloat.LifecycleTests /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /out:test-results\LifecycleTests.exe QuotaFloat.cs ControlPanel.cs tests\LifecycleTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & .\test-results\LifecycleTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Lifecycle tests failed.' }
} finally { Pop-Location }
