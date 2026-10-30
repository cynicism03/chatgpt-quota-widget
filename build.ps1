param([string]$Output = (Join-Path $PSScriptRoot 'QuotaFloat.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ('/out:' + $Output) (Join-Path $PSScriptRoot 'QuotaFloat.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
