# Builds StartScreenSetup.exe: rebuilds the app first, then embeds StartScreen.exe inside the installer.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& "$PSScriptRoot\..\build.ps1"
Set-Location $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$app = Resolve-Path "$PSScriptRoot\..\StartScreen.exe"
& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:"$PSScriptRoot\..\StartScreen.ico" /out:StartScreenSetup.exe `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll `
    /resource:"$app,StartScreen.exe" `
    Installer.cs
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
$f = Get-Item "$PSScriptRoot\StartScreenSetup.exe"
Write-Host ("Built {0} ({1:N1} MB)" -f $f.FullName, ($f.Length / 1MB))
