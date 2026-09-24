<p align="center"><img src="assets/llm-servy-logo.png" alt="LLM Servy logo" width="128"></p>

# LLM Servy

English · [Русский](README.ru.md)

A Windows tray application for running **llama.cpp on Windows** and **DeepSeek Harness or Pi in WSL**.

## Features

- Model selection from INI files in a configurable folder.
- Harness selection with a separate working folder for each harness.
- Pi in Windows Terminal, with a tmux session that survives closing the terminal.
- Background service startup with logs in the application window.
- Connection to existing servers; only processes started by LLM Servy are stopped by it.
- Tray controls and a Russian or English interface.

## Requirements

- Windows x64 with [.NET Desktop Runtime 10 x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Under **.NET Desktop Runtime**, choose **Windows → x64**. The runtime is not bundled; the SDK is only needed to build from source.
- A Windows [llama.cpp build](https://github.com/ggml-org/llama.cpp/releases) matching your hardware (CPU, CUDA, or Vulkan).
- Models and INI presets compatible with your installed llama.cpp version and hardware.
- A [WSL distribution](https://learn.microsoft.com/en-us/windows/wsl/install) with Bash, Python 3, and Node.js with npm/npx, compatible with your chosen harness.
- For Pi: Pi installed in WSL, `tmux` (`sudo apt install tmux` on Ubuntu), and [Windows Terminal](https://learn.microsoft.com/en-us/windows/terminal/install) on Windows. Pi and tmux must be available in the WSL login shell.

To install WSL with Ubuntu, run `wsl --install -d Ubuntu` in **PowerShell as Administrator**, restart if prompted, then open Ubuntu and complete the Linux user setup.

LLM Servy uses the configured DeepSeek Harness version in WSL. If it is not installed or cached, npx downloads it on first start; this requires network access. The WSL login shell must be able to find Node.js and Python.

## Quick start

1. Download the Windows x64 application ZIP from [Releases](https://github.com/wl04/llm-servy/releases), extract the entire archive to a folder, and run `llm-servy.exe`. Choose the application archive, not **Source code**; no installer is required.
2. Open **Settings** and choose the models/INI folder, the llama.cpp folder, your WSL distribution, and an existing working folder for your chosen harness. For DeepSeek Harness, set the exact package version. For Pi, set the executable and the name of an existing Pi model provider.
3. Select **DeepSeek Harness** or **Pi** and a model, click **Start**, then **Open DeepSeek Harness** or **Open Pi**. The working folder can also be selected on the main screen before starting.
4. In your harness, configure the provider with the llama.cpp API address described [below](#windows-and-wsl-networking) and the model ID from the INI section, then select that model in DeepSeek Harness. Pi receives the provider name and selected model ID automatically. LLM Servy does not edit the harness provider configuration.

The default ports are **1234** for llama.cpp and **3080** for the DeepSeek Harness web interface. Closing the window hides it in the tray; **Exit** stops the services launched by the application and closes it.

## Pi sessions

**Open Pi** attaches Windows Terminal to the running CLI in WSL. Closing the terminal leaves Pi running; opening it again attaches to the same process. **Stop** or tray **Exit** ends that session and the services started by the application. Sessions started independently are left alone.

Pi status describes the process lifecycle: starting, running, stopped, or failed. It does not indicate whether the agent is thinking, using tools, or waiting for input. Conversation output stays in the terminal; the launcher logs lifecycle events. If Pi exits, click **Stop** before starting a new session. Only one harness session is managed at a time.

Pi is not installed automatically. Existing Pi provider settings and credentials remain under Pi's control. Closing the terminal preserves the live session, but stopping the application, WSL, or Windows does not; use Pi's own saved sessions to resume work after a restart.

## Windows and WSL networking

The launcher and llama.cpp run on Windows; the selected harness runs in WSL. Opening the harness web interface from a Windows browser does not confirm that the harness can reach the model API.

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

## Updating

Exit LLM Servy from the tray, extract the new release into a separate folder, and launch it from there. Settings remain in `%LOCALAPPDATA%\llm-servy` and are reused automatically.

## Build from source

Use a Windows terminal in the repository root. The SDK version is specified in [global.json](global.json).

```powershell
dotnet build llm-servy.slnx
.\publish.ps1
```

The application is written in C# with WinForms on .NET 10. See the [development guide](docs/development.md) for debugging, tests, architecture, localization, and configuration import.

## License

[MIT](LICENSE) © 2026 LLM Servy contributors.
