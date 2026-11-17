[CmdletBinding()]
param(
    [ValidateSet('Install', 'Update', 'Rollback', 'Uninstall', 'Status')]
    [string]$Action = 'Install',
    [string]$PackagePath = (Join-Path $PSScriptRoot 'payload'),
    [string]$InstallRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\WindowsDock'),
    [switch]$NoShellIntegration,
    [switch]$NoStartAtSignIn
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$versions = Join-Path $root 'versions'
$stateFile = Join-Path $root 'installation.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'WindowsDock'
$programs = [Environment]::GetFolderPath('Programs')

function Assert-LocalPath([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:\\') { throw 'Use a local Windows directory.' }
    $entry = [IO.DirectoryInfo]::new($Path)
    while ($null -ne $entry) {
        if ($entry.Exists -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Use a folder without links: $($entry.FullName)"
        }
        $entry = $entry.Parent
    }
}

function Get-BuildPath([string]$Id) {
    if ($Id -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid installed build identifier.' }
    Join-Path $versions $Id
}

function Get-PayloadFile([string]$Directory, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Split('\') -match '^(?:\.{1,2}|)$|[ .]$' -or $Relative -match '[:/*?<>|"]') {
        throw "Invalid path in the package manifest: $Relative"
    }
    $file = [IO.Path]::GetFullPath((Join-Path $Directory $Relative))
    $prefix = $Directory.TrimEnd('\') + '\'
    if (!$file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The package manifest refers to a file outside its package.'
    }
    Assert-LocalPath (Split-Path -Parent $file)
    $item = Get-Item -LiteralPath $file
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "The package entry is not an ordinary file: $Relative"
    }
    $file
}

function Read-Manifest([string]$Directory) {
    Assert-LocalPath $Directory
    $file = Get-PayloadFile $Directory 'manifest.json'
    if ((Get-Item -LiteralPath $file).Length -gt 1048576) { throw 'The package manifest is too large.' }
    $manifest = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($manifest.Schema -ne 1 -or $manifest.Files.Count -lt 4 -or $manifest.Files.Count -gt 1200) {
        throw 'Unsupported package manifest.'
    }
    $names = @{}
    foreach ($entry in $manifest.Files) {
        if (!$entry.Path -or $entry.Hash -notmatch '^[a-f0-9]{64}$' -or $names.ContainsKey([string]$entry.Path) -or
            $entry.Path -in @('manifest.json', 'install-owner.txt')) { throw 'Invalid file entry in the package manifest.' }
        $names[[string]$entry.Path] = $true
        $source = Get-PayloadFile $Directory $entry.Path
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Hash) {
            throw "Package integrity check failed: $($entry.Path)"
        }
    }
    foreach ($required in 'WindowsDock.exe', 'WindowsDock.deps.json', 'NativeTaskbar.exe', 'NativeTaskbar.Xaml.dll') {
        if (!$names.ContainsKey($required)) { throw "Package is missing $required." }
    }
    $manifest
}

function Assert-OwnedBuild([string]$Id, [string]$Owner) {
    $directory = Get-BuildPath $Id
    $marker = Get-PayloadFile $directory 'install-owner.txt'
    $manifest = Get-PayloadFile $directory 'manifest.json'
    if ((Get-Content -LiteralPath $marker -Raw).Trim() -ne $Owner -or
        (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $Id) {
        throw 'The saved Windows Dock build does not belong to this installation.'
    }
    $directory
}

function Write-State($State) {
    $pending = Join-Path $root ('.state-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($pending, ($State | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($stateFile)) { [IO.File]::Replace($pending, $stateFile, (Join-Path $root 'installation.backup.json')) }
    else { [IO.File]::Move($pending, $stateFile) }
}

function Is-OwnedEntry([string]$Value) {
    if ($Value -notmatch '^"([^"]+)" --start-at-logon$') { return $false }
    $Matches[1].StartsWith($versions + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Publish-ShellEntry([string]$Dashboard, [bool]$EnableStartup) {
    [IO.Directory]::CreateDirectory($programs) | Out-Null
    $shortcut = Join-Path $programs 'Windows Dock.lnk'
    $oldRun = Get-ItemPropertyValue -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue
    $oldLink = if ([IO.File]::Exists($shortcut)) { [IO.File]::ReadAllBytes($shortcut) } else { $null }
    if ($oldLink -and ((Get-Item -LiteralPath $shortcut).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The Start menu shortcut is linked elsewhere.'
    }
    try {
        if ($Dashboard) {
            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($shortcut)
            if ($oldLink -and [IO.Path]::GetFileName($link.TargetPath) -ne 'WindowsDock.exe') {
                throw 'A different app already uses the Windows Dock Start menu shortcut.'
            }
            $link.TargetPath = $Dashboard
            $link.Arguments = '--open'
            $link.WorkingDirectory = Split-Path -Parent $Dashboard
            $link.Save()
        } elseif ($oldLink) {
            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($shortcut)
            if ($link.TargetPath.StartsWith($versions + '\', [StringComparison]::OrdinalIgnoreCase)) {
                [IO.File]::Delete($shortcut)
            }
        }
        if ($Dashboard -and $EnableStartup) {
            New-Item -Path $runKey -Force | Out-Null
            New-ItemProperty -LiteralPath $runKey -Name $runName -Value ('"' + $Dashboard + '" --start-at-logon') -PropertyType String -Force | Out-Null
        } elseif (Is-OwnedEntry $oldRun) {
            Remove-ItemProperty -LiteralPath $runKey -Name $runName
        }
    } catch {
        if ($oldLink) { [IO.File]::WriteAllBytes($shortcut, $oldLink) }
        elseif ($Dashboard) { [IO.File]::Delete($shortcut) }
        if ($oldRun) { New-ItemProperty -LiteralPath $runKey -Name $runName -Value $oldRun -PropertyType String -Force | Out-Null }
        elseif (Get-ItemPropertyValue -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -LiteralPath $runKey -Name $runName
        }
        throw
    }
}

function Use-Build($Old, $New, [bool]$EnableStartup) {
    Write-State $New
    try {
        if (!$New.Isolated) {
            $dashboard = if ($New.Current) { Join-Path (Get-BuildPath $New.Current) 'WindowsDock.exe' } else { '' }
            Publish-ShellEntry $dashboard $EnableStartup
        }
    } catch {
        Write-State $Old
        throw
    }
}

function Remove-OwnedBuild([string]$Id, [string]$Owner) {
    $directory = Assert-OwnedBuild $Id $Owner
    $manifest = Get-Content -LiteralPath (Join-Path $directory 'manifest.json') -Raw | ConvertFrom-Json
    $subfolders = @{}
    foreach ($entry in $manifest.Files) {
        $candidate = Join-Path $directory ([string]$entry.Path)
        if (![IO.File]::Exists($candidate)) { continue }
        $file = Get-PayloadFile $directory $entry.Path
        [IO.File]::Delete($file)
        $parent = Split-Path -Parent $file
        while ($parent -ne $directory) {
            $subfolders[$parent] = $true
            $parent = Split-Path -Parent $parent
        }
    }
    foreach ($folder in $subfolders.Keys | Sort-Object Length -Descending) {
        if (![IO.Directory]::EnumerateFileSystemEntries($folder).GetEnumerator().MoveNext()) { [IO.Directory]::Delete($folder) }
    }
    [IO.File]::Delete((Join-Path $directory 'manifest.json'))
    [IO.File]::Delete((Join-Path $directory 'install-owner.txt'))
    if (![IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator().MoveNext()) { [IO.Directory]::Delete($directory) }
}

if ($root.Length -lt 5) { throw 'Choose a dedicated Windows Dock install folder.' }
Assert-LocalPath $root
if ($Action -eq 'Status' -and ![IO.File]::Exists($stateFile)) { Write-Output 'Windows Dock is not installed here.'; return }
[IO.Directory]::CreateDirectory($root) | Out-Null
$gate = [IO.File]::Open((Join-Path $root '.installer.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    if ([IO.File]::Exists($stateFile)) {
        $state = Get-Content -LiteralPath (Get-PayloadFile $root 'installation.json') -Raw | ConvertFrom-Json
        if ($state.Schema -ne 1 -or $state.Owner -cnotmatch '^[0-9a-f]{32}$') { throw 'Invalid Windows Dock installation state.' }
        if ($NoShellIntegration -and !$state.Isolated) { throw 'This installation uses shell integration.' }
    } else {
        if ($Action -ne 'Install') { throw 'Install Windows Dock first.' }
        if (Get-ChildItem -LiteralPath $root -Force | Where-Object Name -ne '.installer.lock') {
            throw 'Choose an empty directory for the first install.'
        }
        $state = @{ Schema = 1; Owner = [Guid]::NewGuid().ToString('N'); Current = $null; Previous = $null
            Builds = @(); Isolated = [bool]$NoShellIntegration }
        Write-State $state
    }
    if ($Action -eq 'Status') {
        $state | ConvertTo-Json -Depth 4
        return
    }
    Assert-LocalPath $versions
    [IO.Directory]::CreateDirectory($versions) | Out-Null
    $next = @{ Schema = 1; Owner = $state.Owner; Current = $state.Current; Previous = $state.Previous
        Builds = @($state.Builds); Isolated = $state.Isolated }
    $currentRun = if (!$state.Isolated) { Get-ItemPropertyValue -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue }
    $enableStartup = !$NoStartAtSignIn -and (!$state.Current -or (Is-OwnedEntry $currentRun))

    if ($Action -in @('Install', 'Update')) {
        if ($Action -eq 'Update' -and !$state.Current) { throw 'Install Windows Dock first.' }
        $source = [IO.Path]::GetFullPath($PackagePath).TrimEnd('\')
        $manifest = Read-Manifest $source
        $id = (Get-FileHash -LiteralPath (Join-Path $source 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        $target = Get-BuildPath $id
        if (![IO.Directory]::Exists($target)) {
            $stage = Join-Path $versions ('.stage-' + [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            foreach ($entry in $manifest.Files) {
                $from = Get-PayloadFile $source $entry.Path
                $to = Join-Path $stage $entry.Path
                [IO.Directory]::CreateDirectory((Split-Path -Parent $to)) | Out-Null
                [IO.File]::Copy($from, $to)
            }
            [IO.File]::Copy((Join-Path $source 'manifest.json'), (Join-Path $stage 'manifest.json'))
            [IO.File]::WriteAllText((Join-Path $stage 'install-owner.txt'), $state.Owner)
            $null = Read-Manifest $stage
            [IO.Directory]::Move($stage, $target)
        }
        $null = Read-Manifest (Assert-OwnedBuild $id $state.Owner)
        $next.Builds = @($state.Builds | Where-Object { $_ -and $_ -ne $id }) + @($id)
        if ($state.Current -ne $id) {
            $next.Previous = $state.Current
            $next.Current = $id
        }
        Use-Build $state $next $enableStartup
        Write-Output "Windows Dock $($manifest.Version) installed in $target. The selected build is used at the next launch."
    } elseif ($Action -eq 'Rollback') {
        if (!$state.Previous) { throw 'There is no previous build to restore.' }
        $target = Assert-OwnedBuild $state.Previous $state.Owner
        $manifest = Read-Manifest $target
        $next.Current = $state.Previous
        $next.Previous = $state.Current
        Use-Build $state $next $enableStartup
        Write-Output "Windows Dock $($manifest.Version) selected for the next launch."
    } else {
        foreach ($id in $state.Builds) {
            if (!$id) { continue }
            $directory = Assert-OwnedBuild $id $state.Owner
            $controller = Join-Path $directory 'NativeTaskbar.exe'
            $clients = @(Get-CimInstance Win32_Process -Filter "Name='NativeTaskbar.exe' OR Name='WindowsDock.exe'" |
                Where-Object { $_.ExecutablePath -in @($controller, (Join-Path $directory 'WindowsDock.exe')) })
            foreach ($client in $clients | Where-Object Name -eq 'NativeTaskbar.exe') {
                Start-Process -FilePath $controller -ArgumentList @('--restore', [string]$client.ProcessId) -Wait -WindowStyle Hidden
                $process = Get-Process -Id $client.ProcessId -ErrorAction SilentlyContinue
                if ($process) { $null = $process.WaitForExit(12000) }
            }
        }
        $next.Current = $next.Previous = $null
        Use-Build $state $next $false
        foreach ($id in @($next.Builds)) {
            if (!$id) { continue }
            try {
                $directory = Assert-OwnedBuild $id $state.Owner
                $inUse = Get-CimInstance Win32_Process -Filter "Name='NativeTaskbar.exe' OR Name='WindowsDock.exe'" |
                    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase) }
                if ($inUse) { throw 'Windows Dock is still running.' }
                Remove-OwnedBuild $id $state.Owner
                $next.Builds = @($next.Builds | Where-Object { $_ -ne $id })
            } catch {
                Write-Warning "Windows still has files open from $id. Run Uninstall again after signing back in."
            }
        }
        Write-State $next
        Write-Output 'Windows Dock was removed from startup and the Start menu. Saved chats, settings, and other app data remain.'
    }
} finally {
    $gate.Dispose()
}
