# NeonDeck Desktop Gadgets — v1.5.0 (Windows 11 portable)

Double-click **`NeonDeck.exe`**. No installer, CMD window, Go, or Python is required on the end user's PC.

Unzip the **entire** package into a writable directory, then launch `NeonDeck.exe`. On first launch the native Windows GUI launcher compiles `NeonDeck.cs` into a cached `NeonDeckApp.exe` using the .NET Framework C# compiler available in Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, or 32-bit equivalent). Subsequent launches start the cached module directly. If the compiler is unavailable, an error is displayed and `NeonDeck_build_error.txt` is created if compilation fails. A precompiled application can also be built using the GitHub source release / Windows GitHub Actions workflow.

The dashboard contains CPU, NVIDIA GPU, Network and Drives widgets; move them freely, hide/show via the tray icon, and right-click the tray icon for configuration. The **same custom yellow/teal activity icon** is embedded in both the launcher executable and cached C# module, and appears in the tray.

Drives are displayed in a single vertical column and include local volumes and mapped network drives; hardware-dependent health, SSD wear and temperatures show `N/A` where unsupported. `EMPTY BIN` permanently empties the Recycle Bin without confirmation; `CLEAN TEMP` deletes a limited set of older temporary files.

Upgrade: exit the previous version, overwrite program files **except `settings.ini`**, and launch `NeonDeck.exe`. For auto-start, the application registers the launcher, not the cache EXE. Removal requires exiting, disabling auto-start if selected, and deleting the folder. No service or installer is used.

**Validation:** the native x64 Windows GUI launcher and icon resources have been built and inspected. Compilation/running of the WinForms application has **not** been verified on Windows 11 in this environment.
