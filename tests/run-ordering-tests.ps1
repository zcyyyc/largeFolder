$ErrorActionPreference = 'Stop'
$testRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$sandbox = Join-Path $testRoot ('ordering-sandbox-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandbox | Out-Null
$compilerDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $compilerDirectory 'csc.exe'))) {
    $compilerDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $compilerDirectory 'csc.exe'
$visualBasic = Join-Path $compilerDirectory 'Microsoft.VisualBasic.dll'
$outputExecutable = Join-Path $sandbox 'OrderingTests.exe'
$sourceRoot = Join-Path (Split-Path -Parent $testRoot) 'src'
$compilerArguments = @(
    '/nologo',
    '/target:exe',
    "/out:$outputExecutable",
    '/reference:System.Runtime.Serialization.dll',
    "/reference:$visualBasic",
    (Join-Path $sourceRoot 'Storage.cs'),
    (Join-Path $sourceRoot 'ItemOrdering.cs'),
    (Join-Path $testRoot 'OrderingTests.cs')
)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Ordering test compilation failed.' }
& $outputExecutable $sandbox
if ($LASTEXITCODE -ne 0) { throw 'Ordering tests failed.' }
