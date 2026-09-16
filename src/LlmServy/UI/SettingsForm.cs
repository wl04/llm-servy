using LlmServy.Configuration;
using LlmServy.Localization;
using LlmServy.Models;
using LlmServy.Services;

namespace LlmServy.UI;

public sealed class SettingsForm : Form
{
    public AppSettings EditedSettings
    {
        get;
    }
    private readonly Localizer localizer = new();

    public SettingsForm(AppSettings settings, string settingsFile, string? logsDirectory = null)
    {
        SuspendLayout();
        EditedSettings = settings with
        {
        };
        EditedSettings.MigratePaths();
        localizer.SetLanguage(EditedSettings.Language);
        Icon = AppIcon.Instance;
        Font = new("Segoe UI", 10);
        Size = new(1000, 730);
        MinimumSize = new(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 0, 100, 56, 80, 44 })
            layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles[0].SizeType = SizeType.Percent;
        layout.RowStyles[0].Height = 100;
        Controls.Add(layout);
        var view = new SettingsView(EditedSettings, localizer);
        var grid = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = false, PropertySort = PropertySort.CategorizedAlphabetical, SelectedObject = view };
        layout.Controls.Add(grid, 0, 0);
        var help = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle
        };
        void UpdateHelp() => help.Text = grid.SelectedGridItem?.PropertyDescriptor?.Description ?? "";
        grid.SelectedGridItemChanged += (_, _) => UpdateHelp();
        Shown += (_, _) => UpdateHelp();
        layout.Controls.Add(help, 0, 1);
        var fileLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        var logLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        var openSettings = new Button { Dock = DockStyle.Fill };
        var openLog = new Button { Dock = DockStyle.Fill };
        var openFolder = new Button { Dock = DockStyle.Fill };
        var logDirectory = logsDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsFile)) ?? throw new ArgumentException("Settings path has no directory.", nameof(settingsFile)), "logs");
        var logFile = Path.Combine(logDirectory, "launcher.log");
        AddFileRow(layout, 2, fileLabel, openSettings);
        AddFileRow(layout, 3, logLabel, openLog, openFolder);
        void Handle(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error) { MessageBox.Show(this, localizer.Error(error), localizer.Get("Settings"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        openSettings.Click += (_, _) => Handle(() => ShellActions.OpenFile(settingsFile, "SettingsMissing"));
        openLog.Click += (_, _) => Handle(() => ShellActions.OpenFile(logFile, "LogMissing"));
        openFolder.Click += (_, _) => Handle(() => { Directory.CreateDirectory(logDirectory); ShellActions.Open(logDirectory); });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Size = new(130, 34) };
        var cancel = new Button { Size = new(110, 34), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Handle(() =>
        {
            if (!Directory.Exists(EditedSettings.ModelsDirectory))
                throw new AppException(new("InvalidModelsDirectory"));
            var catalog = Preset.Scan(EditedSettings.ModelsDirectory);
            if (catalog.Models.Count == 0)
                throw new AppException(new("NoModels"));
            var selected = catalog.Models.FirstOrDefault(m => m.IniPath.Equals(EditedSettings.PresetPath, StringComparison.OrdinalIgnoreCase) && m.ModelId == EditedSettings.SelectedModelId) ?? catalog.Models[0];
            EditedSettings.PresetPath = selected.IniPath;
            EditedSettings.SelectedModelId = selected.ModelId;
            if (!EditedSettings.LlamaDirectory.Equals(settings.LlamaDirectory, StringComparison.OrdinalIgnoreCase))
                EditedSettings.LlamaExecutable = "";
            SettingsValidator.Validate(EditedSettings);
            DialogResult = DialogResult.OK;
        });
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 4);
        void ApplyLanguage()
        {
            localizer.SetLanguage(EditedSettings.Language);
            Text = localizer.Get("SettingsTitle");
            help.AccessibleName = localizer.Get("Help");
            fileLabel.Text = localizer.Get("SettingsPath", settingsFile);
            logLabel.Text = localizer.Get("LogPath", logFile);
            openSettings.Text = openLog.Text = localizer.Get("OpenEditor");
            openFolder.Text = localizer.Get("OpenFolder");
            save.Text = localizer.Get("Save");
            cancel.Text = localizer.Get("Cancel");
            grid.SelectedObject = null;
            grid.SelectedObject = view;
            UpdateHelp();
        }
        view.LanguageChanged += () => BeginInvoke(ApplyLanguage);
        ApplyLanguage();
        AcceptButton = save;
        CancelButton = cancel;
        AutoScaleDimensions = new(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true);
    }

    private static void AddFileRow(TableLayoutPanel parent, int row, Label label, params Button[] buttons)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new(SizeType.Absolute, 170));
        panel.RowStyles.Add(new(SizeType.Percent, 100));
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = buttons.Length };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int index = 0; index < buttons.Length; index++)
        {
            actions.RowStyles.Add(new(SizeType.Percent, 100f / buttons.Length));
            actions.Controls.Add(buttons[index], 0, index);
        }
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        parent.Controls.Add(panel, 0, row);
    }
}
