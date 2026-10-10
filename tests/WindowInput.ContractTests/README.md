# Window and input behavior tests

Windows x64 / .NET Framework 4.7.2. Tests compile production window, hook and input-buffer sources with minimal service stubs. No test framework or new production dependency is required.

```powershell
dotnet build tests/WindowInput.ContractTests/WindowInput.ContractTests.csproj -c Release
& tests/WindowInput.ContractTests/bin/Release/net472/WindowInput.ContractTests.exe
```

The native tests briefly show small nonactivating fixture windows. They verify actual Win32 hit testing and z-order, not just planned state values.

To launch the real release executable against native game/external-app stand-ins, with separate settings:

```powershell
& tests/WindowInput.ContractTests/bin/Release/net472/WindowInput.ContractTests.exe --smoke '<absolute release exe>' '<isolated temporary directory>'
```

Append `--debug` to use the production debug-helper input backend. This smoke test briefly changes foreground between its own windows and restores the previous foreground on exit. It checks attachment, service startup, visibility and restoration together; it does not replace testing actual GW2 or third-party injected overlays.
