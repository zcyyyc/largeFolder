$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$packageVersion = '1.0.0'
$packageOutput = Join-Path $packageRoot 'dist'
$packageStage = Join-Path $packageOutput ('stage-' + [Guid]::NewGuid().ToString('N'))
$packageFiles = @('LargeFolder.exe', 'LargeFolder.exe.config', 'README.md', 'CHANGELOG.md', 'LICENSE', '使用说明.txt', 'docs\app-icon.png', 'docs\preview.svg')
New-Item -ItemType Directory -Path $packageStage -Force | Out-Null
foreach ($packageFile in $packageFiles) {
    $packageSource = Join-Path $packageRoot $packageFile
    if (-not (Test-Path -LiteralPath $packageSource -PathType Leaf)) { throw "Missing release file: $packageFile. Run build.ps1 first." }
    $packageDestination = Join-Path $packageStage $packageFile
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($packageDestination)) -Force | Out-Null
    Copy-Item -LiteralPath $packageSource -Destination $packageDestination
}
$packageZip = Join-Path $packageOutput ("LargeFolder-v$packageVersion-windows-portable.zip")
Compress-Archive -Path (Join-Path $packageStage '*') -DestinationPath $packageZip -Force
$packageHash = Get-FileHash -LiteralPath $packageZip -Algorithm SHA256
$packageHashLine = $packageHash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($packageZip)
[IO.File]::WriteAllText((Join-Path $packageOutput 'SHA256SUMS.txt'), $packageHashLine + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
Write-Output ("Packaged: $packageZip")
