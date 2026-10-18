# Third-party components

## Dell SMM driver

The application loads the unmodified signed `bzh_dell_smm_io_x64.sys` from
[DellFanCmd 1.1](https://github.com/AaronKelley/DellFanCmd/releases/tag/1.1).
The release archive is `DellFanCmd-1.1.zip`. Driver author: 424778940z;
the distributed binary was signed by Aaron Kelley.

| Item | Value |
| --- | --- |
| Binary size | 16,128 bytes |
| SHA-256 | `C524435027B6C252464DAB58E78CB01F7A09C820DAD870677AA15B62B974B2AF` |
| License | GPLv3; see `dependencies/DellSmm-LICENSE.txt` |
| Source repository | <https://github.com/424778940z/bzh-windrv-dell-smm-io> |
| Vendored source revision | `67786c69a80f2dd3e0d948cfaba6ced6c2c090e3` |

`dependencies/driver-source/` is an unmodified upstream snapshot, including
the original license, Visual C++/WDK build files and assembly source for the
low-level library. It is available in this repository and GitHub release source archives; the portable ZIP contains only binaries, licenses and notices.
It is kept separate from the application and is not built by the .NET solution.
The upstream README describes Visual Studio 2015 and Windows 10 x64. Driver
rebuilding/signing is outside this application's build process. No reproducible
binary comparison has been performed against the signed upstream binary.

The driver is accessed through Windows `DeviceIoControl`, with a 16-byte packet
and the Dell SMM commands implemented in `Hardware/DellSmmSession.cs`.
Its SHA-256 is checked before loading. The verified file remains open without
write/delete sharing until loading and device opening finish.

The temporary demand-start kernel service is named `DellFanControlSmm`. Existing
services with a different path or configuration are rejected. On normal exit,
the application attempts to restore BIOS cooling, stop the driver and delete
its service. A forced termination can leave a loaded driver and service entry;
reboot ends the loaded driver, but the service entry can remain until cleanup.

The noncommercial license of the application does not change the GPL rights in
the driver or upstream source. Distribute their original license, notices and
source access together. Separate license files do not by themselves settle whether any
future combination or copied code is a derivative work; review licensing when
changing the boundary or redistributing a modified driver.

## .NET

Portable builds include the .NET runtime and Windows Desktop runtime. Their MIT
license and third-party notices are copied from the exact restored runtime packs
by `scripts/Publish-Portable.ps1`. There are no application NuGet package references.

## Artwork

The application icon is project artwork in `DellSmmFanControl/Assets/`.
The RPM rotor is drawn from one vector blade repeated at 90-degree intervals;
its centered hub is drawn separately. No Dell wordmark is included.
