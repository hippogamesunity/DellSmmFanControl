# Publishing a release

1. Commit and push the release changes in GitHub Desktop.
2. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1`.
3. Open the repository's **Releases** page and choose **Draft a new release**.
4. Create tag `v0.1.0` on `main`, with title **Dell SMM Fan Control 0.1.0**.
5. Attach the `win-x64.zip` and `SHA256SUMS.txt` files from `artifacts/releases/`.
6. Paste the prepared release notes, including the driver source link, and publish.

The portable ZIP contains the EXE, signed driver, licenses and attribution.
.NET 9 is included in the EXE. There is no installer or separate source ZIP.
Driver sources stay in `dependencies/driver-source/`; GitHub automatically provides
source archives for the release tag. Keep clear source access links in the release
notes and driver notice when distributing the GPL driver.

Before publishing, manually verify the UI/hardware on the known machine. Building
and packaging do not run the app, change fan states, or register startup tasks.
Other Dell models remain unverified. The application uses PolyForm Noncommercial;
the driver and .NET retain their own licenses. See `NOTICE.md`.

Use a supported SDK/runtime for release builds. .NET 9 support ends on
10 November 2026; migrate to .NET 10 LTS before maintaining releases beyond then.
