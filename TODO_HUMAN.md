# Actions requiring the project owner

- Register an application with Discord and provide its Application ID before enabling Discord Rich Presence.
- Create a Steam Web API key at `https://steamcommunity.com/dev/apikey` to load Steam achievements. Enter it in ARKADIA Settings; it is encrypted for the current Windows account.
- Create a CurseForge API key only if/when catalog enrichment is enabled; local installed modpacks are detected without it.
- Register the Microsoft application and obtain Mojang approval before any Minecraft online authentication is enabled; the current provider only detects and opens the local launcher.
- Confirm the Rinaorc launcher path or URI protocol if the registry and common detection locations do not find it.
- Choose a telemetry endpoint before enabling telemetry delivery. Telemetry remains disabled by default.
- Obtain an Authenticode code-signing certificate (or apply to SignPath Foundation) and add its base64-encoded PFX and password as the `ARKADIA_SIGNING_CERTIFICATE_BASE64` and `ARKADIA_SIGNING_CERTIFICATE_PASSWORD` repository secrets. Until then, GitHub releases remain unsigned and may show a SmartScreen warning; see [docs/release.md](docs/release.md).
