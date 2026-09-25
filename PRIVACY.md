# Privacy Policy

RMX3171 Control Centre is a local-first application designed to respect your data privacy and device integrity.

## Local First
This application runs entirely locally on your Windows PC. It does not require any cloud service or internet connectivity to function (other than fetching standard NuGet packages during compilation).

## Device Information
* All device information (such as hardware specs, storage usage, and battery statistics) is read purely through standard ADB (Android Debug Bridge) commands.
* **No telemetry is intentionally uploaded.**
* Device information is never automatically transmitted to the developer, any third party, or any remote server.

## Logging
* Application logs and raw ADB command outputs remain strictly local on your machine (stored in `%LocalAppData%\RMX3171ControlCentre\Logs`).
* You are in complete control of your diagnostic reports. Users should independently review their own diagnostic exports before sharing them publicly (e.g., in bug reports), as they may contain personal identifiers like serial numbers, MAC addresses, or application names.
