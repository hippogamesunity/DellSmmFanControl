# Publishing and releasing

## Repository

Publish the source tree with Git, not a ZIP of the entire workspace. The root
`.gitignore` excludes IDE state, local NuGet caches, build outputs, firmware
analysis and previous packages under `artifacts/`, user settings and key files.
Keep `dependencies/driver-source/`, the signed `.sys`, the original GPL text,
`LICENSE.md`, `NOTICE.md`, and all attribution in the public repository.

The root noncommercial license applies only to original project material.
The driver's GPLv3 and runtime licenses remain independent. Do not describe
the whole distribution as exclusively noncommercial or relicense dependencies.
Review any future copied code or driver modifications for license compatibility.

Inspect the staged file list before the first commit. Do not include personal
firmware dumps, service tags, screenshots containing usernames, private keys,
or local diagnostic reports. Those are not required to build the application.

## Version and package

1. Set `<Version>` in `DellSmmFanControl/DellSmmFanControl.csproj`.
2. On Windows, use a supported, fully patched .NET SDK. The current target is
   .NET 9; its support ends on 10 November 2026. Move to .NET 10 LTS before
   releases maintained beyond that date, and update the workflow/requirements.
3. Run `dotnet build .\DellSmmFanControl.sln -c Release -warnaserror`.
4. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1`.
5. Inspect the generated ZIP contents and SHA256SUMS under `artifacts/releases/`.
   The portable ZIP must contain the `.exe`, `.sys`, application license,
   original driver GPL/attribution, full upstream driver snapshot and .NET notices.
6. Perform a separately authorized manual hardware/UI check on the known machine:
   RPM and CPU temperature; mode changes; tray restore; duplicate launch;
   startup registration; sleep/resume; and BIOS control restoration on normal exit.
   Never run those checks automatically on a shared build runner.

The package script also creates a source ZIP using explicit source roots. It
excludes caches, local IDE settings and artifacts, but includes third-party driver
sources and licenses. Keep the source ZIP beside each binary release. The driver
is unmodified; its upstream source snapshot has not been rebuilt or compared
reproducibly against the signed binary.

## GitHub

Push the reviewed source to your public repository. GitHub Actions builds and
provides downloadable workflow artifacts; it does not create a public release.
After reviewing the package and manual results, create a tag such as `v0.1.0`,
create a GitHub Release for that tag, and attach the binary ZIP, source ZIP and
SHA256SUMS file from the same build. State the exact hardware verified and any
remaining compatibility limitations in the release notes.

The application executable is not Authenticode-signed by this project. The
bundled driver retains its upstream signature. SmartScreen reputation and
Windows driver acceptance are separate from a successful build.

## Review status

See [REVIEW.md](REVIEW.md) for the static review, changes made and verification
limits. A successful CI/build run does not establish firmware compatibility or
replace a legal review of a changed dependency arrangement.
