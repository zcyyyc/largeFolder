$ErrorActionPreference = 'Stop'
$buildRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler was not found. Build on Windows with .NET Framework 4.x installed.' }
$vb = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll'
if (-not (Test-Path -LiteralPath $vb)) {
    $vb = (Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1).FullName
}
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xaml.dll')
$args = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/out:' + (Join-Path $buildRoot 'LargeFolder.exe')),('/win32manifest:' + (Join-Path $buildRoot 'src\app.manifest')))
foreach ($ref in $refs) { $args += '/reference:' + (Join-Path $framework $ref) }
foreach ($ref in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')) { $args += '/reference:' + (Join-Path $framework ('WPF\' + $ref)) }
$args += '/reference:' + $vb
$icon = Join-Path $buildRoot 'src\LargeFolder.ico'
if (Test-Path -LiteralPath $icon) { $args += '/win32icon:' + $icon }
$args += @(Get-ChildItem -LiteralPath (Join-Path $buildRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @args
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built: ' + (Join-Path $buildRoot 'LargeFolder.exe'))
