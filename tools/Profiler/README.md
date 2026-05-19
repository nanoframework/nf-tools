# NanoProfiler

`NanoProfiler` is a Windows desktop profiling tool for .NET nanoFramework targets.
It helps analyze runtime behavior, allocation patterns, and memory usage during debugging and performance investigations.

## Purpose

- Profile runtime events from nanoFramework devices.
- Inspect allocation and memory behavior.
- Support troubleshooting and performance tuning.

## Install

`NanoProfiler` is built from source in this repository.

Prerequisites:

- Windows
- .NET 6 SDK (or compatible SDK for `net6.0-windows`)

Build:

```bash
cd tools/Profiler/NanoProfiler
dotnet build NanoProfiler.sln -c Release
```

Run the generated executable from the build output folder.
