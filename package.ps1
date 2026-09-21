param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'test.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dist = Join-Path $root 'dist'
$exe = Join-Path $dist 'ThorEvidenceCollector.exe'
$readme = Join-Path $root 'README.txt'
$launcher = Join-Path $root 'Start-ThorEvidenceCollector.cmd'
if ([string]::IsNullOrEmpty($Version)) { $Version = Get-Date -Format 'yyyyMMdd-HHmmss' }
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('ThorEvidenceCollector-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'ThorEvidenceCollector.exe')
Copy-Item -LiteralPath $readme -Destination (Join-Path $stage 'README.txt')
Copy-Item -LiteralPath $launcher -Destination (Join-Path $stage 'Start-ThorEvidenceCollector.cmd')
$zip = Join-Path $dist ('ThorEvidenceCollector-' + $Version + '.zip')
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Output "PACKAGE_ZIP=$zip"
Write-Output "APP_EXE=$exe"
