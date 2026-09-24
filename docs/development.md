# Development

[README](../README.md) · [Русский README](../README.ru.md)

## Environment and build

Build and run the C# solution on Windows. [global.json](../global.json) pins .NET SDK 10.0.401 with `latestPatch` roll-forward, allowing newer patches in the same feature band. VS Code with C# Dev Kit is optional; open the repository in the Windows version of VS Code for F5 debugging. The workspace includes build, test, and publish tasks.

From the repository root:

```powershell
dotnet build llm-servy.slnx
dotnet run --project src/LlmServy
.\publish.ps1
```

Publishing creates `artifacts\publish\llm-servy.exe` plus a versioned ZIP and `.zip.sha256` file in `artifacts\release`. Each package is built in a clean staging folder. This is a framework-dependent x64 build requiring .NET Desktop Runtime 10 x64. Distribute the entire output directory, including the Russian resource assembly in `ru`, the bridge scripts in `wsl`, and the runtime configuration files.

## Releases

`Version` in `src/LlmServy/llm-servy.csproj` is the source for the product version and archive name. The Win32 manifest has a stable assembly identity independent of the product version. Use `0.1.1` for fixes, `0.2.0` for new features during initial development, and a suffix such as `0.2.0-beta.1` for preview builds.

1. Update `Version` and add release notes in `docs/releases/<version>.md`.
2. Run the C# and Python tests below, then `./publish.ps1 -ExpectedTag v<version>` and `./tests/Verify-Package.ps1` on Windows. A mismatched tag fails before publication.
3. Extract the ZIP into a new folder and check startup. Run the integration diagnostics on a configured Windows/WSL machine before release; GitHub CI does not provide that environment.
4. Commit the release changes. Create a GitHub Release targeting that commit with tag `v<version>`, attach the ZIP and its SHA-256 file, and use the prepared release notes. Mark preview builds as prereleases. Review the draft before publishing.

CI runs C# tests and formatting on Windows and supervisor tests on Ubuntu. It packages the Windows application only after both jobs pass and uploads it as a workflow artifact. Tag builds also verify the tag against the project version. CI does not publish a GitHub Release automatically. Download and extract the workflow artifact to obtain the application ZIP and checksum for a release built from the exact commit.

Do not commit build output or replace published release files with different builds under the same version. Increment the version instead. The ZIP excludes .NET, llama.cpp, model files, and user settings. Keep the configured SDK patched for future releases.

## Architecture

C# source directories are under `src/LlmServy`:

| Directory | Responsibility |
| --- | --- |
| `UI` | Forms, user actions, folder pickers, and localized settings metadata |
| `Configuration` | Settings, validation, atomic persistence, and explicit migration |
| `Models` | INI parsing and model catalog results with diagnostics |
| `Services` | Lifecycle controller, typed states and messages, logs, and harness URLs |
| `Runtime` | Windows/WSL integration, HTTP clients, readiness, and service ownership |
| `Processes` | Native Windows process creation, Job Objects, and standard I/O pipes |
| `Localization` | English and Russian resources and language selection |
| `Diagnostics` | Explicit process, integration, and UI diagnostic modes |

`LauncherController` depends on `ILauncherRuntime`; `Composition` wires the production dependencies. Start and stop transitions are serialized. Each session captures a settings snapshot. Cleanup attempts both services and retains failed cleanup handles for retry; it preserves startup and cleanup failures together.

Domain commands change state and normally return completion only. Queries return data without changing state or raising notifications. The llama.cpp `/props?autoload=true` request is exposed as a command because it loads a model. Native process creation returns an owned handle at the Windows API boundary. Atomic operations and framework contracts may combine state changes with a result when this is inherent to the operation.

Windows processes are created suspended, assigned to a kill-on-close Job Object, and then resumed. The scripts in `wsl` are copied during build and publish. The DSH Python supervisor owns a separate Linux process group, uses a port lock, and terminates its group on a stop request or controller disconnection. The Pi supervisor owns a private tmux socket and session. It emits JSON lifecycle events to `PiSession`; terminal clients attach independently. STOP or controller EOF ends the managed session. Pi conversation output stays in the tmux pane, not the application log. `RuntimeSnapshot.Harness` and `InterfaceReady` describe the selected harness; HTTP readiness remains specific to DSH. Pi readiness means a live process, not a verified provider or agent activity. Keep simple WSL switches such as `-d` unquoted when constructing the Windows command line.

Harness launch URLs are accepted only for the expected local host, port, and root path. Authentication tokens remain in memory, are redacted from logs, and are not sent in readiness probes.

## Tests

On Windows:

```powershell
dotnet test llm-servy.slnx
dotnet format whitespace llm-servy.slnx --verify-no-changes
```

The C# suite covers settings, migration, INI catalogs, localization, HTTP responses, lifecycle transitions, cancellation, direct form disposal, and cleanup failures. It includes a native child-process timeout test.

In WSL, from the repository root:

```bash
sudo apt install tmux
python3 -m unittest discover -s wsl/tests -v
```

Supervisor tests exercise process-group cleanup, controller disconnection, duplicate launches, occupied ports, child failures, isolated Pi ownership, argument preservation, and terminal detach/reattach to the same process.

Use `Operation_Behavior_Context` test names in C# and `test_operation_behavior_context` in Python. Bug fixes start with a failing regression test. Asynchronous checks wait for an observable condition with a timeout.

### Integration diagnostics

Run these after publishing:

```powershell
& .\artifacts\publish\llm-servy.exe --self-test
& .\artifacts\publish\llm-servy.exe --runtime-integration-test
& .\artifacts\publish\llm-servy.exe --dsh-integration-test
& .\artifacts\publish\llm-servy.exe --pi-integration-test
```

| Mode | Scope | Report |
| --- | --- | --- |
| `--self-test` | Windows argument escaping, pipes, process-tree cleanup, and Ubuntu WSL launch | `launcher-self-test.txt` |
| `--runtime-integration-test` | Production controller with simulated llama.cpp HTTP and real authenticated harness startup, readiness, shutdown, restart, and direct disposal | `launcher-runtime-test.txt` |
| `--pi-integration-test` | Production controller with simulated llama.cpp and a CLI fixture, real Windows → WSL → tmux, terminal endpoint, refresh, shutdown, restart and disposal | `launcher-pi-test.txt` |
| `--dsh-integration-test` | Direct bridge and authenticated harness startup/shutdown | `launcher-dsh-test.txt` |

Run the two DSH integration modes separately: both use port **13083**. They read the normal application settings and require a working WSL distribution and the configured harness package. The runtime test also needs a readable model INI; it rejects an occupied diagnostic port. The Pi mode uses tmux with a temporary CLI fixture and does not require Pi credentials. It validates orchestration, not the actual Pi provider or Windows Terminal UI. No integration mode loads a model into VRAM.

Reports default to `%LOCALAPPDATA%\llm-servy`. Pass `--diagnostics-dir <absolute-path>` to choose another report directory. This does not change the settings read by the harness integration modes.

### UI snapshots

These modes render a window without starting services or saving the language override:

```powershell
& .\artifacts\publish\llm-servy.exe --ui-smoke --language en --diagnostics-dir C:\temp\servy-main
& .\artifacts\publish\llm-servy.exe --settings-smoke --language ru --diagnostics-dir C:\temp\servy-settings
```

Add `--harness pi` to preview the Pi interface. Each writes `launcher-ui.png` to the selected directory. Normal startup does not run diagnostic fixtures.

## Code conventions

Private instance fields use camelCase. Async methods use the `Async` suffix; event handlers may use `async void`. Pass cancellation tokens through supported operations. Mandatory process cleanup uses a bounded shutdown wait independent of startup cancellation.

Nullable analysis is enabled and nullable warnings fail the build. CA2016 checks cancellation forwarding. Document important ownership and failure contracts with XML comments. Expected pipe closure or an absent cache entry is handled explicitly; unexpected failures must remain observable.

UI resources are released through form disposal, including diagnostic runs that never show the window. Runtime disposal waits for the WSL supervisor before releasing its Windows handle and attempts all remaining releases even if one fails.

## Localization

`Localization/Strings.resx` contains English resources; `Strings.ru.resx` contains Russian translations. Settings store stable language codes: `system`, `en`, and `ru`. `Localizer` resolves the culture and formats typed `AppMessage` values. Settings property names, descriptions, and categories come from `SettingsView`.

Add new user-facing text to both resource files with matching keys and format arguments. To add another language, provide `Strings.<culture>.resx`, update culture resolution, settings validation, and the language selector, and extend localization tests. Service output and historical log entries are not translated.

Keep [README.md](../README.md) and [README.ru.md](../README.ru.md) aligned when changing documented behavior. Developer documentation is maintained in English.

## Settings storage and import

Settings are stored in `%LOCALAPPDATA%\llm-servy\settings.json`. Saving uses a temporary file and atomic replacement. Publication and temporary-file cleanup failures are preserved together. The previous file becomes `settings.json.bak`. Logs rotate from `launcher.log` to `launcher.log.1` after exceeding 10 MiB. Keep user configuration and tokens out of the repository.

If no settings file exists, startup can import `%LOCALAPPDATA%\DshLauncher\settings.json`. The source is preserved and existing target settings are not overwritten. Missing `Language` uses `system`; missing `HarnessKind` uses `dsh`, preserving existing installations. Pi has separate `PiDirectory`, `PiExecutable`, `PiProvider`, and `OpenPiTerminal` settings. Both harnesses share the selected model and WSL distribution.

For a manual import, before creating application settings:

```powershell
& .\artifacts\publish\llm-servy.exe --import-settings C:\path\to\launcher-settings.json
```

A legacy `SelectedPreset` becomes an absolute `PresetPath` relative to the imported settings file. `AppPaths` only resolves paths; migration is an explicit startup operation.
