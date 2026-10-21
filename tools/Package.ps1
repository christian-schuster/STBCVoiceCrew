# Baut das Veröffentlichungspaket (Nexus Mods): BepInEx 5 + Mod-DLL + Vosk (Deutsch und Englisch).
# Aufruf:  pwsh tools\Package.ps1 [-GameDir "...\Star Trek Bridge Crew"] [-NoBepInEx]
# Ergebnis: dist\<Name>-<Version>.zip, zum Entpacken direkt in den Spielordner.
param(
    [string]$GameDir = "G:\SteamLibrary\steamapps\common\Star Trek Bridge Crew",
    [string]$Name = "STBC-Voice-Crew",
    [switch]$NoBepInEx
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$src = Join-Path $repo "src\StbcAiCrew"

# Version aus dem BepInPlugin-Attribut
$plugin = Get-Content (Join-Path $src "Plugin.cs") -Raw
if ($plugin -notmatch 'BepInPlugin\(Guid, "[^"]*", "(?<v>[\d.]+)"\)') { throw "Version nicht gefunden" }
$version = $Matches.v

Write-Host "Baue $Name $version ..."
dotnet build (Join-Path $src "StbcAiCrew.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen" }
$dll = Get-ChildItem (Join-Path $src "bin\Release") -Recurse -Filter StbcAiCrew.dll | Sort-Object LastWriteTime -Descending | Select-Object -First 1

$stage = Join-Path $repo "dist\$Name-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$pluginDir = Join-Path $stage "BepInEx\plugins\StbcAiCrew"
New-Item -ItemType Directory -Force $pluginDir | Out-Null

# Mod
Copy-Item $dll.FullName $pluginDir

# Vosk: Bibliotheken und die beiden kleinen Modelle (aus der installierten Mod)
$voskSrc = Join-Path $GameDir "BepInEx\plugins\StbcAiCrew\vosk"
$voskDst = Join-Path $pluginDir "vosk"
New-Item -ItemType Directory -Force $voskDst | Out-Null
Copy-Item (Join-Path $voskSrc "*.dll") $voskDst
foreach ($m in "vosk-model-small-de-0.15", "vosk-model-small-en-us-0.15") {
    if (-not (Test-Path (Join-Path $voskSrc $m))) { throw "Vosk-Modell fehlt: $m" }
    Copy-Item (Join-Path $voskSrc $m) $voskDst -Recurse
}

# BepInEx 5 (wie im Spielordner installiert), damit Spieler nur entpacken müssen
if (-not $NoBepInEx) {
    Copy-Item (Join-Path $GameDir "winhttp.dll") $stage
    Copy-Item (Join-Path $GameDir "doorstop_config.ini") $stage
    Copy-Item (Join-Path $GameDir ".doorstop_version") $stage
    $core = Join-Path $stage "BepInEx\core"
    New-Item -ItemType Directory -Force $core | Out-Null
    Get-ChildItem (Join-Path $GameDir "BepInEx\core") -File | Copy-Item -Destination $core
}

# Anleitung und Lizenzhinweise
Copy-Item (Join-Path $repo "docs\release\README.txt") $stage
Copy-Item (Join-Path $repo "docs\release\THIRD-PARTY-NOTICES.txt") $stage

# Datenschutz: keine lokalen Pfade oder persönlichen Angaben in eigenen Dateien (DLL, Texte)
$private = '[A-Za-z]:\\[^\x00\s]{2,}', '\\Users\\', 'schuster', 'Christian', 'CeeBeeEss', 'Plantronics', '\.pdb'
$own = @(Get-Item (Join-Path $pluginDir "StbcAiCrew.dll")) + (Get-ChildItem $stage -File -Filter *.txt) + (Get-ChildItem $stage -File -Filter *.ini)
foreach ($f in $own) {
    $b = [IO.File]::ReadAllBytes($f.FullName)
    foreach ($t in [Text.Encoding]::ASCII.GetString($b), [Text.Encoding]::Unicode.GetString($b)) {
        foreach ($p in $private) {
            $m = [regex]::Match($t, $p, 'IgnoreCase')
            if ($m.Success) { throw "Private Angabe in $($f.Name): '$($m.Value)' – Paket nicht erstellt." }
        }
    }
}

$zip = Join-Path $repo "dist\$Name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
# Compress-Archive übernimmt Punktdateien nicht immer: prüfen
Add-Type -AssemblyName System.IO.Compression.FileSystem
$entries = [System.IO.Compression.ZipFile]::OpenRead($zip).Entries | ForEach-Object { $_.FullName }
if (-not $NoBepInEx -and -not ($entries -contains ".doorstop_version")) {
    $z = [System.IO.Compression.ZipFile]::Open($zip, "Update")
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, (Join-Path $stage ".doorstop_version"), ".doorstop_version") | Out-Null
    $z.Dispose()
}
"{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
