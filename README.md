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

## Windows and WSL networking

The launcher and llama.cpp run on Windows; DeepSeek Harness runs in WSL. Opening the harness web interface from a Windows browser does not confirm that the harness can reach the model API.

Set the harness provider's API URL according to your WSL networking mode (replace port `1234` if changed):

- **WSL2 NAT:** use `http://<Windows-host-IP>:1234/v1`. Find the host IP from WSL with `ip route show default` (the address after `via`). llama.cpp must listen on an interface reachable from WSL; the default `0.0.0.0` listens on all IPv4 interfaces, with access subject to Windows Firewall.
- **WSL2 mirrored:** use `http://127.0.0.1:1234/v1`; llama.cpp can listen on `127.0.0.1` to restrict access to loopback.

`0.0.0.0` is a server bind address, not the API destination to enter in the harness. See [Microsoft's WSL networking guide](https://learn.microsoft.com/en-us/windows/wsl/networking).

## Configuration

LLM Servy passes INI files directly to llama.cpp through `--models-preset`, using its [native model preset format](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#model-presets). This format is supported on Windows and Linux, and the same presets can be used without the launcher.

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
