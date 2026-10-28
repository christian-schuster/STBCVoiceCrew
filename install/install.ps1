<#
  STBC Voice Crew – installer / updater for Star Trek: Bridge Crew (Windows)

  One-line install (PowerShell or the Windows "Run" box via powershell):
    powershell -ExecutionPolicy Bypass -c "irm https://github.com/christian-schuster/STBCVoiceCrew/releases/latest/download/install.ps1 | iex"

  What it does:
    1. Finds the game (Steam libraries, Ubisoft Connect, or asks for stbc.exe).
    2. Downloads the latest release ZIP from GitHub and unpacks it into the game folder
       (BepInEx mod loader + the mod). Speech models and voices are downloaded later by the mod itself when needed.
    3. Removes the mod's old plugin folder "StbcAiCrew" if present (data is taken over by the mod on first start).
  Options:  -GameDir "<folder with stbc.exe>"   -Uninstall
#>
param([string]$GameDir, [switch]$Uninstall)
$ErrorActionPreference = "Stop"
$Repo = "christian-schuster/STBCVoiceCrew"                      # GitHub owner/repository
$AppId = "527100"                       # Steam app id of Star Trek: Bridge Crew
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Find-Game {
    $found = New-Object System.Collections.Generic.List[string]
    # Steam: all libraries from libraryfolders.vdf, the one with appmanifest_527100.acf has the game
    foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
        $steam = (Get-ItemProperty $key -ErrorAction SilentlyContinue)
        $root = if ($steam.SteamPath) { $steam.SteamPath } else { $steam.InstallPath }
        if (-not $root) { continue }
        $libs = @($root)
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) { $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } }
        foreach ($lib in ($libs | Select-Object -Unique)) {
            $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
            if (-not (Test-Path $acf)) { continue }
            $dir = [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
            $path = Join-Path $lib "steamapps\common\$dir"
            if (Test-Path (Join-Path $path "stbc.exe")) { $found.Add((Resolve-Path $path).Path) }
        }
    }
    # Ubisoft Connect
    foreach ($k in Get-ChildItem "HKLM:\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs" -ErrorAction SilentlyContinue) {
        $dir = (Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue).InstallDir
        if ($dir -and (Test-Path (Join-Path $dir "stbc.exe"))) { $found.Add((Resolve-Path $dir).Path) }
    }
    return $found | Select-Object -Unique
}

if (-not $GameDir) {
    $games = @(Find-Game)
    if ($games.Count -eq 1) { $GameDir = $games[0] }
    elseif ($games.Count -gt 1) {
        Write-Host "Star Trek: Bridge Crew found in several places:"
        for ($i = 0; $i -lt $games.Count; $i++) { Write-Host "  [$($i+1)] $($games[$i])" }
        $GameDir = $games[[int](Read-Host "Install into which one (number)") - 1]
    }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object System.Windows.Forms.OpenFileDialog -Property @{ Title = "Please select stbc.exe (Star Trek: Bridge Crew)"; Filter = "stbc.exe|stbc.exe" }
        if ($dlg.ShowDialog() -ne "OK") { throw "Game folder not selected." }
        $GameDir = Split-Path $dlg.FileName -Parent
    }
}
if (-not (Test-Path (Join-Path $GameDir "stbc.exe"))) { throw "stbc.exe not found in $GameDir" }
Write-Host "Game folder: $GameDir"

$plugins = Join-Path $GameDir "BepInEx\plugins"
if ($Uninstall) {
    foreach ($d in "STBCVoiceCrew", "STBC Voice Crew", "StbcAiCrew") { $p = Join-Path $plugins $d; if (Test-Path $p) { Remove-Item $p -Recurse -Force; Write-Host "Removed $p" } }
    Write-Host "STBC Voice Crew removed. (BepInEx stays; delete winhttp.dll, doorstop_config.ini and the BepInEx folder if no other mod needs it.)"
    return
}
if (Get-Process stbc -ErrorAction SilentlyContinue) { throw "Please close Star Trek: Bridge Crew first." }

# Latest release: the asset STBC-Voice-Crew-<version>.zip
$rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ "User-Agent" = "STBC-Voice-Crew-Installer" }
$asset = $rel.assets | Where-Object { $_.name -like "STBC-Voice-Crew-*.zip" } | Select-Object -First 1
if (-not $asset) { throw "No release ZIP found in $Repo." }
$zip = Join-Path $env:TEMP $asset.name
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB) ..."
Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

# Old plugin folders: the mod takes over their data on first start; only the old DLLs must go, or BepInEx might load them
foreach ($d in "StbcAiCrew", "STBC Voice Crew") {
    foreach ($n in "StbcAiCrew.dll", "de.phuntasyproductions.stbcvoicecrew.dll") {
        $old = Join-Path $plugins "$d\$n"
        if (Test-Path $old) { Remove-Item $old -Force }
    }
}

Expand-Archive $zip -DestinationPath $GameDir -Force
Remove-Item $zip -Force
Write-Host "STBC Voice Crew $($rel.tag_name) installed. Start the game as usual." -ForegroundColor Green
