# Builds StartScreen.exe with the C# compiler that ships with Windows (no SDK needed).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /win32icon:"$PSScriptRoot\StartScreen.ico" /out:StartScreen.exe `
    /r:"$wpf\PresentationFramework.dll" /r:"$wpf\PresentationCore.dll" /r:"$wpf\WindowsBase.dll" `
    /r:"$wpf\UIAutomationClient.dll" /r:"$wpf\UIAutomationTypes.dll" `
    /r:System.Xaml.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:Microsoft.CSharp.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Management.dll `
    /r:"$fw\System.Runtime.dll" /r:"$fw\System.Runtime.InteropServices.WindowsRuntime.dll" /r:"$env:WINDIR\System32\WinMetadata\Windows.ApplicationModel.winmd" `
    /r:"$env:WINDIR\System32\WinMetadata\Windows.Storage.winmd" /r:"$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd" `
    StartScreen.cs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $PSScriptRoot\StartScreen.exe"
