[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$Output = (Join-Path $PSScriptRoot ('..\artifacts\installer-test-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$setup = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\scripts\Install-WindowsDock.ps1')).Path
$directory = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $directory) { throw 'Choose a new test directory.' }
[IO.Directory]::CreateDirectory($directory) | Out-Null
$installed = Join-Path $directory 'installed'
$updated = Join-Path $directory 'updated'
Copy-Item -LiteralPath $package -Destination $updated -Recurse
$manifestPath = Join-Path $updated 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.Version = 'installation-test-update'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

function Status() {
    & $setup -Action Status -InstallRoot $installed | ConvertFrom-Json
}

& $setup -Action Install -PackagePath $package -InstallRoot $installed -NoShellIntegration
$first = (Status).Current
if (!$first -or !(Test-Path -LiteralPath (Join-Path $installed "versions\$first\WindowsDock.exe"))) { throw 'Install did not select a build.' }

$dashboard = Join-Path $updated 'WindowsDock.exe'
$backup = Join-Path $directory 'dashboard-original.exe'
[IO.File]::Copy($dashboard, $backup)
[IO.File]::AppendAllText($dashboard, 'fixture appended after packaging')
$failed = $false
try { & $setup -Action Update -PackagePath $updated -InstallRoot $installed -NoShellIntegration }
catch {
    if ($_.Exception.Message -notlike 'Package integrity check failed:*') { throw }
    $failed = $true
}
if (!$failed -or (Status).Current -ne $first) { throw 'A modified package changed the active build.' }
[IO.File]::Copy($backup, $dashboard, $true)

& $setup -Action Update -PackagePath $updated -InstallRoot $installed -NoShellIntegration
$second = Status
if ($second.Current -eq $first -or $second.Previous -ne $first) { throw 'Update did not keep a rollback point.' }
& $setup -Action Rollback -InstallRoot $installed -NoShellIntegration
if ((Status).Current -ne $first) { throw 'The older build was not selected by rollback.' }

$keep = Join-Path $installed 'user-data-fixture.txt'
[IO.File]::WriteAllText($keep, 'Preserve data outside package ownership.')
& $setup -Action Uninstall -InstallRoot $installed -NoShellIntegration
$final = Status
if ($final.Current -or $final.Previous -or $final.Builds.Count -or ![IO.File]::Exists($keep)) {
    throw 'Uninstall did not remove only the managed files.'
}
Write-Output 'Install, modified payload rejection, update, rollback, and uninstall passed.'
