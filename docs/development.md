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

Publishing creates `artifacts\publish\llm-servy.exe`. This is a framework-dependent x64 build requiring .NET Desktop Runtime 10 x64. Distribute the entire output directory, including the Russian resource assembly in `ru`, the bridge scripts in `wsl`, and the runtime configuration files.

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

Windows processes are created suspended, assigned to a kill-on-close Job Object, and then resumed. The scripts in `wsl` are copied during build and publish. The Python supervisor owns a separate Linux process group, uses a port lock, and terminates its group on a stop request or controller disconnection. Keep simple WSL switches such as `-d` unquoted when constructing the Windows command line.

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
python3 -m unittest discover -s wsl/tests -v
```

Supervisor tests exercise process-group cleanup, controller disconnection, duplicate launches, occupied ports, and child failures.

Use `Operation_Behavior_Context` test names in C# and `test_operation_behavior_context` in Python. Bug fixes start with a failing regression test. Asynchronous checks wait for an observable condition with a timeout.

### Integration diagnostics

Run these after publishing:

```powershell
& .\artifacts\publish\llm-servy.exe --self-test
& .\artifacts\publish\llm-servy.exe --runtime-integration-test
& .\artifacts\publish\llm-servy.exe --dsh-integration-test
```

| Mode | Scope | Report |
| --- | --- | --- |
| `--self-test` | Windows argument escaping, pipes, process-tree cleanup, and Ubuntu WSL launch | `launcher-self-test.txt` |
| `--runtime-integration-test` | Production controller with simulated llama.cpp HTTP and real authenticated harness startup, readiness, shutdown, restart, and direct disposal | `launcher-runtime-test.txt` |
| `--dsh-integration-test` | Direct bridge and authenticated harness startup/shutdown | `launcher-dsh-test.txt` |

Run the two harness integration modes separately: both use port **13083**. They read the normal application settings and require a working WSL distribution and the configured harness package. The runtime test also needs a readable model INI; it rejects an occupied diagnostic port. Neither integration mode loads a model into VRAM.

Reports default to `%LOCALAPPDATA%\llm-servy`. Pass `--diagnostics-dir <absolute-path>` to choose another report directory. This does not change the settings read by the harness integration modes.

### UI snapshots

These modes render a window without starting services or saving the language override:

```powershell
& .\artifacts\publish\llm-servy.exe --ui-smoke --language en --diagnostics-dir C:\temp\servy-main
& .\artifacts\publish\llm-servy.exe --settings-smoke --language ru --diagnostics-dir C:\temp\servy-settings
```

Each writes `launcher-ui.png` to the selected directory. Normal startup does not run diagnostic fixtures.

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

If no settings file exists, startup can import `%LOCALAPPDATA%\DshLauncher\settings.json`. The source is preserved and existing target settings are not overwritten. Missing `Language` uses `system`.

For a manual import, before creating application settings:

```powershell
& .\artifacts\publish\llm-servy.exe --import-settings C:\path\to\launcher-settings.json
```

A legacy `SelectedPreset` becomes an absolute `PresetPath` relative to the imported settings file. `AppPaths` only resolves paths; migration is an explicit startup operation.
