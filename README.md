<p align="center"><img src="assets/llm-servy-logo.png" alt="LLM Servy logo" width="128"></p>

# LLM Servy

English · [Русский](README.ru.md)

A Windows tray application for running **llama.cpp on Windows** and **DeepSeek Harness or Pi in WSL**.

## Features

- Model selection from INI files in a configurable folder.
- Harness selection with a separate working folder for each harness.
- DeepSeek Harness in a browser; Pi in Windows Terminal with persistent tmux sessions.
- Service status, logs, and tray controls.
- Russian and English interface.

## Requirements

### Common

- Windows x64 with [.NET Desktop Runtime 10 x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Under **.NET Desktop Runtime**, choose **Windows → x64**. The runtime is not bundled; the SDK is only needed to build from source.
- A Windows [llama.cpp build](https://github.com/ggml-org/llama.cpp/releases) matching your hardware, plus compatible models and INI presets.
- A [WSL distribution](https://learn.microsoft.com/en-us/windows/wsl/install) with Bash, Python 3, and Node.js compatible with your harness. These commands must be available in the WSL login shell.

To install Ubuntu in WSL, run `wsl --install -d Ubuntu` in **PowerShell as Administrator**, restart if prompted, then open Ubuntu and complete the Linux user setup.

### Harness-specific

| Harness | Additional requirements |
| --- | --- |
| [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.md) | npm/npx in WSL. LLM Servy uses the configured package version; npx downloads it on first start if unavailable locally, requiring network access. |
| [Pi](https://pi.dev/docs/latest/quickstart) | Pi installed in WSL, tmux 3.2+ (`sudo apt install tmux` on Ubuntu), and [Windows Terminal](https://learn.microsoft.com/en-us/windows/terminal/install) on Windows. Pi and tmux must be available in the WSL login shell. LLM Servy does not install Pi. |

## Quick start

1. Download the Windows x64 application ZIP from [Releases](https://github.com/wl04/llm-servy/releases), extract the entire archive, and run `llm-servy.exe`. Choose the application archive, not **Source code**; no installer is required.
2. In **Settings**, choose the models/INI folder, the llama.cpp folder, your WSL distribution, and an existing working folder for the chosen harness.
3. Select the harness and model on the main screen. Complete the harness-specific setup below.

LLM Servy does not edit harness provider settings or credentials. Use the [API address appropriate for your WSL networking mode](#windows-and-wsl-networking). The model ID must match the **INI section name**, which may differ from the model filename.

- **DeepSeek Harness:** set its exact package version in Settings. Click **Start**, then **Open DeepSeek Harness**. In its web interface, configure the llama.cpp provider and model ID, then select that model.
- **Pi:** **before clicking Start**, configure the provider and selected model in Pi; see [Pi model configuration](https://pi.dev/docs/latest/models). In **Settings → Pi**, enter the executable and the exact provider name from that configuration. Click **Start**, then **Open Pi**. LLM Servy passes the provider name and selected model ID to Pi.

## Service controls

One harness is managed at a time. LLM Servy can reuse running llama.cpp and DeepSeek Harness servers; it stops only processes it starts. Independently started Pi sessions are not adopted.

| Action | Result |
| --- | --- |
| **Start** | Starts or connects to llama.cpp, loads the selected model, then starts or connects to the chosen harness. Pi always starts as a managed session. |
| **Open DeepSeek Harness** | Opens the web interface. |
| **Open Pi** | Attaches to the running Pi session, or starts a new one after Pi exits, without restarting llama.cpp or reloading the model. |
| Close the Pi terminal | Leaves Pi running; reopen it to reconnect. |
| Exit Pi with `/quit` or a keyboard shortcut | Ends Pi while llama.cpp keeps running. Terminal clients detach successfully; tab closure depends on Windows Terminal settings. |
| **Stop** | Stops the services started by LLM Servy. |
| Close the LLM Servy window | Hides the application in the tray. |
| Tray **Exit** | Stops its managed services and closes LLM Servy. |

Pi status tracks the process lifecycle, not whether the agent is thinking or waiting for input. A new Pi process does not automatically resume a previous conversation. Use Pi's saved sessions to continue after stopping Pi, WSL, or Windows.

## Windows and WSL networking

The launcher and llama.cpp run on Windows; the harness runs in WSL. Opening the DeepSeek Harness web interface from Windows does not confirm that it can reach the model API.

Default ports: **1234** for llama.cpp, **3080** for the DeepSeek Harness web interface. Set the harness provider's API URL according to the WSL networking mode, replacing `1234` if changed:

- **WSL2 NAT:** `http://<Windows-host-IP>:1234/v1`. Find the host IP in WSL with `ip route show default` (the address after `via`). llama.cpp must listen on an interface reachable from WSL. The default bind address `0.0.0.0` listens on all IPv4 interfaces; Windows Firewall controls access.
- **WSL2 mirrored:** `http://127.0.0.1:1234/v1`. llama.cpp can bind to `127.0.0.1` to restrict access to loopback.

`0.0.0.0` is a server bind address, not an API destination. See [Microsoft's WSL networking guide](https://learn.microsoft.com/en-us/windows/wsl/networking).

## Configuration

### Models and INI

LLM Servy passes INI files directly to llama.cpp through `--models-preset`, using its [native model preset format](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#model-presets). The format works on Windows and Linux; the presets can also be used without LLM Servy.

- Only INI files directly in the selected models folder are scanned. Relative model paths are resolved from the INI's folder.
- The application, INI files, and models can be stored separately. Each harness has its own working folder, also selectable on the main screen before starting.
- An explicit llama.cpp folder is searched for `llama-server.exe`, then `llama.exe`. With no folder or saved executable path, automatic discovery looks for `llama.exe` in PATH and Windows app links; select the folder explicitly for `llama-server.exe`.

### Settings and logs

Service setting changes apply after **Stop → Start**. Reopening an exited Pi retains the active session's settings. **Settings → Interface → Language** applies after saving without restarting services; system language selects Russian for Russian Windows and English otherwise.

Settings are stored in `%LOCALAPPDATA%\llm-servy\settings.json`; logs in `%LOCALAPPDATA%\llm-servy\logs\launcher.log`. Both can be opened from **Settings**. Launcher messages are localized; service output retains its original language. Pi conversation output stays in the terminal; its lifecycle events appear in the launcher log. DeepSeek Harness web sign-in tokens are kept in memory and redacted from logs.

## Updating

Exit LLM Servy from the tray, extract the new release into a separate folder, and launch it from there. Existing settings are reused automatically.

## Build from source

Use a Windows terminal in the repository root with the SDK specified in [global.json](global.json):

```powershell
dotnet build llm-servy.slnx
.\publish.ps1
```

The application uses C# and WinForms on .NET 10. See the [development guide](docs/development.md) for build output, debugging, tests, architecture, localization, and configuration import.

## License

[MIT](LICENSE) © 2026 LLM Servy contributors.
