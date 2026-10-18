# Contributing

Changes and redistribution must follow [LICENSE.md](LICENSE.md). Original
contributions are submitted under the same terms. Third-party code must retain
its original license and attribution; do not copy GPL code into the application's
noncommercial source files.

Build on Windows with the .NET 9 SDK. Keep the WinForms designer file compatible
with Visual Studio: explicit control fields, construction and property assignment;
put hardware access and runtime logic outside `InitializeComponent`.

There is no automated test project. CI checks build/package integrity and never
loads the driver. Build success does not establish hardware compatibility.

Do not add new model-specific RPM overrides without an exact model/board/BIOS
match and measured running behavior. Preserve failover to BIOS, the temperature
checks, overheat handling, serialized hardware access and cleanup on exit.

Hardware verification is manual and requires the operator's permission. Do not
automatically sweep fan states, stop a fan, register startup tasks or install/load
a driver as part of a build, packaging step or CI job.

For a bug report, provide the model, BIOS, Windows version, selected mode and
status text. Remove personal identifiers and local paths. Report reproducible
steps and distinguish observed readings from expectations.
