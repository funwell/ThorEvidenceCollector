param(
    [switch]$BuildTestsOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$tests = Join-Path $root 'tests'
$dist = Join-Path $root 'dist'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path $csc)) { throw "inbox csc.exe not found" }

New-Item -ItemType Directory -Path $dist -Force | Out-Null
$coreSources = @(
    (Join-Path $src 'CollectorCore.cs'),
    (Join-Path $src 'CaptureSession.cs'),
    (Join-Path $src 'EvidenceExporter.cs'),
    (Join-Path $src 'SerialDiscovery.cs'),
    (Join-Path $src 'PassiveSerialCapture.cs'),
    (Join-Path $src 'DemoTransport.cs'),
    (Join-Path $src 'Replay.cs'),
    (Join-Path $src 'SelfTestRunner.cs')
)
$testExe = Join-Path $dist 'CollectorCoreTests.exe'
$testArgs = @(
    '/nologo', '/langversion:5', '/optimize+', '/target:exe', ('/out:' + $testExe),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Management.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll'
) + $coreSources + @(Join-Path $tests 'CollectorCoreTests.cs')
& $csc @testArgs
if ($LASTEXITCODE -ne 0) { throw "CollectorCoreTests compilation failed with exit code $LASTEXITCODE" }

if (-not $BuildTestsOnly) {
    $appExe = Join-Path $dist 'ThorEvidenceCollector.exe'
    $appSources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
    $appArgs = @(
        '/nologo', '/langversion:5', '/optimize+', '/target:winexe', ('/out:' + $appExe),
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll', '/reference:System.Management.dll',
        '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll'
    ) + $appSources
    & $csc @appArgs
    if ($LASTEXITCODE -ne 0) { throw "ThorEvidenceCollector compilation failed with exit code $LASTEXITCODE" }
    Write-Output "APP_EXE=$appExe"
}
Write-Output "TEST_EXE=$testExe"
