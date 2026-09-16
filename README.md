<p align="center"><img src="assets/llm-servy-logo.png" alt="LLM Servy logo" width="128"></p>

# LLM Servy

English · [Русский](README.ru.md)

A Windows tray application for running **llama.cpp on Windows** and **DeepSeek Harness in WSL**. Start and stop services, select models from INI files, and open the harness web interface from one window.

## Features

- Model selection from INI files in a configurable folder.
- Background service startup with logs in the application window.
- Connection to existing servers; only processes started by LLM Servy are stopped by it.
- Tray controls and a Russian or English interface.

## Requirements

- Windows x64 with .NET Desktop Runtime 10 x64.
- A Windows llama.cpp installation supporting `--models-preset`, `/v1/models`, and `/props?autoload=true&model=…`.
- Models and INI presets compatible with your installed llama.cpp version and hardware.
- A WSL distribution with Bash, Python 3, and Node.js with npm/npx, compatible with your chosen DeepSeek Harness version.

DeepSeek Harness must be available in WSL. If the configured package version is not installed or cached, LLM Servy invokes npx to install it; this requires network access. The WSL login shell must be able to find Node.js and Python.

## Quick start

1. [Build the application](#build-from-source) and open `artifacts\publish\llm-servy.exe`. Keep the entire published folder together, including `ru` and `wsl`.
2. Open **Settings** and choose the models/INI folder, the llama.cpp folder, your WSL distribution, and an existing harness working folder in WSL. Set the exact DeepSeek Harness package version to use.
3. Configure the provider in DeepSeek Harness to use your llama.cpp API address reachable from WSL and the model ID from the INI section. LLM Servy does not edit the harness provider configuration.
4. Select a model, click **Start**, then **Open DeepSeek Harness** when it becomes available. Select the matching model in the harness.

The default ports are **1234** for llama.cpp and **3080** for the harness web interface. Services start when you click **Start**. Closing the window hides it in the tray; **Exit** stops the services launched by the application and closes it.

## Configuration

- Only INI files directly in the selected models folder are scanned. Model paths inside an INI are resolved relative to that INI's folder.
- The application, INI files, and model files can be stored in different locations. The harness working folder is configured separately.
- An empty llama.cpp folder enables automatic executable discovery. An explicit folder is searched for `llama-server.exe`, then `llama.exe`.
- Service setting changes apply on the next start. **Settings → Interface → Language** changes the UI language after saving, without restarting services. System language uses Russian for Russian Windows and English otherwise.

Settings: `%LOCALAPPDATA%\llm-servy\settings.json`. Logs: `%LOCALAPPDATA%\llm-servy\logs\launcher.log`. Both can be opened from **Settings**. Harness sign-in tokens are kept in memory and redacted from logs; service output retains its original language.

## Build from source

Use a Windows terminal in the repository root. The SDK version is specified in [global.json](global.json).

```powershell
dotnet build llm-servy.slnx
.\publish.ps1
```

The application is written in C# with WinForms on .NET 10. See the [development guide](docs/development.md) for debugging, tests, architecture, localization, and configuration import.

## License

[MIT](LICENSE) © 2026 LLM Servy contributors.
