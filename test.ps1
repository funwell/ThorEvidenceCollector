$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1') -BuildTestsOnly
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$testExe = Join-Path $root 'dist\CollectorCoreTests.exe'
& $testExe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'CORE_TESTS=PASS'
