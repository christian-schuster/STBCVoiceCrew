# GitHub release

Repository: https://github.com/christian-schuster/STBCVoiceCrew (the name is built into `install/install.ps1`,
`install/Installer/Program.cs`, `src/STBCVoiceCrew/UpdateCheck.cs` and `README.md`).

1. Build the release files:
   ```powershell
   pwsh tools/Package.ps1
   ```
   Result in `dist/`: `STBC-Voice-Crew-<version>.zip`, `STBC-Voice-Crew-Setup.exe`, `install.ps1`.
   The script refuses to package if it finds local paths or personal data in the files.
2. Push and tag:
   ```powershell
   git push origin main
   git tag v1.1.1
   git push origin v1.1.1
   ```
3. Create the release for tag `v1.1.1` (title "STBC Voice Crew 1.1.1 – Reliable Course Setting", notes from `release-notes-1.1.1.md`) and attach the three
   files from `dist/`. The setup, the install command and the mod's update check all use "latest release", so the
   asset names must stay `STBC-Voice-Crew-<version>.zip`, `STBC-Voice-Crew-Setup.exe` and `install.ps1`.
