# Dell SMM Fan Control

Fan control for Dell OptiPlex motherboards running **Windows 11 x64**.
Useful when moving a 7010, 9010 or similar board into another case without the
original front panel, external temperature sensor or second chassis fan.
The app controls the remaining fans through Dell SMM using CPU temperature.
It does not remove BIOS warnings or F1 prompts.

Tested on **OptiPlex 9010 SFF, BIOS A30, Core i7-3770**. Compatibility with
7010 and other models is not yet confirmed.

![Dell SMM Fan Control](docs/images/main-window.png)

Choose **Min**, **Normal**, **Max** or **Auto**. Auto keeps the fans quiet,
raising speed at 65 °C and 85 °C. The window shows RPM, fan percentage, CPU
temperature and its maximum since launch. Supports up to two running fans,
startup at sign-in and minimizing to the system tray, where temperature
monitoring and fan control continue in the background.

## Running

Extract the entire portable ZIP and run `DellSmmFanControl.exe` as administrator.
Keep the bundled `.sys` driver beside the executable.

The app uses **.NET 9 Windows Desktop Runtime (x64)**. It is included in the
portable package, so no separate .NET installation is needed. A
framework-dependent build requires that runtime to be installed.

## Building

Requires the **.NET 9 SDK** on Windows.

```powershell
dotnet build .\DellSmmFanControl.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1
```

## License

Application: [PolyForm Noncommercial 1.0.0](LICENSE.md), for noncommercial use.
The bundled driver retains its original **GPLv3** license; see
[third-party notices](NOTICE.md).
