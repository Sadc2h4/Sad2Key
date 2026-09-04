using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Gaming.Input;

namespace Sad2Key
{
    public partial class Form1 : Form
    {
        private const int MaxDevices = 16;
        private const int MaxButtons = 32;
        private const int JoyReturnAll = 0x000000FF;
        private const string Joystick1SectionName = "[Joystick 1]";
        private const string SettingsFileName = "settings.json";
        private const string JsonProfileFileName = "keymap.json";

        private Theme theme = Theme.Dark;

        // 画面部品
        private readonly TableLayoutPanel rootLayout;
        private readonly TileButton statusTile;
        private readonly TileButton controllerTile;
        private readonly TileButton profileTile;
        private readonly TileButton sidewaysTile;
        private readonly TileButton inputLogTile;
        private readonly TileButton testKeyTile;
        private readonly TileButton themeTile;
        private readonly RoundButton saveButton;
        private readonly RoundButton powerButton;
        private readonly RoundedPanel mappingPanel;
        private readonly RoundedPanel logPanel;
        private readonly Panel logHeaderPanel;
        private readonly Label logHeaderLabel;
        private readonly Label logChevronLabel;
        private readonly Label hintLabel;
        private readonly Panel bottomPanel;
        private readonly ThemedListView stateListView;
        private readonly TextBox logTextBox;
        private readonly TextBox focusSinkTextBox;
        private readonly ContextMenuStrip deviceMenu;
        private readonly ContextMenuStrip profileMenu;
        private readonly ToolTip toolTip = new();

        // 状態
        private readonly List<DeviceInfo> devices = [];
        private readonly List<ProfileInfo> profiles = [];
        private readonly List<SwitchHidController> switchHidControllers = [];
        private readonly Dictionary<string, KeyMapping> keyMappings = [];
        private readonly KeySender keySender;
        private readonly InputEngine inputEngine;
        private readonly List<string> preservedHeaderLines = [];       // cfgの[Joystick 1]より前の行（原文保持用）
        private readonly List<string> preservedJoystickLines = [];     // cfgの[Joystick 1]内の行（原文保持用）
        private readonly List<string> preservedTrailingLines = [];     // cfgの[Joystick 1]より後の行（原文保持用）
        private readonly string applicationDirectory;                  // exeのあるフォルダ（ポータブル設定の置き場所）
        private readonly string configPath;
        private readonly string settingsPath;
        private string profileDirectory;
        private bool joyConSidewaysStick = true;
        private bool isLogCollapsed;
        private DeviceInfo? selectedDevice;
        private ProfileInfo selectedProfile = ProfileInfo.JsonDefault;
        private CancellationTokenSource? pollingCancellationTokenSource;
        private Task? pollingTask;
        private DeviceInfo? activeDevice;
        private long lastStateViewUpdateTicks;
        private HashSet<string> lastDisplayedInputs = [];
        private volatile bool enableInputLog;

        //-------------------------------------------------------------------------------
        // 画面と入力監視処理を初期化する処理
        //-------------------------------------------------------------------------------
        public Form1()
        {
            InitializeComponent();
            applicationDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            configPath = Path.Combine(applicationDirectory, JsonProfileFileName);
            settingsPath = Path.Combine(applicationDirectory, SettingsFileName);
            var settings = LoadSettings();
            profileDirectory = settings?.ProfileDirectory is { } savedDirectory && Directory.Exists(savedDirectory)
                ? savedDirectory
                : FindProfileDirectory();
            joyConSidewaysStick = settings?.JoyConSidewaysStick ?? true;
            isLogCollapsed = settings?.LogCollapsed ?? false;
            theme = Theme.FromMode(Enum.TryParse<ThemeMode>(settings?.Theme, true, out var savedMode) ? savedMode : ThemeMode.Dark);
            keySender = new KeySender(AppendLog);
            inputEngine = new InputEngine(keySender, keyMappings, AppendLog);

            rootLayout = new TableLayoutPanel();
            statusTile = new TileButton();
            controllerTile = new TileButton();
            profileTile = new TileButton();
            sidewaysTile = new TileButton();
            inputLogTile = new TileButton();
            testKeyTile = new TileButton();
            themeTile = new TileButton();
            saveButton = new RoundButton();
            powerButton = new RoundButton();
            mappingPanel = new RoundedPanel();
            logPanel = new RoundedPanel();
            logHeaderPanel = new Panel();
            logHeaderLabel = new Label();
            logChevronLabel = new Label();
            hintLabel = new Label();
            bottomPanel = new Panel();
            stateListView = new ThemedListView();
            logTextBox = new TextBox();
            focusSinkTextBox = new TextBox();
            deviceMenu = ThemedMenu.Create(theme);
            profileMenu = ThemedMenu.Create(theme);

            LoadApplicationIcon();
            InitializeUserInterface();
            ApplyTheme();
            LoadOrCreateConfig();
            RefreshProfiles();
            RefreshDevices();
            UpdateStatusTile();

            FormClosing += Form1_FormClosing;
        }

        //-------------------------------------------------------------------------------
        // 論理ピクセルをDPIに合わせた実ピクセルへ変換する処理
        //-------------------------------------------------------------------------------
        private int S(int value)
        {
            return ThemedDrawing.Scale(this, value);
        }

        //-------------------------------------------------------------------------------
        // 埋め込みリソースからアプリアイコンを読み込む処理
        //-------------------------------------------------------------------------------
        private void LoadApplicationIcon()
        {
            try
            {
                using var stream = typeof(Form1).Assembly.GetManifestResourceStream("Sad2Key.exe_icon.ico");

                if (stream is not null)
                {
                    Icon = new Icon(stream);
                }
            }
            catch
            {
                // アイコンが無くても動作には影響しない
            }
        }

        //-------------------------------------------------------------------------------
        // 画面部品を配置する処理（参考UIのタイル構成）
        //-------------------------------------------------------------------------------
        private void InitializeUserInterface()
        {
            ClientSize = new Size(S(780), S(900));
            MinimumSize = new Size(S(700), S(760));
            Font = Theme.BodyFont;
            DoubleBuffered = true;

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 5;
            rootLayout.Padding = new Padding(S(14), S(14), S(14), S(8));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(72)));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(264)));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(170)));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(68)));
            Controls.Add(rootLayout);

            // 状態表示ピル
            statusTile.Interactive = false;
            statusTile.Glyph = Glyphs.Link;
            statusTile.Dock = DockStyle.Fill;
            statusTile.Margin = new Padding(S(6), 0, S(6), S(6));
            rootLayout.Controls.Add(statusTile, 0, 0);

            // タイル2列×3行
            var tilesGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
            };
            tilesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tilesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tilesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
            tilesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
            tilesGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.4f));
            rootLayout.Controls.Add(tilesGrid, 0, 1);

            ConfigureTile(controllerTile, Glyphs.Game, "Controller", true);
            controllerTile.Click += ControllerTile_Click;
            tilesGrid.Controls.Add(controllerTile, 0, 0);

            ConfigureTile(profileTile, Glyphs.Document, "Profile", true);
            profileTile.Click += ProfileTile_Click;
            tilesGrid.Controls.Add(profileTile, 1, 0);

            ConfigureTile(sidewaysTile, Glyphs.Rotate, "Joy-Con stick sideways", false);
            sidewaysTile.Click += SidewaysTile_Click;
            tilesGrid.Controls.Add(sidewaysTile, 0, 1);

            ConfigureTile(inputLogTile, Glyphs.Log, "Input event log", false);
            inputLogTile.Click += InputLogTile_Click;
            tilesGrid.Controls.Add(inputLogTile, 1, 1);

            ConfigureTile(testKeyTile, Glyphs.Keyboard, "Test key", false);
            testKeyTile.Subtitle = "Send A to the focused window";
            testKeyTile.Click += TestKeyTile_Click;
            tilesGrid.Controls.Add(testKeyTile, 0, 2);

            ConfigureTile(themeTile, Glyphs.Moon, "Theme", false);
            themeTile.Click += ThemeTile_Click;
            tilesGrid.Controls.Add(themeTile, 1, 2);

            // マッピング一覧
            mappingPanel.Dock = DockStyle.Fill;
            mappingPanel.Margin = new Padding(S(6));
            mappingPanel.Padding = new Padding(S(14), S(12), S(14), S(12));
            rootLayout.Controls.Add(mappingPanel, 0, 2);

            stateListView.Dock = DockStyle.Fill;
            stateListView.Columns.Add("Input", S(170));
            stateListView.Columns.Add("Key", S(300));
            stateListView.DoubleClick += StateListView_DoubleClick;
            stateListView.ClientSizeChanged += (_, _) => FitListColumns();
            mappingPanel.Controls.Add(stateListView);

            // ログ（折りたたみ可）
            logPanel.Dock = DockStyle.Fill;
            logPanel.Margin = new Padding(S(6));
            logPanel.Padding = new Padding(S(14), S(6), S(14), S(10));
            rootLayout.Controls.Add(logPanel, 0, 3);

            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.BorderStyle = BorderStyle.None;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.Font = Theme.SmallFont;
            logPanel.Controls.Add(logTextBox);

            logHeaderPanel.Dock = DockStyle.Top;
            logHeaderPanel.Height = S(30);
            logHeaderPanel.Cursor = Cursors.Hand;
            logHeaderPanel.Click += LogHeader_Click;
            logPanel.Controls.Add(logHeaderPanel);

            logHeaderLabel.Text = "Log";
            logHeaderLabel.Font = Theme.TitleFont;
            logHeaderLabel.Dock = DockStyle.Left;
            logHeaderLabel.Width = S(120);
            logHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
            logHeaderLabel.Cursor = Cursors.Hand;
            logHeaderLabel.Click += LogHeader_Click;
            logHeaderPanel.Controls.Add(logHeaderLabel);

            logChevronLabel.Font = Theme.GlyphFont;
            logChevronLabel.Dock = DockStyle.Right;
            logChevronLabel.Width = S(30);
            logChevronLabel.TextAlign = ContentAlignment.MiddleRight;
            logChevronLabel.Cursor = Cursors.Hand;
            logChevronLabel.Click += LogHeader_Click;
            logHeaderPanel.Controls.Add(logChevronLabel);

            // 下部の丸ボタン
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.Margin = new Padding(S(6), S(4), S(6), 0);
            rootLayout.Controls.Add(bottomPanel, 0, 4);

            hintLabel.Text = "Double-click a row to edit its key assignment.";
            hintLabel.Font = Theme.SmallFont;
            hintLabel.Dock = DockStyle.Left;
            hintLabel.Width = S(360);
            hintLabel.TextAlign = ContentAlignment.MiddleLeft;
            bottomPanel.Controls.Add(hintLabel);

            var roundButtonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                Width = S(150),
                WrapContents = false,
                Padding = new Padding(0),
            };
            bottomPanel.Controls.Add(roundButtonPanel);

            powerButton.Glyph = Glyphs.Power;
            powerButton.Size = new Size(S(60), S(60));
            powerButton.Margin = new Padding(S(6), 0, 0, 0);
            powerButton.Click += PowerButton_Click;
            toolTip.SetToolTip(powerButton, "Start / Stop mapping");
            roundButtonPanel.Controls.Add(powerButton);

            saveButton.Glyph = Glyphs.Save;
            saveButton.Size = new Size(S(60), S(60));
            saveButton.Margin = new Padding(S(6), 0, S(6), 0);
            saveButton.Click += SaveButton_Click;
            toolTip.SetToolTip(saveButton, "Save profile");
            roundButtonPanel.Controls.Add(saveButton);

            focusSinkTextBox.BorderStyle = BorderStyle.None;
            focusSinkTextBox.Location = new Point(-200, -200);
            focusSinkTextBox.Size = new Size(1, 1);
            focusSinkTextBox.TabStop = false;
            Controls.Add(focusSinkTextBox);

            ApplyLogCollapsedState();
        }

        //-------------------------------------------------------------------------------
        // タイルの共通設定を行う処理
        //-------------------------------------------------------------------------------
        private void ConfigureTile(TileButton tile, string glyph, string title, bool showChevron)
        {
            tile.Glyph = glyph;
            tile.Title = title;
            tile.ShowChevron = showChevron;
            tile.Dock = DockStyle.Fill;
            tile.Margin = new Padding(S(6));
        }

        //-------------------------------------------------------------------------------
        // 一覧の列幅を表示幅に合わせる処理
        //-------------------------------------------------------------------------------
        private void FitListColumns()
        {
            if (stateListView.Columns.Count < 2)
            {
                return;
            }

            var keyWidth = stateListView.ClientSize.Width - stateListView.Columns[0].Width - S(4);
            stateListView.Columns[1].Width = Math.Max(S(120), keyWidth);
        }

        //-------------------------------------------------------------------------------
        // 現在のテーマを全部品へ適用する処理
        //-------------------------------------------------------------------------------
        private void ApplyTheme()
        {
            BackColor = theme.Background;
            ForeColor = theme.Text;
            rootLayout.BackColor = theme.Background;

            foreach (var tile in new[] { statusTile, controllerTile, profileTile, sidewaysTile, inputLogTile, testKeyTile, themeTile })
            {
                tile.Theme = theme;
            }

            themeTile.Glyph = theme.IsDark ? Glyphs.Moon : Glyphs.Sun;
            themeTile.Subtitle = theme.IsDark ? "Dark" : "Light";
            saveButton.Theme = theme;
            powerButton.Theme = theme;
            mappingPanel.Theme = theme;
            logPanel.Theme = theme;
            stateListView.Theme = theme;
            logHeaderPanel.BackColor = theme.Surface;
            logHeaderLabel.BackColor = theme.Surface;
            logHeaderLabel.ForeColor = theme.Text;
            logChevronLabel.BackColor = theme.Surface;
            logChevronLabel.ForeColor = theme.TextSecondary;
            logTextBox.BackColor = theme.Surface;
            logTextBox.ForeColor = theme.TextSecondary;
            bottomPanel.BackColor = theme.Background;
            hintLabel.BackColor = theme.Background;
            hintLabel.ForeColor = theme.TextSecondary;
            ThemedMenu.Apply(deviceMenu, theme);
            ThemedMenu.Apply(profileMenu, theme);
            NativeTheme.ApplyWindowTheme(this, theme);
            NativeTheme.ApplyControlTheme(stateListView, theme);
            NativeTheme.ApplyControlTheme(logTextBox, theme);
            RepaintStateRows();
            Invalidate(true);
        }

        //-------------------------------------------------------------------------------
        // ハンドル作成時にタイトルバーとスクロールバーの配色を合わせる処理
        //-------------------------------------------------------------------------------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyWindowTheme(this, theme);
        }

        //-------------------------------------------------------------------------------
        // 表示直後にスクロールバー配色を合わせる処理（子ハンドル作成後に必要）
        //-------------------------------------------------------------------------------
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NativeTheme.ApplyControlTheme(stateListView, theme);
            NativeTheme.ApplyControlTheme(logTextBox, theme);
            FitListColumns();
        }

        //-------------------------------------------------------------------------------
        // 状態表示ピルを更新する処理
        //-------------------------------------------------------------------------------
        private void UpdateStatusTile()
        {
            var isRunning = pollingTask is not null;
            statusTile.IsOn = isRunning;
            statusTile.Glyph = isRunning ? Glyphs.Play : Glyphs.Link;
            statusTile.Title = isRunning ? "Running" : "Stopped";
            statusTile.Subtitle = isRunning
                ? $"{activeDevice?.ToString() ?? "-"}  ·  {selectedProfile.Name}"
                : devices.Count <= 1
                    ? "No controller found"
                    : $"{devices.Count - 1} controller(s) found  ·  {selectedProfile.Name}";
            powerButton.IsAccent = isRunning;
            controllerTile.Enabled = !isRunning;
            profileTile.Enabled = !isRunning;
            saveButton.Enabled = !isRunning;
        }

        //-------------------------------------------------------------------------------
        // プロファイル配置フォルダを探す処理（exeフォルダから親へたどって最初にcfgがある場所）
        //-------------------------------------------------------------------------------
        private string FindProfileDirectory()
        {
            var directory = new DirectoryInfo(applicationDirectory);

            while (directory is not null)
            {
                if (directory.GetFiles("*.cfg").Length > 0)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return applicationDirectory;
        }

        //-------------------------------------------------------------------------------
        // 設定ファイル（keymap.json）を読み込む処理
        //-------------------------------------------------------------------------------
        private void LoadOrCreateConfig()
        {
            var defaultMappings = CreateDefaultMappings();

            try
            {
                if (!File.Exists(configPath))
                {
                    var json = JsonSerializer.Serialize(defaultMappings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, json, Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                AppendLog($"keymap.json could not be created: {exception.Message}");
            }

            var config = new Dictionary<string, string>();

            try
            {
                if (File.Exists(configPath))
                {
                    config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath, Encoding.UTF8)) ?? [];
                }
            }
            catch (Exception exception)
            {
                AppendLog($"keymap.json could not be read: {exception.Message}");
            }

            foreach (var mapping in defaultMappings)
            {
                config.TryAdd(mapping.Key, mapping.Value);
            }

            keyMappings.Clear();

            foreach (var pair in config)
            {
                if (TryParseKeys(pair.Value, out var keys))
                {
                    keyMappings[pair.Key] = KeyMapping.CreateHold(keys);
                }
            }

            ClearPreservedProfileLines();
            RefreshMappingView();
            AppendLog($"Loaded config: {configPath}");
        }

        //-------------------------------------------------------------------------------
        // cfgプロファイル一覧を更新して選択する処理
        //-------------------------------------------------------------------------------
        private void RefreshProfiles(string selectedPathOverride = "")
        {
            var selectedPath = selectedPathOverride.Length > 0 ? selectedPathOverride : selectedProfile.Path;
            profiles.Clear();
            profiles.Add(ProfileInfo.JsonDefault);

            try
            {
                foreach (var filePath in Directory.GetFiles(profileDirectory, "*.cfg").OrderBy(Path.GetFileName))
                {
                    profiles.Add(new ProfileInfo(Path.GetFileName(filePath), filePath, false));
                }
            }
            catch (Exception exception)
            {
                AppendLog($"Profile directory could not be read: {exception.Message}");
            }

            var target = profiles.FirstOrDefault(x => x.Path == selectedPath && selectedPath.Length > 0) ?? profiles[0];
            SelectProfile(target);
        }

        //-------------------------------------------------------------------------------
        // プロファイルを選択してキーマッピングを読み込む処理
        //-------------------------------------------------------------------------------
        private void SelectProfile(ProfileInfo profile)
        {
            selectedProfile = profile;
            profileTile.Subtitle = profile.Name;
            toolTip.SetToolTip(profileTile, profile.IsJsonDefault ? configPath : profile.Path);

            if (profile.IsJsonDefault)
            {
                LoadOrCreateConfig();
                AddEditableMappingRows(false);
                RefreshMappingView();
            }
            else
            {
                LoadJoyToKeyProfile(profile.Path);
            }

            UpdateStatusTile();
        }

        //-------------------------------------------------------------------------------
        // Profileタイル押下時に選択メニューを開く処理
        //-------------------------------------------------------------------------------
        private void ProfileTile_Click(object? sender, EventArgs e)
        {
            profileMenu.Items.Clear();

            foreach (var profile in profiles)
            {
                var capturedProfile = profile;
                profileMenu.Items.Add(ThemedMenu.CreateItem(profile.Name, (_, _) => SelectProfile(capturedProfile), profile == selectedProfile));
            }

            profileMenu.Items.Add(new ToolStripSeparator());
            profileMenu.Items.Add(ThemedMenu.CreateItem("New profile...", ProfileNew_Click));
            profileMenu.Items.Add(ThemedMenu.CreateItem("Save profile", SaveButton_Click));
            profileMenu.Items.Add(ThemedMenu.CreateItem("Reload list", (_, _) => { RefreshProfiles(); AppendLog($"Profile directory: {profileDirectory}"); }));
            profileMenu.Items.Add(ThemedMenu.CreateItem("Change folder...", ProfileFolder_Click));
            ShowTileMenu(profileMenu, profileTile);
        }

        //-------------------------------------------------------------------------------
        // Controllerタイル押下時に選択メニューを開く処理
        //-------------------------------------------------------------------------------
        private void ControllerTile_Click(object? sender, EventArgs e)
        {
            deviceMenu.Items.Clear();

            foreach (var device in devices)
            {
                var capturedDevice = device;
                deviceMenu.Items.Add(ThemedMenu.CreateItem(device.ToString(), (_, _) => SelectDevice(capturedDevice), device == selectedDevice));
            }

            if (devices.Count > 0)
            {
                deviceMenu.Items.Add(new ToolStripSeparator());
            }

            deviceMenu.Items.Add(ThemedMenu.CreateItem("Refresh controllers", (_, _) => { StopMapping(); RefreshDevices(); }));
            ShowTileMenu(deviceMenu, controllerTile);
        }

        //-------------------------------------------------------------------------------
        // タイルの直下にメニューを表示する処理
        //-------------------------------------------------------------------------------
        private void ShowTileMenu(ContextMenuStrip menu, TileButton tile)
        {
            menu.MinimumSize = new Size(tile.Width, 0);
            menu.Show(tile, new Point(0, tile.Height + S(4)));
        }

        //-------------------------------------------------------------------------------
        // コントローラーを選択する処理
        //-------------------------------------------------------------------------------
        private void SelectDevice(DeviceInfo? device)
        {
            selectedDevice = device;
            controllerTile.Subtitle = device?.ToString() ?? "No controller found";
            UpdateStatusTile();
        }

        //-------------------------------------------------------------------------------
        // 新規cfgプロファイルを作成する処理
        //-------------------------------------------------------------------------------
        private void ProfileNew_Click(object? sender, EventArgs e)
        {
            using var dialog = new ProfileNameDialog(theme);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var fileName = Path.ChangeExtension(dialog.ProfileName, ".cfg");
            var filePath = Path.Combine(profileDirectory, fileName);

            if (File.Exists(filePath))
            {
                MessageBox.Show(this, "Profile already exists.", "Sad2Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            keyMappings.Clear();
            ClearPreservedProfileLines();
            AddEditableMappingRows(true);
            SaveJoyToKeyProfile(filePath);
            RefreshProfiles(filePath);
            AppendLog($"Created profile: {filePath}");
        }

        //-------------------------------------------------------------------------------
        // 現在のプロファイルへマッピングを保存する処理
        //-------------------------------------------------------------------------------
        private void SaveButton_Click(object? sender, EventArgs e)
        {
            if (pollingTask is not null)
            {
                AppendLog("Stop mapping before saving");
                return;
            }

            if (selectedProfile.IsJsonDefault)
            {
                SaveJsonProfile();
                AppendLog($"Saved profile: {configPath}");
                return;
            }

            SaveJoyToKeyProfile(selectedProfile.Path);
            AppendLog($"Saved profile: {selectedProfile.Path}");
        }

        //-------------------------------------------------------------------------------
        // cfgプロファイルフォルダを選択する処理
        //-------------------------------------------------------------------------------
        private void ProfileFolder_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select cfg profile folder",
                SelectedPath = profileDirectory,
                UseDescriptionForTitle = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            profileDirectory = dialog.SelectedPath;
            SaveSettings();
            RefreshProfiles();
            AppendLog($"Profile directory: {profileDirectory}");
        }

        //-------------------------------------------------------------------------------
        // アプリ設定（exe横のsettings.json）を読み込む処理
        //-------------------------------------------------------------------------------
        private AppSettings? LoadSettings()
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        //-------------------------------------------------------------------------------
        // アプリ設定（exe横のsettings.json）を保存する処理
        //-------------------------------------------------------------------------------
        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings(profileDirectory, joyConSidewaysStick, theme.Mode.ToString(), isLogCollapsed);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                AppendLog($"settings.json could not be saved: {exception.Message}");
            }
        }

        //-------------------------------------------------------------------------------
        // Joy-Con横持ちタイル押下時に設定を切り替える処理
        //-------------------------------------------------------------------------------
        private void SidewaysTile_Click(object? sender, EventArgs e)
        {
            joyConSidewaysStick = !joyConSidewaysStick;

            foreach (var switchHidController in switchHidControllers)
            {
                switchHidController.SidewaysStick = joyConSidewaysStick;
            }

            RefreshToggleTiles();
            SaveSettings();
        }

        //-------------------------------------------------------------------------------
        // 入力ログタイル押下時に設定を切り替える処理
        //-------------------------------------------------------------------------------
        private void InputLogTile_Click(object? sender, EventArgs e)
        {
            enableInputLog = !enableInputLog;
            inputEngine.EnableLog = enableInputLog;
            RefreshToggleTiles();
        }

        //-------------------------------------------------------------------------------
        // テーマタイル押下時にダーク／ライトを切り替える処理
        //-------------------------------------------------------------------------------
        private void ThemeTile_Click(object? sender, EventArgs e)
        {
            theme = theme.IsDark ? Theme.Light : Theme.Dark;
            ApplyTheme();
            SaveSettings();
        }

        //-------------------------------------------------------------------------------
        // ON/OFF型タイルの表示を更新する処理
        //-------------------------------------------------------------------------------
        private void RefreshToggleTiles()
        {
            sidewaysTile.IsOn = joyConSidewaysStick;
            sidewaysTile.Subtitle = joyConSidewaysStick ? "On" : "Off";
            inputLogTile.IsOn = enableInputLog;
            inputLogTile.Subtitle = enableInputLog ? "On" : "Off";
        }

        //-------------------------------------------------------------------------------
        // ログ見出し押下時に表示／折りたたみを切り替える処理
        //-------------------------------------------------------------------------------
        private void LogHeader_Click(object? sender, EventArgs e)
        {
            isLogCollapsed = !isLogCollapsed;
            ApplyLogCollapsedState();
            SaveSettings();
        }

        //-------------------------------------------------------------------------------
        // ログの折りたたみ状態を画面へ反映する処理
        //-------------------------------------------------------------------------------
        private void ApplyLogCollapsedState()
        {
            logTextBox.Visible = !isLogCollapsed;
            logChevronLabel.Text = isLogCollapsed ? Glyphs.ChevronUp : Glyphs.ChevronDown;
            rootLayout.RowStyles[3].Height = isLogCollapsed ? S(54) : S(170);
            RefreshToggleTiles();
        }

        //-------------------------------------------------------------------------------
        // JoyToKey形式cfgを読み込む処理
        //-------------------------------------------------------------------------------
        private void LoadJoyToKeyProfile(string filePath)
        {
            keyMappings.Clear();
            ClearPreservedProfileLines();
            var currentSection = preservedHeaderLines;
            var hasUnsupportedMultiMode = false;

            try
            {
                foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
                {
                    var trimmedLine = line.Trim();

                    if (trimmedLine.StartsWith('['))
                    {
                        currentSection = trimmedLine.Equals(Joystick1SectionName, StringComparison.OrdinalIgnoreCase)
                            ? preservedJoystickLines
                            : currentSection == preservedJoystickLines ? preservedTrailingLines : currentSection;   // [Joystick 1]の後は末尾扱い

                        if (currentSection != preservedJoystickLines)
                        {
                            currentSection.Add(line);
                        }

                        continue;
                    }

                    currentSection.Add(line);

                    if (currentSection != preservedJoystickLines || !TrySplitProfileLine(trimmedLine, out var inputName, out var settingValue))
                    {
                        continue;
                    }

                    var mappedInputNames = ConvertJoyToKeyInputNames(inputName).ToArray();

                    if (mappedInputNames.Length == 0 || !KeyMapping.TryParseJoyToKeyValue(settingValue, out var mapping))
                    {
                        continue;                                           // 管理対象外の行は原文保持のみ
                    }

                    hasUnsupportedMultiMode |= mapping.Kind == MappingKind.MultiKeyOther;

                    foreach (var mappedInputName in mappedInputNames)
                    {
                        keyMappings[mappedInputName] = mapping;
                    }
                }
            }
            catch (Exception exception)
            {
                AppendLog($"Profile could not be read: {exception.Message}");
            }

            if (hasUnsupportedMultiMode)
            {
                AppendLog("Unsupported multi-key mode found: only input 1 is used while pressed");
            }

            AddEditableMappingRows(true);
            RefreshMappingView();
            AppendLog($"Loaded profile: {filePath}");
        }

        //-------------------------------------------------------------------------------
        // cfgの「名前=値」行を名前と値に分割する処理
        //-------------------------------------------------------------------------------
        private static bool TrySplitProfileLine(string trimmedLine, out string inputName, out string settingValue)
        {
            inputName = string.Empty;
            settingValue = string.Empty;
            var splitIndex = trimmedLine.IndexOf('=');

            if (trimmedLine.Length == 0 || splitIndex <= 0)
            {
                return false;
            }

            inputName = trimmedLine[..splitIndex].Trim();
            settingValue = trimmedLine[(splitIndex + 1)..].Trim();
            return true;
        }

        //-------------------------------------------------------------------------------
        // 保存時の原文保持用に記憶したcfgの行を破棄する処理
        //-------------------------------------------------------------------------------
        private void ClearPreservedProfileLines()
        {
            preservedHeaderLines.Clear();
            preservedJoystickLines.Clear();
            preservedTrailingLines.Clear();
        }

        //-------------------------------------------------------------------------------
        // UI編集用の基本入力行を追加する処理（cfg用はJoyToKey互換名，json用はSwitch名）
        //-------------------------------------------------------------------------------
        private void AddEditableMappingRows(bool forJoyToKeyProfile)
        {
            if (forJoyToKeyProfile)
            {
                keyMappings.TryAdd("PovUp", KeyMapping.CreateHold([]));
                keyMappings.TryAdd("PovRight", KeyMapping.CreateHold([]));
                keyMappings.TryAdd("PovDown", KeyMapping.CreateHold([]));
                keyMappings.TryAdd("PovLeft", KeyMapping.CreateHold([]));

                for (var buttonNumber = 1; buttonNumber <= 16; buttonNumber++)
                {
                    keyMappings.TryAdd($"Button{buttonNumber:D2}", KeyMapping.CreateHold([]));
                }

                return;
            }

            keyMappings.TryAdd("SwitchDPadUp", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchDPadRight", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchDPadDown", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchDPadLeft", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchAxisUp", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchAxisRight", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchAxisDown", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchAxisLeft", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchRightAxisUp", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchRightAxisRight", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchRightAxisDown", KeyMapping.CreateHold([]));
            keyMappings.TryAdd("SwitchRightAxisLeft", KeyMapping.CreateHold([]));
        }

        //-------------------------------------------------------------------------------
        // keymap.jsonへマッピングを保存する処理（押しっぱなし型のみ対応）
        //-------------------------------------------------------------------------------
        private void SaveJsonProfile()
        {
            var config = keyMappings
                .Where(x => x.Value.Kind == MappingKind.Hold && x.Value.Keys.Count > 0)
                .ToDictionary(x => x.Key, x => string.Join("+", x.Value.Keys.Select(ConvertKeyToConfigName)));

            try
            {
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                AppendLog($"keymap.json could not be saved: {exception.Message}");
            }
        }

        //-------------------------------------------------------------------------------
        // JoyToKey形式cfgへマッピングを保存する処理
        // 読み込み時に記憶した原文を土台にし，管理対象の行だけを現在の割り当てで置き換える
        //-------------------------------------------------------------------------------
        private void SaveJoyToKeyProfile(string filePath)
        {
            var lines = new List<string>();

            if (preservedHeaderLines.Count > 0)
            {
                lines.AddRange(preservedHeaderLines);
            }
            else
            {
                lines.AddRange(CreateDefaultProfileHeaderLines());
            }

            lines.Add(Joystick1SectionName);
            var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var preservedLine in preservedJoystickLines)
            {
                if (!TrySplitProfileLine(preservedLine.Trim(), out var joyToKeyName, out _)
                    || !TryConvertJoyToKeyNameToInputName(joyToKeyName, out var inputName))
                {
                    lines.Add(preservedLine);                               // 管理対象外の行は原文のまま残す
                    continue;
                }

                if (writtenNames.Contains(joyToKeyName))
                {
                    continue;                                               // 重複行は最初の1つだけ残す
                }

                writtenNames.Add(joyToKeyName);

                if (keyMappings.TryGetValue(inputName, out var mapping) && !mapping.IsEmpty)
                {
                    lines.Add($"{joyToKeyName}={mapping.ToJoyToKeyValue()}");
                }
            }

            foreach (var pair in keyMappings.OrderBy(x => x.Key))
            {
                if (pair.Value.IsEmpty
                    || !TryConvertInputNameToJoyToKeyName(pair.Key, out var joyToKeyName)
                    || writtenNames.Contains(joyToKeyName))
                {
                    continue;
                }

                writtenNames.Add(joyToKeyName);
                lines.Add($"{joyToKeyName}={pair.Value.ToJoyToKeyValue()}");
            }

            lines.AddRange(preservedTrailingLines);

            try
            {
                File.WriteAllLines(filePath, lines, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                AppendLog($"Profile could not be saved: {exception.Message}");
            }
        }

        //-------------------------------------------------------------------------------
        // 新規cfg用の[General]セクション行を作成する処理
        //-------------------------------------------------------------------------------
        private static List<string> CreateDefaultProfileHeaderLines()
        {
            return
            [
                "[General]",
                "FileVersion=61",
                "NumberOfJoysticks=1",
                "NumberOfButtons=32",
                "DisplayMode=2",
                "UseDiagonalInput=0",
                "UsePOV8Way=0",
                "RepeatSameKeyInSequence=0",
                "Threshold=20",
                "Threshold2=20",
                "KeySendMode=0",
                "SoundFile=",
                "ImageFile=",
                string.Empty,
            ];
        }

        //-------------------------------------------------------------------------------
        // JoyToKey入力名を一覧表示用の入力名へ変換する処理（保存時の照合用）
        //-------------------------------------------------------------------------------
        private static bool TryConvertJoyToKeyNameToInputName(string joyToKeyName, out string inputName)
        {
            inputName = ConvertJoyToKeyInputNames(joyToKeyName).FirstOrDefault() ?? string.Empty;
            return inputName.Length > 0;
        }

        //-------------------------------------------------------------------------------
        // キー名を設定保存用文字列へ変換する処理
        //-------------------------------------------------------------------------------
        private static string ConvertKeyToConfigName(Keys key)
        {
            return key.ToString();
        }

        //-------------------------------------------------------------------------------
        // 入力名をJoyToKey入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private static bool TryConvertInputNameToJoyToKeyName(string inputName, out string joyToKeyName)
        {
            joyToKeyName = inputName switch
            {
                "SwitchDPadUp" or "PovUp" => "POV1-1",
                "SwitchDPadRight" or "PovRight" => "POV1-3",
                "SwitchDPadDown" or "PovDown" => "POV1-5",
                "SwitchDPadLeft" or "PovLeft" => "POV1-7",
                _ => string.Empty,
            };

            if (joyToKeyName.Length > 0)
            {
                return true;
            }

            if (inputName.Length == 8                                      // 「Button01」形式（2桁）だけを保存対象にする
                && inputName.StartsWith("Button", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(inputName[6..], out var buttonNumber))
            {
                joyToKeyName = $"Button{buttonNumber:D2}";
                return true;
            }

            return false;
        }

        //-------------------------------------------------------------------------------
        // JoyToKeyの入力名をSad2Keyの入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private static IEnumerable<string> ConvertJoyToKeyInputNames(string inputName)
        {
            var povInputNames = ConvertJoyToKeyPovInputNames(inputName).ToArray();

            if (povInputNames.Length > 0)
            {
                foreach (var povInputName in povInputNames)
                {
                    yield return povInputName;
                }

                yield break;
            }

            if (!inputName.StartsWith("Button", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(inputName[6..], out var buttonNumber))
            {
                yield break;
            }

            yield return $"Button{buttonNumber:D2}";
            yield return $"Button{buttonNumber}";
            yield return $"RawButton{buttonNumber}";
        }

        //-------------------------------------------------------------------------------
        // JoyToKeyのPOV入力名をSad2Keyの入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private static IEnumerable<string> ConvertJoyToKeyPovInputNames(string inputName)
        {
            // POVはコントローラー側でJoyToKey互換名（Pov*）として出すため，Switch名へは二重登録しない
            var inputNames = inputName.ToUpperInvariant() switch
            {
                "POV1-1" => ["PovUp"],
                "POV1-3" => ["PovRight"],
                "POV1-5" => ["PovDown"],
                "POV1-7" => ["PovLeft"],
                _ => Array.Empty<string>(),
            };

            foreach (var sad2KeyInputName in inputNames)
            {
                yield return sad2KeyInputName;
            }
        }

        //-------------------------------------------------------------------------------
        // 初期キーマッピングを作成する処理
        //-------------------------------------------------------------------------------
        private static Dictionary<string, string> CreateDefaultMappings()
        {
            return new Dictionary<string, string>
            {
                ["Button1"] = "Space",
                ["Button2"] = "Enter",
                ["Button3"] = "Escape",
                ["Button4"] = "Tab",
                ["Button5"] = "Q",
                ["Button6"] = "E",
                ["Button7"] = "LShiftKey",
                ["Button8"] = "LControlKey",
                ["Button9"] = "R",
                ["Button10"] = "F",
                ["Button11"] = "Z",
                ["Button12"] = "X",
                ["Button13"] = "C",
                ["Button14"] = "V",
                ["Button15"] = "D1",
                ["Button16"] = "D2",
                ["GamepadA"] = "Space",
                ["GamepadB"] = "Enter",
                ["GamepadX"] = "Escape",
                ["GamepadY"] = "Tab",
                ["GamepadLeftShoulder"] = "Q",
                ["GamepadRightShoulder"] = "E",
                ["GamepadLeftTrigger"] = "LShiftKey",
                ["GamepadRightTrigger"] = "LControlKey",
                ["GamepadView"] = "Back",
                ["GamepadMenu"] = "Enter",
                ["SwitchA"] = "Space",
                ["SwitchB"] = "Enter",
                ["SwitchX"] = "Escape",
                ["SwitchY"] = "Tab",
                ["SwitchL"] = "Q",
                ["SwitchR"] = "E",
                ["SwitchZL"] = "LShiftKey",
                ["SwitchZR"] = "LControlKey",
                ["SwitchMinus"] = "Back",
                ["SwitchPlus"] = "Enter",
                ["SwitchHome"] = "Escape",
                ["SwitchCapture"] = "PrintScreen",
                ["SwitchDPadUp"] = "Up",
                ["SwitchDPadDown"] = "Down",
                ["SwitchDPadLeft"] = "Left",
                ["SwitchDPadRight"] = "Right",
                ["SwitchAxisUp"] = "W",
                ["SwitchAxisDown"] = "S",
                ["SwitchAxisLeft"] = "A",
                ["SwitchAxisRight"] = "D",
                ["SwitchRightAxisUp"] = "Up",
                ["SwitchRightAxisDown"] = "Down",
                ["SwitchRightAxisLeft"] = "Left",
                ["SwitchRightAxisRight"] = "Right",
                ["RawButton1"] = "Space",
                ["RawButton2"] = "Enter",
                ["RawButton3"] = "Escape",
                ["RawButton4"] = "Tab",
                ["RawButton5"] = "Q",
                ["RawButton6"] = "E",
                ["RawButton7"] = "LShiftKey",
                ["RawButton8"] = "LControlKey",
                ["RawButton9"] = "R",
                ["RawButton10"] = "F",
                ["RawButton11"] = "Z",
                ["RawButton12"] = "X",
                ["RawButton13"] = "C",
                ["RawButton14"] = "V",
                ["RawButton15"] = "D1",
                ["RawButton16"] = "D2",
                ["RawAxis1Negative"] = "A",
                ["RawAxis1Positive"] = "D",
                ["RawAxis2Negative"] = "W",
                ["RawAxis2Positive"] = "S",
                ["PovUp"] = "Up",
                ["PovDown"] = "Down",
                ["PovLeft"] = "Left",
                ["PovRight"] = "Right",
                ["AxisUp"] = "W",
                ["AxisDown"] = "S",
                ["AxisLeft"] = "A",
                ["AxisRight"] = "D",
            };
        }

        //-------------------------------------------------------------------------------
        // キーマッピング一覧を更新する処理
        //-------------------------------------------------------------------------------
        private void RefreshMappingView()
        {
            stateListView.BeginUpdate();
            stateListView.Items.Clear();
            lastDisplayedInputs.Clear();

            foreach (var mapping in keyMappings.OrderBy(x => x.Key))
            {
                if (IsHiddenAliasRow(mapping.Key))
                {
                    continue;
                }

                var item = new ListViewItem(mapping.Key)
                {
                    BackColor = theme.Surface,
                    ForeColor = theme.Text,
                };
                item.SubItems.Add(mapping.Value.IsEmpty ? string.Empty : mapping.Value.Describe());
                stateListView.Items.Add(item);
            }

            stateListView.EndUpdate();
        }

        //-------------------------------------------------------------------------------
        // テーマ変更時に一覧の行色を現在のテーマで塗り直す処理
        //-------------------------------------------------------------------------------
        private void RepaintStateRows()
        {
            var pressedInputs = lastDisplayedInputs;
            lastDisplayedInputs = [];

            foreach (ListViewItem item in stateListView.Items)
            {
                var isPressed = pressedInputs.Contains(item.Text);
                item.BackColor = isPressed ? theme.Accent : theme.Surface;
                item.ForeColor = isPressed ? theme.OnAccent : theme.Text;
            }

            lastDisplayedInputs = pressedInputs;
            stateListView.Invalidate();
        }

        //-------------------------------------------------------------------------------
        // ButtonNN行がある場合にButtonN / RawButtonNの別名行を一覧から隠すか判定する処理
        //-------------------------------------------------------------------------------
        private bool IsHiddenAliasRow(string inputName)
        {
            var numberText = inputName.StartsWith("RawButton", StringComparison.Ordinal)
                ? inputName[9..]
                : inputName.StartsWith("Button", StringComparison.Ordinal) ? inputName[6..] : string.Empty;

            if (numberText.Length == 0 || !int.TryParse(numberText, out var buttonNumber))
            {
                return false;
            }

            var canonicalName = $"Button{buttonNumber:D2}";
            return inputName != canonicalName && keyMappings.ContainsKey(canonicalName);
        }

        //-------------------------------------------------------------------------------
        // マッピング行ダブルクリック時にキー割り当て編集画面を開く処理
        //-------------------------------------------------------------------------------
        private void StateListView_DoubleClick(object? sender, EventArgs e)
        {
            if (stateListView.SelectedItems.Count == 0)
            {
                return;
            }

            if (pollingTask is not null)
            {
                AppendLog("Stop mapping before editing");                   // 監視スレッドと割り当て辞書の競合を避ける
                return;
            }

            var item = stateListView.SelectedItems[0];
            var inputName = item.Text;
            keyMappings.TryGetValue(inputName, out var currentMapping);

            using var dialog = new MappingEditDialog(inputName, currentMapping ?? KeyMapping.CreateHold([]), theme);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            keyMappings[inputName] = dialog.Result;

            if (TryConvertInputNameToJoyToKeyName(inputName, out var joyToKeyName))
            {
                foreach (var aliasInputName in ConvertJoyToKeyInputNames(joyToKeyName))
                {
                    keyMappings[aliasInputName] = dialog.Result;            // ButtonN / RawButtonN などの別名にも同じ割り当てを反映する
                }
            }

            RefreshMappingView();
            SelectMappingRow(inputName);
        }

        //-------------------------------------------------------------------------------
        // 指定したマッピング行を選択する処理
        //-------------------------------------------------------------------------------
        private void SelectMappingRow(string inputName)
        {
            foreach (ListViewItem item in stateListView.Items)
            {
                if (item.Text != inputName)
                {
                    continue;
                }

                item.Selected = true;
                item.EnsureVisible();
                return;
            }
        }

        //-------------------------------------------------------------------------------
        // 接続済みコントローラーを再読み込みする処理
        //-------------------------------------------------------------------------------
        private void RefreshDevices()
        {
            DisposeSwitchHidControllers();
            devices.Clear();
            devices.Add(DeviceInfo.AllDevices);

            foreach (var switchHidController in SwitchHidController.Enumerate())
            {
                switchHidController.SidewaysStick = joyConSidewaysStick;
                switchHidControllers.Add(switchHidController);
                devices.Add(new DeviceInfo(InputSource.SwitchHid, 0, switchHidController.Name, null, null, switchHidController));
            }

            foreach (var gamepad in Gamepad.Gamepads)
            {
                devices.Add(new DeviceInfo(InputSource.WindowsGamingInput, 0, "Windows Gaming Gamepad", gamepad, null, null));
            }

            foreach (var rawController in RawGameController.RawGameControllers)
            {
                var name = string.IsNullOrWhiteSpace(rawController.DisplayName) ? "Raw Game Controller" : rawController.DisplayName;
                devices.Add(new DeviceInfo(InputSource.RawGameController, 0, name, null, rawController, null));
            }

            for (uint deviceId = 0; deviceId < MaxDevices; deviceId++)
            {
                var caps = new JoyCaps();
                var result = JoyGetDevCaps(deviceId, ref caps, Marshal.SizeOf<JoyCaps>());

                if (result == 0 && TryGetJoyInfo(deviceId, out _))
                {
                    var name = string.IsNullOrWhiteSpace(caps.ProductName) ? $"Controller {deviceId}" : caps.ProductName;
                    devices.Add(new DeviceInfo(InputSource.WinMm, deviceId, name, null, null, null));
                }
            }

            // Switch HID経路があればそれを既定にし，無ければ先頭（All）を選ぶ
            var preferredDevice = devices.FirstOrDefault(x => x.Source == InputSource.SwitchHid)
                ?? devices.FirstOrDefault(x => x.Source != InputSource.All)
                ?? devices.FirstOrDefault();
            SelectDevice(preferredDevice);
            powerButton.Enabled = devices.Count > 1;
            UpdateStatusTile();
        }

        //-------------------------------------------------------------------------------
        // 設定文字列をキーコードへ変換する処理
        //-------------------------------------------------------------------------------
        private static bool TryParseKeys(string keyName, out List<Keys> keys)
        {
            keys = [];
            var aliases = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
            {
                ["LeftShift"] = Keys.LShiftKey,
                ["LeftControl"] = Keys.LControlKey,
                ["RightShift"] = Keys.RShiftKey,
                ["RightControl"] = Keys.RControlKey,
            };

            var keyNames = keyName.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var currentKeyName in keyNames)
            {
                if (aliases.TryGetValue(currentKeyName, out var aliasKey))
                {
                    keys.Add(aliasKey);
                    continue;
                }

                if (Enum.TryParse(currentKeyName, true, out Keys key))
                {
                    keys.Add(key);
                }
            }

            return keys.Count > 0;
        }

        //-------------------------------------------------------------------------------
        // 電源ボタン押下時にキー変換を開始／停止する処理
        //-------------------------------------------------------------------------------
        private void PowerButton_Click(object? sender, EventArgs e)
        {
            if (pollingTask is not null)
            {
                StopMapping();
                return;
            }

            StartMapping();
        }

        //-------------------------------------------------------------------------------
        // キー変換を開始する処理
        //-------------------------------------------------------------------------------
        private void StartMapping()
        {
            if (selectedDevice is null || selectedDevice.Source == InputSource.All && devices.Count <= 1)
            {
                AppendLog("Controller is not selected");
                return;
            }

            inputEngine.ReleaseAll();
            inputEngine.EnableLog = enableInputLog;
            activeDevice = selectedDevice;
            pollingCancellationTokenSource = new CancellationTokenSource();
            pollingTask = Task.Run(() => PollInputLoop(pollingCancellationTokenSource.Token));
            focusSinkTextBox.Focus();
            UpdateStatusTile();
            AppendLog($"Mapping started: {activeDevice}");
        }

        //-------------------------------------------------------------------------------
        // Test keyタイル押下時にキー送信だけを確認する処理
        //-------------------------------------------------------------------------------
        private void TestKeyTile_Click(object? sender, EventArgs e)
        {
            keySender.SendTap(Keys.A);
            AppendLog("TestKey -> A");
        }

        //-------------------------------------------------------------------------------
        // 画面終了時に押下中のキーを解除する処理
        //-------------------------------------------------------------------------------
        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopMapping();
            DisposeSwitchHidControllers();
            SaveSettings();
        }

        //-------------------------------------------------------------------------------
        // キー変換を停止する処理
        //-------------------------------------------------------------------------------
        private void StopMapping()
        {
            var wasRunning = pollingTask is not null;
            pollingCancellationTokenSource?.Cancel();
            pollingTask?.Wait(300);
            pollingCancellationTokenSource?.Dispose();
            pollingCancellationTokenSource = null;
            pollingTask = null;
            inputEngine.ReleaseAll();
            activeDevice = null;
            UpdateStatusTile();

            if (wasRunning)
            {
                AppendLog("Mapping stopped");
            }
        }

        //-------------------------------------------------------------------------------
        // バックグラウンドでコントローラー入力を読み取る処理
        //-------------------------------------------------------------------------------
        private async Task PollInputLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var device = activeDevice;

                if (device is null)
                {
                    return;
                }

                if (!TryGetPressedInputs(device, devices, out var currentInputs))
                {
                    BeginInvoke(() =>
                    {
                        AppendLog("Controller disconnected");
                        StopMapping();
                        RefreshDevices();
                    });
                    return;
                }

                inputEngine.Update(currentInputs, Environment.TickCount64);   // 長押し判定とタップ解除もここで進む
                QueueStateViewUpdate(currentInputs);

                if (device.Source == InputSource.SwitchHid)
                {
                    continue;
                }

                try
                {
                    await Task.Delay(1, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        //-------------------------------------------------------------------------------
        // 選択中コントローラーの押下中入力を取得する処理
        //-------------------------------------------------------------------------------
        private static bool TryGetPressedInputs(DeviceInfo device, List<DeviceInfo> allDevices, out HashSet<string> inputs)
        {
            inputs = [];

            if (device.Source == InputSource.All)
            {
                var availableDeviceCount = 0;

                foreach (var currentDevice in allDevices)
                {
                    if (currentDevice.Source == InputSource.All || !TryGetPressedInputs(currentDevice, allDevices, out var currentInputs))
                    {
                        continue;
                    }

                    availableDeviceCount++;

                    foreach (var inputName in currentInputs)
                    {
                        inputs.Add(inputName);
                    }
                }

                return availableDeviceCount > 0;
            }

            if (device.Source == InputSource.WindowsGamingInput)
            {
                if (device.Gamepad is null)
                {
                    return false;
                }

                inputs = GetPressedInputs(device.Gamepad.GetCurrentReading());
                return true;
            }

            if (device.Source == InputSource.SwitchHid)
            {
                if (device.SwitchHidController is null)
                {
                    return false;
                }

                return device.SwitchHidController.TryGetPressedInputs(out inputs, 5);
            }

            if (device.Source == InputSource.RawGameController)
            {
                if (device.RawGameController is null)
                {
                    return false;
                }

                inputs = GetPressedInputs(device.RawGameController);
                return true;
            }

            if (!TryGetJoyInfo(device.Id, out var joyInfo))
            {
                return false;
            }

            inputs = GetPressedInputs(joyInfo);
            return true;
        }

        //-------------------------------------------------------------------------------
        // RawGameController入力から押下中の入力名を作成する処理
        //-------------------------------------------------------------------------------
        private static HashSet<string> GetPressedInputs(RawGameController controller)
        {
            var inputs = new HashSet<string>();
            var buttons = new bool[controller.ButtonCount];
            var switches = new GameControllerSwitchPosition[controller.SwitchCount];
            var axes = new double[controller.AxisCount];

            controller.GetCurrentReading(buttons, switches, axes);

            for (var buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
            {
                if (buttons[buttonIndex])
                {
                    inputs.Add($"RawButton{buttonIndex + 1}");
                }
            }

            for (var switchIndex = 0; switchIndex < switches.Length; switchIndex++)
            {
                AddRawSwitchInputs(inputs, switches[switchIndex], switchIndex + 1);
            }

            for (var axisIndex = 0; axisIndex < axes.Length; axisIndex++)
            {
                if (axes[axisIndex] < 0.35)
                {
                    inputs.Add($"RawAxis{axisIndex + 1}Negative");
                }
                else if (axes[axisIndex] > 0.65)
                {
                    inputs.Add($"RawAxis{axisIndex + 1}Positive");
                }
            }

            return inputs;
        }

        //-------------------------------------------------------------------------------
        // RawGameControllerのスイッチ入力を入力名に変換する処理
        //-------------------------------------------------------------------------------
        private static void AddRawSwitchInputs(HashSet<string> inputs, GameControllerSwitchPosition switchPosition, int switchNumber)
        {
            var prefix = switchNumber == 1 ? "Pov" : $"RawSwitch{switchNumber}";

            if ((switchPosition & GameControllerSwitchPosition.Up) == GameControllerSwitchPosition.Up)
            {
                inputs.Add($"{prefix}Up");
            }

            if ((switchPosition & GameControllerSwitchPosition.Down) == GameControllerSwitchPosition.Down)
            {
                inputs.Add($"{prefix}Down");
            }

            if ((switchPosition & GameControllerSwitchPosition.Left) == GameControllerSwitchPosition.Left)
            {
                inputs.Add($"{prefix}Left");
            }

            if ((switchPosition & GameControllerSwitchPosition.Right) == GameControllerSwitchPosition.Right)
            {
                inputs.Add($"{prefix}Right");
            }
        }

        //-------------------------------------------------------------------------------
        // コントローラーの入力状態を取得する処理
        //-------------------------------------------------------------------------------
        private static bool TryGetJoyInfo(uint deviceId, out JoyInfoEx joyInfo)
        {
            joyInfo = new JoyInfoEx
            {
                Size = Marshal.SizeOf<JoyInfoEx>(),
                Flags = JoyReturnAll,
            };

            return JoyGetPosEx(deviceId, ref joyInfo) == 0;
        }

        //-------------------------------------------------------------------------------
        // 押下中の入力名を作成する処理
        //-------------------------------------------------------------------------------
        private static HashSet<string> GetPressedInputs(JoyInfoEx joyInfo)
        {
            var inputs = new HashSet<string>();

            for (var buttonIndex = 0; buttonIndex < MaxButtons; buttonIndex++)
            {
                if ((joyInfo.Buttons & (1u << buttonIndex)) != 0)
                {
                    inputs.Add($"Button{buttonIndex + 1}");
                }
            }

            AddAxisInputs(inputs, joyInfo);
            AddPovInputs(inputs, joyInfo);

            return inputs;
        }

        //-------------------------------------------------------------------------------
        // Gamepad入力から押下中の入力名を作成する処理
        //-------------------------------------------------------------------------------
        private static HashSet<string> GetPressedInputs(GamepadReading reading)
        {
            var inputs = new HashSet<string>();
            var buttons = reading.Buttons;

            AddGamepadButton(inputs, buttons, GamepadButtons.A, "GamepadA");
            AddGamepadButton(inputs, buttons, GamepadButtons.B, "GamepadB");
            AddGamepadButton(inputs, buttons, GamepadButtons.X, "GamepadX");
            AddGamepadButton(inputs, buttons, GamepadButtons.Y, "GamepadY");
            AddGamepadButton(inputs, buttons, GamepadButtons.LeftShoulder, "GamepadLeftShoulder");
            AddGamepadButton(inputs, buttons, GamepadButtons.RightShoulder, "GamepadRightShoulder");
            AddGamepadButton(inputs, buttons, GamepadButtons.View, "GamepadView");
            AddGamepadButton(inputs, buttons, GamepadButtons.Menu, "GamepadMenu");
            AddGamepadButton(inputs, buttons, GamepadButtons.DPadUp, "PovUp");
            AddGamepadButton(inputs, buttons, GamepadButtons.DPadDown, "PovDown");
            AddGamepadButton(inputs, buttons, GamepadButtons.DPadLeft, "PovLeft");
            AddGamepadButton(inputs, buttons, GamepadButtons.DPadRight, "PovRight");

            if (reading.LeftTrigger > 0.35)
            {
                inputs.Add("GamepadLeftTrigger");
            }

            if (reading.RightTrigger > 0.35)
            {
                inputs.Add("GamepadRightTrigger");
            }

            if (reading.LeftThumbstickX < -0.35)
            {
                inputs.Add("AxisLeft");
            }
            else if (reading.LeftThumbstickX > 0.35)
            {
                inputs.Add("AxisRight");
            }

            if (reading.LeftThumbstickY > 0.35)
            {
                inputs.Add("AxisUp");
            }
            else if (reading.LeftThumbstickY < -0.35)
            {
                inputs.Add("AxisDown");
            }

            return inputs;
        }

        //-------------------------------------------------------------------------------
        // Gamepadボタン押下時に入力名を追加する処理
        //-------------------------------------------------------------------------------
        private static void AddGamepadButton(HashSet<string> inputs, GamepadButtons buttons, GamepadButtons target, string inputName)
        {
            if ((buttons & target) == target)
            {
                inputs.Add(inputName);
            }
        }

        //-------------------------------------------------------------------------------
        // スティック入力を入力名に変換する処理
        //-------------------------------------------------------------------------------
        private static void AddAxisInputs(HashSet<string> inputs, JoyInfoEx joyInfo)
        {
            const uint lowThreshold = 22000;
            const uint highThreshold = 43000;

            if (joyInfo.Xpos < lowThreshold)
            {
                inputs.Add("AxisLeft");
            }
            else if (joyInfo.Xpos > highThreshold)
            {
                inputs.Add("AxisRight");
            }

            if (joyInfo.Ypos < lowThreshold)
            {
                inputs.Add("AxisUp");
            }
            else if (joyInfo.Ypos > highThreshold)
            {
                inputs.Add("AxisDown");
            }
        }

        //-------------------------------------------------------------------------------
        // 十字キー入力を入力名に変換する処理
        //-------------------------------------------------------------------------------
        private static void AddPovInputs(HashSet<string> inputs, JoyInfoEx joyInfo)
        {
            if (joyInfo.Pov == 0xFFFF)
            {
                return;
            }

            var angle = joyInfo.Pov / 100;

            if (angle >= 315 || angle <= 45)
            {
                inputs.Add("PovUp");
            }

            if (angle >= 45 && angle <= 135)
            {
                inputs.Add("PovRight");
            }

            if (angle >= 135 && angle <= 225)
            {
                inputs.Add("PovDown");
            }

            if (angle >= 225 && angle <= 315)
            {
                inputs.Add("PovLeft");
            }
        }

        //-------------------------------------------------------------------------------
        // 入力状態一覧を更新する処理（押下中の行をアクセント色にする）
        //-------------------------------------------------------------------------------
        private void UpdateStateView(HashSet<string> currentInputs)
        {
            if (lastDisplayedInputs.SetEquals(currentInputs))
            {
                return;
            }

            stateListView.BeginUpdate();

            foreach (var inputName in currentInputs.Where(x => !keyMappings.ContainsKey(x)).OrderBy(x => x))
            {
                if (stateListView.Items.Cast<ListViewItem>().Any(x => x.Text == inputName))
                {
                    continue;
                }

                var item = new ListViewItem(inputName);
                item.SubItems.Add("-");
                stateListView.Items.Add(item);
            }

            foreach (ListViewItem item in stateListView.Items)
            {
                var isPressed = currentInputs.Contains(item.Text);
                item.BackColor = isPressed ? theme.Accent : theme.Surface;
                item.ForeColor = isPressed ? theme.OnAccent : theme.Text;
            }

            lastDisplayedInputs = currentInputs.ToHashSet();
            stateListView.EndUpdate();
        }

        //-------------------------------------------------------------------------------
        // 入力状態一覧の更新をUIスレッドへ予約する処理
        //-------------------------------------------------------------------------------
        private void QueueStateViewUpdate(HashSet<string> currentInputs)
        {
            var nowTicks = Environment.TickCount64;

            if (nowTicks - lastStateViewUpdateTicks < 50)
            {
                return;
            }

            lastStateViewUpdateTicks = nowTicks;
            var snapshot = currentInputs.ToHashSet();

            if (IsHandleCreated)
            {
                BeginInvoke(() => UpdateStateView(snapshot));
            }
        }

        //-------------------------------------------------------------------------------
        // ログを画面へ追記する処理
        //-------------------------------------------------------------------------------
        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => AppendLog(message));
                return;
            }

            logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        //-------------------------------------------------------------------------------
        // Switch HIDコントローラーを解放する処理
        //-------------------------------------------------------------------------------
        private void DisposeSwitchHidControllers()
        {
            foreach (var switchHidController in switchHidControllers)
            {
                switchHidController.Dispose();
            }

            switchHidControllers.Clear();
        }

        //-------------------------------------------------------------------------------
        // ジョイスティック性能情報を取得するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)]
        private static extern uint JoyGetDevCaps(uint deviceId, ref JoyCaps caps, int capsSize);

        //-------------------------------------------------------------------------------
        // ジョイスティック入力状態を取得するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("winmm.dll", EntryPoint = "joyGetPosEx")]
        private static extern uint JoyGetPosEx(uint deviceId, ref JoyInfoEx joyInfo);

        private sealed record DeviceInfo(InputSource Source, uint Id, string Name, Gamepad? Gamepad, RawGameController? RawGameController, SwitchHidController? SwitchHidController)
        {
            public static readonly DeviceInfo AllDevices = new(InputSource.All, 0, "All detected controllers", null, null, null);

            //-------------------------------------------------------------------------------
            // コントローラー表示名を返す処理
            //-------------------------------------------------------------------------------
            public override string ToString()
            {
                return Source == InputSource.All
                    ? Name
                    : Source == InputSource.SwitchHid
                    ? $"{Name} (Switch HID)"
                    : Source == InputSource.WindowsGamingInput
                    ? $"{Name} (Windows.Gaming.Input)"
                    : Source == InputSource.RawGameController
                        ? $"{Name} (RawGameController)"
                    : $"{Name} (winmm ID {Id})";
            }
        }

        private enum InputSource
        {
            All,
            SwitchHid,
            WindowsGamingInput,
            RawGameController,
            WinMm,
        }

        private sealed record AppSettings(string ProfileDirectory, bool JoyConSidewaysStick = true, string Theme = "Dark", bool LogCollapsed = false);

        private sealed record ProfileInfo(string Name, string Path, bool IsJsonDefault)
        {
            public static readonly ProfileInfo JsonDefault = new(JsonProfileFileName, string.Empty, true);

            //-------------------------------------------------------------------------------
            // プロファイル表示名を返す処理
            //-------------------------------------------------------------------------------
            public override string ToString()
            {
                return Name;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JoyCaps
        {
            public ushort Mid;
            public ushort Pid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string ProductName;
            public uint Xmin;
            public uint Xmax;
            public uint Ymin;
            public uint Ymax;
            public uint Zmin;
            public uint Zmax;
            public uint NumButtons;
            public uint PeriodMin;
            public uint PeriodMax;
            public uint Rmin;
            public uint Rmax;
            public uint Umin;
            public uint Umax;
            public uint Vmin;
            public uint Vmax;
            public uint Caps;
            public uint MaxAxes;
            public uint NumAxes;
            public uint MaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string RegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string OemVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JoyInfoEx
        {
            public int Size;
            public int Flags;
            public uint Xpos;
            public uint Ypos;
            public uint Zpos;
            public uint Rpos;
            public uint Upos;
            public uint Vpos;
            public uint Buttons;
            public uint ButtonNumber;
            public uint Pov;
            public uint Reserved1;
            public uint Reserved2;
        }
    }
}
