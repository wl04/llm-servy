using System.Collections.Concurrent;
using LlmServy.Localization;
using LlmServy.Runtime;
using LlmServy.Configuration;
using LlmServy.Models;
using LlmServy.Services;

namespace LlmServy.UI;

public sealed class LauncherForm : Form
{
    private readonly CancellationTokenSource formLifetime = new();
    private readonly Localizer localizer = new();
    private readonly List<(ToolStripItem Item, string Key)> trayItems = new();
    private readonly AppPaths paths;
    private readonly SettingsStore store;
    private readonly LauncherLog log;
    private readonly LauncherController controller;
    private readonly ConcurrentQueue<string> pendingLines = new();
    private readonly ComboBox models = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label llamaState = new() { AutoSize = true }, dshState = new() { AutoSize = true };
    private readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray };
    private readonly Button start = new(), stop = new(), browser = new(), options = new();
    private readonly TextBox logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both, Font = new("Consolas", 9) };
    private readonly NotifyIcon tray = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private AppSettings settings;
    private bool exiting, polling;
    private DateTime nextPoll;

    public LauncherForm(AppPaths paths, AppSettings? initialSettings = null, LauncherController? launcherController = null)
    {
        SuspendLayout();
        this.paths = paths;
        store = new(paths);
        log = new(paths, localizer);
        controller = launcherController ?? Composition.CreateController(paths, log);
        settings = new();
        try
        {
            settings = initialSettings is null ? store.Load() : initialSettings with
            {
            };
            settings.MigratePaths();
        }
        catch (Exception error) { MessageBox.Show(localizer.Get("SettingsReadFailed") + "\n" + localizer.Error(error)); }
        localizer.SetLanguage(settings.Language);
        Text = "LLM Servy";
        Font = new("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 250);
        Size = new(1060, 760);
        MinimumSize = new(930, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Instance;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 40));
        foreach (var height in new[] { 35, 35, 52, 64 })
            layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(models, 0, 0);
        layout.Controls.Add(llamaState, 0, 1);
        layout.Controls.Add(dshState, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Setup(start, localizer.Get("Start"), async (_, _) => await StartAsync());
        Setup(stop, localizer.Get("Stop"), async (_, _) => await controller.StopAsync());
        Setup(browser, localizer.Get("OpenHarness"), (_, _) => OpenBrowser());
        browser.Width = 235;
        Setup(options, localizer.Get("Settings"), (_, _) => EditSettings());
        actions.Controls.AddRange([start, stop, browser, options]);
        layout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(note, 0, 4);
        layout.Controls.Add(logBox, 0, 5);

        var menu = new ContextMenuStrip();
        void AddTray(string key, EventHandler action)
        {
            var item = menu.Items.Add(localizer.Get(key), null, action);
            trayItems.Add((item, key));
        }
        AddTray("Show", (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        AddTray("Start", async (_, _) => await StartAsync());
        AddTray("Stop", async (_, _) => await controller.StopAsync());
        AddTray("OpenHarness", (_, _) => OpenBrowser());
        AddTray("Exit", async (_, _) => await QuitAsync());
        menu.Opening += (_, _) =>
        {
            trayItems[1].Item.Enabled = start.Enabled;
            trayItems[2].Item.Enabled = stop.Enabled;
            trayItems[3].Item.Enabled = browser.Enabled;
        };
        tray.Icon = AppIcon.Instance;
        tray.Text = "LLM Servy";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing && !exiting) { e.Cancel = true; Hide(); } };

        log.LineWritten += line => pendingLines.Enqueue(line);
        controller.StatusChanged += status =>
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
                BeginInvoke(() => Render(status));
            else
                Render(status);
        };
        timer.Tick += async (_, _) =>
        {
            for (int i = 0; i < 150 && pendingLines.TryDequeue(out var line); i++)
                logBox.AppendText(line + Environment.NewLine);
            if (logBox.TextLength > 160000)
                logBox.Text = logBox.Text[^100000..];
            if (!polling && controller.Status.IsActive && DateTime.UtcNow >= nextPoll)
            {
                polling = true;
                nextPoll = DateTime.UtcNow.AddSeconds(4);
                try
                {
                    await controller.RefreshAsync(formLifetime.Token);
                }
                catch (OperationCanceledException) when (formLifetime.IsCancellationRequested) { /* Window is closing. */ }
                catch (Exception error) { log.WriteError("launcher", error); }
                finally { polling = false; }
            }
        };
        timer.Start();
        LoadPresets();
        Render(controller.Status);
        log.WriteMessage("launcher", new("SettingsLocation", paths.SettingsFile));
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }

    private bool resourcesDisposed;
    protected override void Dispose(bool disposing)
    {
        if (!disposing || resourcesDisposed)
        {
            base.Dispose(disposing);
            return;
        }
        resourcesDisposed = true;
        ResourceCleanup.Run(formLifetime.Cancel, timer.Dispose, () => tray.Visible = false,
            () => tray.ContextMenuStrip?.Dispose(), tray.Dispose, controller.Dispose, formLifetime.Dispose,
            () => base.Dispose(disposing));
    }

    private string FormatService(ServiceStatus status) => localizer.Get("State" + status.State) + (status.AlreadyRunning ? " (" + localizer.Get("ExistingService") + ")" : "");

    private void ApplyLanguage()
    {
        localizer.SetLanguage(settings.Language);
        start.Text = localizer.Get("Start");
        stop.Text = localizer.Get("Stop");
        browser.Text = localizer.Get("OpenHarness");
        options.Text = localizer.Get("Settings");
        foreach (var entry in trayItems)
            entry.Item.Text = localizer.Get(entry.Key);
        Render(controller.Status);
    }

    private static void Setup(Button button, string text, EventHandler action)
    {
        button.Text = text;
        button.Size = new(145, 38);
        button.FlatStyle = FlatStyle.Flat;
        button.Click += action;
    }

    private void LoadPresets()
    {
        models.Items.Clear();
        try
        {
            if (string.IsNullOrEmpty(settings.ModelsDirectory))
                return;
            var catalog = Preset.Scan(settings.ModelsDirectory);
            foreach (var diagnostic in catalog.Diagnostics)
                log.WriteMessage("INI", diagnostic);
            foreach (var model in catalog.Models)
                models.Items.Add(model);
            for (int i = 0; i < models.Items.Count; i++)
                if (models.Items[i] is Preset item && item.ModelId == settings.SelectedModelId && item.IniPath.Equals(settings.PresetPath, StringComparison.OrdinalIgnoreCase))
                    models.SelectedIndex = i;
            if (models.SelectedIndex < 0 && models.Items.Count > 0)
                models.SelectedIndex = 0;
        }
        catch (Exception error) { log.WriteError("settings", error); note.Text = localizer.Error(error); }
    }

    private void Render(LauncherStatus status)
    {
        llamaState.Text = "llama.cpp  ·  " + FormatService(status.Llama);
        dshState.Text = "DeepSeek Harness  ·  " + FormatService(status.Dsh);
        start.Enabled = controller.CanStart && models.Items.Count > 0;
        stop.Enabled = controller.CanStop;
        browser.Enabled = status.BrowserReady;
        models.Enabled = !status.IsBusy && !status.IsActive;
        note.Text = models.Items.Count == 0 ? localizer.Get("ChooseModels") : status.Failure is null ? localizer.Format(status.Message) : localizer.Format(status.Message) + "\n" + localizer.Error(status.Failure);
    }

    private void EditSettings()
    {
        using var dialog = new SettingsForm(settings, paths.SettingsFile, paths.LogsDirectory);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            store.Save(dialog.EditedSettings);
            settings = dialog.EditedSettings;
            ApplyLanguage();
            if (!controller.Status.IsActive && !controller.Status.IsBusy)
            {
                LoadPresets();
                Render(controller.Status);
            }
            else
                note.Text = localizer.Get("SettingsSaved");
            log.WriteMessage("settings", new("SettingsSaved"));
        }
        catch (Exception error) { MessageBox.Show(this, localizer.Error(error), localizer.Get("SettingsSaveFailed")); }
    }

    private async Task StartAsync()
    {
        try
        {
            if (!controller.CanStart || models.SelectedItem is not Preset selected)
                return;
            if (!string.Equals(selected.WorkingDirectory, settings.ModelsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                LoadPresets();
                if (models.SelectedItem is not Preset refreshed)
                    throw new AppException(new("NoModels"));
                selected = refreshed;
            }
            settings.PresetPath = selected.IniPath;
            settings.SelectedModelId = selected.ModelId;
            settings.Validate();
            store.Save(settings);
            var snapshot = settings with
            {
            };
            await controller.StartAsync(snapshot, selected);
            if (controller.Status.IsActive && snapshot.OpenBrowser)
                OpenBrowser();
            if (!controller.Status.IsActive)
            {
                LoadPresets();
                Render(controller.Status);
            }
        }
        catch (Exception error) { log.WriteError("launcher", error); note.Text = localizer.Error(error); }
    }

    private void OpenBrowser()
    {
        var url = controller.BrowserUrl;
        if (url is null)
        {
            log.WriteMessage("launcher", new("BrowserNotReady"));
            return;
        }
        ShellOpen(url);
    }

    private void ShellOpen(string target)
    {
        try
        {
            ShellActions.Open(target);
        }
        catch (Exception error) { log.WriteError("launcher", error); }
    }

    private async Task QuitAsync()
    {
        if (exiting)
            return;
        exiting = true;
        await controller.StopAsync();
        if (controller.CanStop)
        {
            exiting = false;
            return;
        }
        Close();
    }
}
