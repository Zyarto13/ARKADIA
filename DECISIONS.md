# Decisions

## Initial implementation

- The application is created as a separate WPF/.NET 8 solution in this folder; the Unreal project is not modified.
- The reference image guides the visual language. Navigation and planned features follow the technical specification.
- Discord is represented as a launcher tile. Rich Presence requires an application ID and a supported SDK integration; implementation is not enabled until that dependency and registration are verified.
- The host currently uses the installed .NET 10 SDK to target .NET 8 because a .NET 8 SDK is not installed on this machine. A .NET 8 SDK build remains the release validation target.
- The Sound page uses NAudio's endpoint notifications only while visible, and uses the documented Core Audio APIs for device/session control. Changing the system default device is deferred: it requires the undocumented `IPolicyConfig` API and must not be enabled without a disabled-by-default `Audio:EnableDefaultDeviceSwitch` gate and a reliable fallback.
