# Reproducible Test551 comparison point (TASK.md: Test551 must give the same result after
# additive changes). The PASS detail text contains measured values (Kepler weight w/cv and
# "ok in N ms"), so the hash covers only the ORDERED LIST OF CHECK NAMES:
#   * lines starting with "PASS " or "FAIL ",
#   * name = text before " | " (the whole line if there is no " | "),
#   * joined with LF, trailing LF, UTF-8 (no BOM).
#
# Usage (from anywhere):
#   powershell -ExecutionPolicy Bypass -File Tools/P1bTests/Study/qualification/baseline/verify_test551.ps1
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$dir = $PSScriptRoot
while ($dir -and -not (Test-Path (Join-Path $dir "Tools\P1bTests\Program.cs"))) {
    $dir = Split-Path -Parent $dir
}
if (-not $dir) { throw "repository root not found" }

$exe = Join-Path $dir "Tools\P1bTests\bin\$Configuration\net8.0\P1bTests.exe"
if (-not (Test-Path $exe)) { throw "build first: dotnet build Tools/P1bTests -c $Configuration" }

$log = Join-Path ([IO.Path]::GetTempPath()) "test551_verify.log"
& $exe "--exact" "Test551_IntegratorFrameworkSelfTest" "--log" $log | Out-Null

$names = New-Object System.Collections.Generic.List[string]
foreach ($line in [IO.File]::ReadAllLines($log, [Text.Encoding]::UTF8)) {
    if ($line -match '^(PASS|FAIL) ') {
        $i = $line.IndexOf(' | ')
        $names.Add($(if ($i -ge 0) { $line.Substring(0, $i) } else { $line }))
    }
}

$text = ($names -join "`n") + "`n"
$sha = [Security.Cryptography.SHA256]::Create()
$hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)) | ForEach-Object { $_.ToString("x2") }) -join ""
$fails = ($names | Where-Object { $_ -like "FAIL *" }).Count
"lines=$($names.Count) fails=$fails"
"SHA256=$hash"
if ($fails -ne 0) { exit 1 }
