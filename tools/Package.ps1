# Builds the release files in dist\:
#   STBC-Voice-Crew-<version>.zip      unpack into the game folder (BepInEx 5 + the mod; speech models and voices are
#                                      downloaded by the mod on first use)
#   STBC-Voice-Crew-Setup.exe          one-click installer/updater (downloads the latest release ZIP from GitHub)
#   install.ps1                        one-line install command (same as the setup, for PowerShell)
# Usage:  pwsh tools\Package.ps1 [-GameDir "...\Star Trek Bridge Crew"] [-Repo owner/name] [-NoBepInEx]
# The game folder is only needed to copy BepInEx and the Vosk libraries; it is found via Steam if not given.
param(
    [string]$GameDir = $env:STBC_GAME_DIR,
    [string]$Repo = "__REPO__",
    [string]$Name = "STBC-Voice-Crew",
    [switch]$NoBepInEx
)
$ErrorActionPreference = "Stop"
$repoDir = Split-Path $PSScriptRoot -Parent
$src = Join-Path $repoDir "src\StbcAiCrew"
$PluginFolder = "STBC Voice Crew"

if (-not $GameDir) {
    # Same search as the installer: Steam libraries → appmanifest_527100.acf
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $libs = @($steam) + ([regex]::Matches((Get-Content (Join-Path $steam "steamapps\libraryfolders.vdf") -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' })
        foreach ($lib in $libs) {
            $p = Join-Path $lib "steamapps\common\Star Trek Bridge Crew"
            if (Test-Path (Join-Path $p "stbc.exe")) { $GameDir = $p; break }
        }
    }
    if (-not $GameDir) { throw "Game folder not found – pass -GameDir or set STBC_GAME_DIR." }
}

# Version from the BepInPlugin attribute
$plugin = Get-Content (Join-Path $src "Plugin.cs") -Raw
if ($plugin -notmatch 'BepInPlugin\(Guid, "[^"]*", "(?<v>[\d.]+)"\)') { throw "Version not found" }
$version = $Matches.v

Write-Host "Building $Name $version ..."
dotnet build (Join-Path $src "StbcAiCrew.csproj") -c Release -p:GameDir="$GameDir" -p:DeployToGame=false | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Mod build failed" }
dotnet build (Join-Path $repoDir "install\Installer\Installer.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
$dll = Get-ChildItem (Join-Path $src "bin\Release") -Recurse -Filter de.phuntasyproductions.stbcvoicecrew.dll | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$setup = Get-ChildItem (Join-Path $repoDir "install\Installer\bin\Release") -Recurse -Filter STBC-Voice-Crew-Setup.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1

$dist = Join-Path $repoDir "dist"
$stage = Join-Path $dist "$Name-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$pluginDir = Join-Path $stage "BepInEx\plugins\$PluginFolder"
New-Item -ItemType Directory -Force $pluginDir | Out-Null

# Mod
Copy-Item $dll.FullName $pluginDir

# Vosk runtime libraries only; the language models are downloaded by the mod when needed
$voskSrc = $null
foreach ($d in $PluginFolder, "StbcAiCrew") { $c = Join-Path $GameDir "BepInEx\plugins\$d\vosk"; if (Test-Path (Join-Path $c "libvosk.dll")) { $voskSrc = $c; break } }
if (-not $voskSrc) { throw "Vosk libraries (libvosk.dll) not found in the game's plugin folder." }
$voskDst = Join-Path $pluginDir "vosk"
New-Item -ItemType Directory -Force $voskDst | Out-Null
Copy-Item (Join-Path $voskSrc "*.dll") $voskDst

# BepInEx 5 (as installed in the game folder), so players only need to unpack
if (-not $NoBepInEx) {
    Copy-Item (Join-Path $GameDir "winhttp.dll") $stage
    Copy-Item (Join-Path $GameDir "doorstop_config.ini") $stage
    Copy-Item (Join-Path $GameDir ".doorstop_version") $stage
    $core = Join-Path $stage "BepInEx\core"
    New-Item -ItemType Directory -Force $core | Out-Null
    Get-ChildItem (Join-Path $GameDir "BepInEx\core") -File | Copy-Item -Destination $core
}

# Documentation and licenses
Copy-Item (Join-Path $repoDir "docs\release\README.txt") $stage
Copy-Item (Join-Path $repoDir "docs\release\THIRD-PARTY-NOTICES.txt") $stage
Copy-Item (Join-Path $repoDir "docs\release\CHANGELOG.txt") $stage
Copy-Item (Join-Path $repoDir "LICENSE") (Join-Path $stage "LICENSE.txt")

# Installer files with the GitHub repository filled in
$install = (Get-Content (Join-Path $repoDir "install\install.ps1") -Raw).Replace("__REPO__", $Repo)
[IO.File]::WriteAllText((Join-Path $dist "install.ps1"), $install, [Text.UTF8Encoding]::new($true))
Copy-Item $setup.FullName (Join-Path $dist "STBC-Voice-Crew-Setup.exe") -Force

# Privacy: no local paths or personal data in our own files (DLL, setup, texts)
# Generic patterns here; personal ones (names, e-mail, device names) come from tools\private-patterns.txt, which is not checked in
$private = @('[A-Za-z]:\\(Users|SteamLibrary|source)\\', '\\Users\\', '\.pdb')
$own_patterns = Join-Path $PSScriptRoot 'private-patterns.txt'
if (Test-Path $own_patterns) { $private += Get-Content $own_patterns | Where-Object { $_ -and -not $_.StartsWith('#') } }
$own = @(Get-Item (Join-Path $pluginDir "de.phuntasyproductions.stbcvoicecrew.dll")) + @(Get-Item (Join-Path $dist "STBC-Voice-Crew-Setup.exe")) + @(Get-Item (Join-Path $dist "install.ps1")) +
       (Get-ChildItem $stage -File -Filter *.txt) + (Get-ChildItem $stage -File -Filter *.ini)
foreach ($f in $own) {
    $b = [IO.File]::ReadAllBytes($f.FullName)
    foreach ($t in [Text.Encoding]::ASCII.GetString($b), [Text.Encoding]::Unicode.GetString($b)) {
        foreach ($p in $private) {
            $m = [regex]::Match($t, $p, 'IgnoreCase')
            if ($m.Success) { throw "Private data in $($f.Name): '$($m.Value)' – package not created." }
        }
    }
}

$zip = Join-Path $dist "$Name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
# Compress-Archive does not always include dot files: check
Add-Type -AssemblyName System.IO.Compression.FileSystem
$entries = [System.IO.Compression.ZipFile]::OpenRead($zip).Entries | ForEach-Object { $_.FullName }
if (-not $NoBepInEx -and -not ($entries -contains ".doorstop_version")) {
    $z = [System.IO.Compression.ZipFile]::Open($zip, "Update")
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, (Join-Path $stage ".doorstop_version"), ".doorstop_version") | Out-Null
    $z.Dispose()
}
foreach ($f in $zip, (Join-Path $dist "STBC-Voice-Crew-Setup.exe"), (Join-Path $dist "install.ps1")) { "{0}  ({1:N1} MB)" -f $f, ((Get-Item $f).Length / 1MB) }
