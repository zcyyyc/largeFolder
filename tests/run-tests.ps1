$ErrorActionPreference = 'Stop'
$testRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$crossWorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'LargeFolder-tests'
$compilerDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $compilerDirectory 'csc.exe'))) {
    $compilerDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $compilerDirectory 'csc.exe'
$visualBasic = Join-Path $compilerDirectory 'Microsoft.VisualBasic.dll'
$outputExecutable = Join-Path $testRoot 'StorageTests.exe'
$applicationSource = Join-Path (Split-Path -Parent $testRoot) 'src\Storage.cs'
$testSource = Join-Path $testRoot 'StorageTests.cs'
New-Item -ItemType Directory -Path $crossWorkRoot -Force | Out-Null
$compilerArguments = @(
    '/nologo',
    '/target:exe',
    "/out:$outputExecutable",
    '/reference:System.Runtime.Serialization.dll',
    "/reference:$visualBasic",
    $applicationSource,
    $testSource
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Storage integration test compilation failed.' }
& $outputExecutable $testRoot $crossWorkRoot
if ($LASTEXITCODE -ne 0) { throw 'Storage integration tests failed.' }
