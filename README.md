# RMX3171 Control Centre

A Windows desktop control centre for Android devices (specifically tailored for the Realme Narzo 30A / RMX3171) using ADB.

## Features

* **Device information:** Read properties and hardware configuration dynamically.
* **Storage information:** View internal storage capacity safely translated from raw linux mappings.
* **Battery diagnostics:** Access deep driver-level battery properties.
* **Empirical battery capacity testing:** Perform discharge capacity testing via current integration over time.
* **ADB logs:** Execute read-only ADB commands and observe log output.
* **Android 11 support:** Compatible with standard properties.
* **Wireless ADB support:** Recommended for discharge testing (ADB over Wi-Fi).

## Safety

This application is designed with a **read-only-first** philosophy. Diagnostic features use ADB to read information from the device. Destructive operations such as flashing, factory reset, bootloader unlocking, or system modification are **not performed automatically**. 

*Note: The empirical battery capacity test is an ESTIMATE generated from numerical integration of the `current_now` readings and is not equivalent to an official battery SOH (State of Health) measurement from the manufacturer.*

## Requirements

* Windows OS
* .NET 8.0 SDK (or newer)
* Android Platform Tools (`adb.exe`)
* USB debugging or Wireless debugging enabled on your device

## Installation & Build

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd RMX3171-Control-Centre
   ```
2. Setup Android Platform Tools:
   Download the [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools) and extract the contents to a folder named `platform-tools` in the root of the project output directory (or ensure `adb` is in your system PATH).
3. Restore and build:
   ```bash
   dotnet restore
   dotnet build
   ```
4. Run the application:
   ```bash
   dotnet run
   ```

## Usage

1. **Enable USB debugging:** Go to Developer Options on your Android device and enable USB debugging.
2. **Connect the Android device:** Plug your device into your PC.
3. **Authorize the PC:** Accept the RSA fingerprint prompt on your phone's screen.
4. **Launch the application:** The dashboard will automatically reflect the connection status.
5. **Refresh device information:** Navigate to the Device Info or Battery tabs.
6. **Use battery diagnostics:** View real-time properties from your device's power_supply subsystem.
7. **Capacity Testing:** To use the Empirical Discharge Capacity Tester, establish a wireless ADB connection (`adb tcpip 5555`, then `adb connect <IP>:5555`), unplug the USB cable so the phone actually discharges, and start the test from the sidebar.
