[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\release'),
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.-]{0,63}$')]
    [string]$Version = 'local'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$directory = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $directory) { throw 'Choose a new release output directory.' }
[IO.Directory]::CreateDirectory($directory) | Out-Null
$package = Join-Path $directory 'WindowsDock-win-x64'
$payload = Join-Path $package 'payload'
$native = Join-Path $directory 'native'

$cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue).Source
if (!$cmake) {
    $finder = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $finder) {
        $cmake = & $finder -latest -products '*' -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' |
            Select-Object -First 1
    }
}
if (!$cmake) { throw 'Install Visual Studio CMake and the Windows C++ build tools.' }

& $cmake -S (Join-Path $repo 'src\NativeTaskbar') -B $native -G 'Visual Studio 18 2026' -A x64
if ($LASTEXITCODE -ne 0) { throw 'Could not configure the native controller.' }
& $cmake --build $native --config Release
if ($LASTEXITCODE -ne 0) { throw 'Could not build the native controller.' }

$publish = @('publish', (Join-Path $repo 'src\WindowsDock'), '-c', 'Release', '-r', 'win-x64', '--self-contained',
    '-p:PublishReadyToRun=true', '--artifacts-path', (Join-Path $directory 'managed'), '-o', $payload)
& dotnet @publish
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the Windows Dock dashboard.' }

foreach ($file in 'NativeTaskbar.exe', 'NativeTaskbar.Xaml.dll') {
    Copy-Item -LiteralPath (Join-Path (Join-Path $native 'Release') $file) -Destination $payload
}

$assetsPath = Join-Path $directory 'managed\obj\WindowsDock\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$dependencies = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object { $_.Value.path })
foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
    foreach ($runtime in $framework.downloadDependencies | Where-Object { $_.name -match '\.Runtime\.win-x64$' }) {
        if ($runtime.version -match '^\[([0-9.]+),\s*\1\]$') {
            $dependencies += $runtime.name.ToLowerInvariant() + '/' + $Matches[1]
        }
    }
}
foreach ($dependency in ($dependencies | Sort-Object -Unique)) {
    foreach ($location in $assets.packageFolders.PSObject.Properties.Name) {
        $folder = Join-Path $location $dependency
        if (!(Test-Path -LiteralPath $folder -PathType Container)) { continue }
        $notices = Get-ChildItem -LiteralPath $folder -File | Where-Object {
            $_.Name -match '^(license|copying|notice|third[-_. ]party[-_. ]?notices?)' -or $_.Extension -eq '.nuspec'
        }
        if ($notices) {
            $dest = Join-Path (Join-Path $payload 'licenses') $dependency
            [IO.Directory]::CreateDirectory($dest) | Out-Null
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $dest }
        }
        break
    }
}

$contents = Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    @{
        Path = $_.FullName.Substring($payload.Length + 1)
        Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
@{ Schema = 1; Version = $Version; Files = @($contents) } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $payload 'manifest.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-WindowsDock.ps1') -Destination $package

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $directory "WindowsDock-$Version-win-x64.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($package, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII -Value "$checksum  $(Split-Path -Leaf $archive)"
Write-Output $archive
