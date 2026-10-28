# GitHub release

1. Create the repository on GitHub (public, no README/license – both are in this repo), e.g. `owner/stbc-voice-crew`.
2. Fill in the repository name: replace `__REPO__` in `install/install.ps1`, `install/Installer/Program.cs`,
   `src/StbcAiCrew/UpdateCheck.cs` and `README.md` (or pass `-Repo owner/name` to the package script, which fills it
   into the published `install.ps1`; the setup and the mod need it at build time).
3. Build the release files:
   ```powershell
   pwsh tools/Package.ps1 -Repo owner/stbc-voice-crew
   ```
   Result in `dist/`: `STBC-Voice-Crew-<version>.zip`, `STBC-Voice-Crew-Setup.exe`, `install.ps1`.
   The script refuses to package if it finds local paths or personal data in the files.
4. Push and tag:
   ```powershell
   git remote add origin https://github.com/owner/stbc-voice-crew.git
   git push -u origin main
   git tag v1.1.0
   git push origin v1.1.0
   ```
5. Create the release for tag `v1.1.0` (title "STBC Voice Crew 1.1.0", notes from `CHANGELOG.txt`) and attach the three
   files from `dist/`. The setup, the install command and the mod's update check all use "latest release", so the
   asset names must stay `STBC-Voice-Crew-<version>.zip`, `STBC-Voice-Crew-Setup.exe` and `install.ps1`.
