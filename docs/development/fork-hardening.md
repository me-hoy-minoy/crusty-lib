# Local security fork

This work starts from Libation v14.2.5, exact upstream commit `8003ae6f29ef28f057e1df994da13817dbdda670`. The working branch is `security/hardening`. The upstream license and attribution remain intact. The app remains C#/.NET; this is not a Rust rewrite.

## Implemented changes

- File moves invoke `/usr/bin/stat` using separate arguments. Paths never enter a shell command. Linux uses the file's device number, and an unavailable device ID does not count as a same-device match.
- Configuration paths expand Unix `$NAME`, `${NAME}` and leading `~/` directly, or Windows `%NAME%` using the platform API. Unknown Unix variables remain literal. Shell quoting, command substitutions, pipelines, globbing and shell parameter operators are not interpreted. Environment values are expanded once.
- Both graphical login flows log an external-login submission event without the callback URL.
- Encrypted token storage uses an explicitly supplied key or the OS secret store. It does not create a portable fallback key. An existing adjacent `libation-master.key` requires explicit selection with `LIBATION_MASTER_KEY_FILE`; it is preserved and blocks automatic selection of a different OS key. Explicit portable keys remain sensitive and can defeat directory-level protection if stored with credentials. Existing plaintext account data is not automatically migrated by changing preferences.
- Widevine device loading uses local account settings only. The third-party acquisition implementation is absent. `UseWidevine` remains off by default, and enabling it does not authorize an external account-information request. Without local device data, the downloader uses its standard-format path; titles/formats requiring Widevine may be unavailable. A future service integration needs a reviewed service, fixed HTTPS destinations, explicit disclosure/consent and redirect controls.
- The concrete updater does not check, download or install upstream binaries. Startup update checks default off; an explicit check explains the source-only workflow. Existing true settings cannot make the concrete updater fetch an installer. UI test doubles retain their simulated upgrade behavior.
- Audiobookshelf addresses without a scheme use HTTPS. Explicit HTTP is accepted only for loopback addresses; remote/LAN servers require HTTPS. Embedded URL credentials are rejected, and API clients do not follow redirects. Local HTTP users must include `http://` explicitly.
- Both account-export dialogs warn that the export contains unencrypted account credentials and default to declining. Export remains an explicit interoperability feature, and its JSON output is still sensitive. The Avalonia save dialog describes the exported credentials.

The local-only Widevine policy, source-only updater and HTTPS defaults are reversible implementation choices for this security fork, based on the user's requested security fixes and source-build workflow. They are not claims about upstream intent or permanent product requirements. Branding, a new UI and integration with SoundGrab are separate work.

## Validation

Regression coverage accompanies literal file moves, configuration expansion, fail-closed secret-store selection, missing/invalid local CDM data, source-only update behavior, and Audiobookshelf URL validation. Filesystem tests use disposable fixtures, including a marker that detects accidental shell execution. Secret-store tests inject unavailable/available stores instead of requiring the real keychain; tests that deliberately exercise an OS store remain opt-in.

The environment used for this patch has **no .NET SDK**. Compilation, MSTest execution and GUI validation have **not** been completed. Static review and `git diff --check` do not replace those checks. No NuGet restore, application launch, Audible sign-in or installation is performed as part of this work.

Before installation:

1. Review the resolved macOS NuGet/runtime/native dependency inputs and current advisories; preserve versions, package hashes and lockfiles. Do not dismiss an advisory as harmless solely because an upstream instruction says so.
2. Use an isolated build environment with the pinned SDK and no personal credentials. Run the affected tests and compile both GUI projects; Windows UI behavior requires validation on Windows.
3. Validate the package in a fresh profile before real-account sign-in. Confirm the actual key store and inspect network destinations.

Affected test commands, after dependency review and toolchain provisioning:

```sh
dotnet test --project Source/_Tests/FileManager.Tests/FileManager.Tests.csproj
dotnet test --project Source/_Tests/LibationFileManager.Tests/LibationFileManager.Tests.csproj
dotnet test --project Source/_Tests/AudibleUtilities.Tests/AudibleUtilities.Tests.csproj
dotnet test --project Source/_Tests/ApplicationServices.Tests/ApplicationServices.Tests.csproj
dotnet test --project Source/_Tests/LibationUiBase.Tests/LibationUiBase.Tests.csproj
dotnet build Source/LibationAvalonia/LibationAvalonia.csproj
dotnet build Source/LibationWinForms/LibationWinForms.csproj
dotnet build Source/LibationCli/LibationCli.csproj
```

Run with a process timeout and leave `LIBATION_TEST_OS_SECRET_STORE` unset unless deliberately testing the real store. These commands are a validation plan, not successful results.

## Remaining review work

The initial security reports remain in the project container's `security-review/libation-v14.2.5/reports/`. They describe the upstream snapshot, not the patched source. Dependency implementations, media/native code, file-icon AppleScript interpolation, downloaded supplemental-file containment, broad macOS entitlements and published installer provenance remain outside the fixes above. No unconditional safety claim follows from this patch.
