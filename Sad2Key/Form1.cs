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
        private const uint InputKeyboard = 1;
        private const uint KeyEventFExtendedKey = 0x0001;
        private const uint KeyEventFKeyUp = 0x0002;

        private readonly ComboBox deviceComboBox;
        private readonly Button refreshButton;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button testKeyButton;
        private readonly CheckBox inputLogCheckBox;
        private readonly ListBox profileListBox;
        private readonly Button profileRefreshButton;
        private readonly Button profileNewButton;
        private readonly Button profileSaveButton;
        private readonly Button profileFolderButton;
        private readonly Label currentProfileLabel;
        private readonly TextBox focusSinkTextBox;
        private readonly Label statusLabel;
        private readonly ListView stateListView;
        private readonly TextBox logTextBox;
        private readonly TextBox configPathTextBox;
        private readonly List<DeviceInfo> devices = [];
        private readonly List<SwitchHidController> switchHidControllers = [];
        private readonly Dictionary<string, List<Keys>> keyMappings = [];
        private readonly HashSet<string> activeInputs = [];
        private readonly object inputLock = new();
        private readonly string configPath;
        private readonly string settingsPath;
        private string profileDirectory;
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
            configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Sad2Key",
                "keymap.json");
            settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Sad2Key",
                "settings.json");
            profileDirectory = LoadProfileDirectorySetting() ?? FindProfileDirectory();

            deviceComboBox = new ComboBox();
            refreshButton = new Button();
            startButton = new Button();
            stopButton = new Button();
            testKeyButton = new Button();
            inputLogCheckBox = new CheckBox();
            profileListBox = new NonKeyboardListBox();
            profileRefreshButton = new Button();
            profileNewButton = new Button();
            profileSaveButton = new Button();
            profileFolderButton = new Button();
            currentProfileLabel = new Label();
            focusSinkTextBox = new TextBox();
            statusLabel = new Label();
            stateListView = new DoubleBufferedListView();
            logTextBox = new TextBox();
            configPathTextBox = new TextBox();
            InitializeUserInterface();
            LoadOrCreateConfig();
            RefreshProfiles();
            RefreshDevices();

            FormClosing += Form1_FormClosing;
        }

        //-------------------------------------------------------------------------------
        // 画面部品を配置する処理
        //-------------------------------------------------------------------------------
        private void InitializeUserInterface()
        {
            var rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12),
            };
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            Controls.Add(rootLayout);

            var topLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 4,
            };
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            rootLayout.Controls.Add(topLayout, 0, 0);

            topLayout.Controls.Add(new Label
            {
                Text = "Controller",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            deviceComboBox.Dock = DockStyle.Fill;
            deviceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            topLayout.Controls.Add(deviceComboBox, 1, 0);

            refreshButton.Text = "Refresh";
            refreshButton.Dock = DockStyle.Fill;
            refreshButton.Click += RefreshButton_Click;
            topLayout.Controls.Add(refreshButton, 2, 0);

            startButton.Text = "Start";
            startButton.Dock = DockStyle.Fill;
            startButton.Click += StartButton_Click;
            topLayout.Controls.Add(startButton, 3, 0);

            stopButton.Text = "Stop";
            stopButton.Dock = DockStyle.Fill;
            stopButton.Enabled = false;
            stopButton.Click += StopButton_Click;
            topLayout.Controls.Add(stopButton, 4, 0);

            testKeyButton.Text = "Test Key";
            testKeyButton.Dock = DockStyle.Fill;
            testKeyButton.Click += TestKeyButton_Click;
            topLayout.Controls.Add(testKeyButton, 5, 0);

            topLayout.Controls.Add(new Label
            {
                Text = "Config",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 1);

            configPathTextBox.Dock = DockStyle.Fill;
            configPathTextBox.ReadOnly = true;
            configPathTextBox.Text = configPath;
            topLayout.SetColumnSpan(configPathTextBox, 5);
            topLayout.Controls.Add(configPathTextBox, 1, 1);

            inputLogCheckBox.Text = "Input event log";
            inputLogCheckBox.Dock = DockStyle.Fill;
            inputLogCheckBox.Checked = false;
            inputLogCheckBox.CheckedChanged += InputLogCheckBox_CheckedChanged;
            topLayout.SetColumnSpan(inputLogCheckBox, 5);
            topLayout.Controls.Add(inputLogCheckBox, 1, 2);

            topLayout.Controls.Add(new Label
            {
                Text = "Profile",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 3);

            currentProfileLabel.Dock = DockStyle.Fill;
            currentProfileLabel.BorderStyle = BorderStyle.FixedSingle;
            currentProfileLabel.TextAlign = ContentAlignment.MiddleLeft;
            currentProfileLabel.Padding = new Padding(8, 0, 0, 0);
            topLayout.SetColumnSpan(currentProfileLabel, 4);
            topLayout.Controls.Add(currentProfileLabel, 1, 3);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            rootLayout.Controls.Add(statusLabel, 0, 1);

            var mappingLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
            };
            mappingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            mappingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.Controls.Add(mappingLayout, 0, 2);

            var profilePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
            };
            profilePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profilePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            profilePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            profilePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            profilePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            mappingLayout.Controls.Add(profilePanel, 0, 0);

            profileListBox.Dock = DockStyle.Fill;
            profileListBox.SelectedIndexChanged += ProfileListBox_SelectedIndexChanged;
            profilePanel.SetColumnSpan(profileListBox, 2);
            profilePanel.Controls.Add(profileListBox, 0, 0);

            profileNewButton.Text = "New";
            profileNewButton.Dock = DockStyle.Fill;
            profileNewButton.Click += ProfileNewButton_Click;
            profilePanel.Controls.Add(profileNewButton, 0, 1);

            profileSaveButton.Text = "Save";
            profileSaveButton.Dock = DockStyle.Fill;
            profileSaveButton.Click += ProfileSaveButton_Click;
            profilePanel.Controls.Add(profileSaveButton, 1, 1);

            profileFolderButton.Text = "Folder";
            profileFolderButton.Dock = DockStyle.Fill;
            profileFolderButton.Click += ProfileFolderButton_Click;
            profilePanel.Controls.Add(profileFolderButton, 0, 2);

            profileRefreshButton.Text = "Reload";
            profileRefreshButton.Dock = DockStyle.Fill;
            profileRefreshButton.Click += ProfileRefreshButton_Click;
            profilePanel.Controls.Add(profileRefreshButton, 1, 2);

            stateListView.Dock = DockStyle.Fill;
            stateListView.View = View.Details;
            stateListView.FullRowSelect = true;
            stateListView.GridLines = true;
            stateListView.Columns.Add("Input", 180);
            stateListView.Columns.Add("Key", 120);
            stateListView.Columns.Add("Active", 100);
            stateListView.DoubleClick += StateListView_DoubleClick;
            mappingLayout.Controls.Add(stateListView, 1, 0);

            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            rootLayout.Controls.Add(logTextBox, 0, 3);

            focusSinkTextBox.BorderStyle = BorderStyle.None;
            focusSinkTextBox.Location = new Point(-200, -200);
            focusSinkTextBox.Size = new Size(1, 1);
            focusSinkTextBox.TabStop = false;
            Controls.Add(focusSinkTextBox);
        }

        //-------------------------------------------------------------------------------
        // プロファイル配置フォルダを探す処理
        //-------------------------------------------------------------------------------
        private static string FindProfileDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (directory.GetFiles("*.cfg").Length > 0)
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory;
        }

        //-------------------------------------------------------------------------------
        // 設定ファイルを読み込む処理
        //-------------------------------------------------------------------------------
        private void LoadOrCreateConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            var defaultMappings = CreateDefaultMappings();

            if (!File.Exists(configPath))
            {
                var json = JsonSerializer.Serialize(defaultMappings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json, Encoding.UTF8);
            }

            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configPath, Encoding.UTF8)) ?? [];

            foreach (var mapping in defaultMappings)
            {
                config.TryAdd(mapping.Key, mapping.Value);
            }

            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            keyMappings.Clear();

            foreach (var pair in config)
            {
                if (TryParseKeys(pair.Value, out var keys))
                {
                    keyMappings[pair.Key] = keys;
                }
            }

            RefreshMappingView();
            AppendLog($"Loaded config: {configPath}");
        }

        //-------------------------------------------------------------------------------
        // cfgプロファイル一覧を更新する処理
        //-------------------------------------------------------------------------------
        private void RefreshProfiles(string selectedPathOverride = "")
        {
            var selectedPath = selectedPathOverride.Length > 0
                ? selectedPathOverride
                : profileListBox.SelectedItem is ProfileInfo selectedProfile
                ? selectedProfile.Path
                : string.Empty;
            profileListBox.Items.Clear();
            profileListBox.Items.Add(ProfileInfo.JsonDefault);

            foreach (var filePath in Directory.GetFiles(profileDirectory, "*.cfg").OrderBy(Path.GetFileName))
            {
                profileListBox.Items.Add(new ProfileInfo(Path.GetFileName(filePath), filePath, false));
            }

            for (var itemIndex = 0; itemIndex < profileListBox.Items.Count; itemIndex++)
            {
                if (profileListBox.Items[itemIndex] is ProfileInfo profile && profile.Path == selectedPath)
                {
                    profileListBox.SelectedIndex = itemIndex;
                    return;
                }
            }

            profileListBox.SelectedIndex = 0;
        }

        //-------------------------------------------------------------------------------
        // プロファイル再読込ボタン押下時にcfg一覧を更新する処理
        //-------------------------------------------------------------------------------
        private void ProfileRefreshButton_Click(object? sender, EventArgs e)
        {
            RefreshProfiles();
            AppendLog($"Profile directory: {profileDirectory}");
        }

        //-------------------------------------------------------------------------------
        // 新規cfgプロファイルを作成する処理
        //-------------------------------------------------------------------------------
        private void ProfileNewButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new ProfileNameDialog();

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var fileName = Path.ChangeExtension(dialog.ProfileName, ".cfg");
            var filePath = Path.Combine(profileDirectory, fileName);

            if (File.Exists(filePath))
            {
                MessageBox.Show("Profile already exists.", "Sad2Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            keyMappings.Clear();
            AddEditableMappingRows();
            SaveJoyToKeyProfile(filePath);
            RefreshProfiles(filePath);
            AppendLog($"Created profile: {filePath}");
        }

        //-------------------------------------------------------------------------------
        // 現在のプロファイルへマッピングを保存する処理
        //-------------------------------------------------------------------------------
        private void ProfileSaveButton_Click(object? sender, EventArgs e)
        {
            if (profileListBox.SelectedItem is not ProfileInfo profile)
            {
                return;
            }

            if (profile.IsJsonDefault)
            {
                SaveJsonProfile();
                AppendLog($"Saved profile: {configPath}");
                return;
            }

            SaveJoyToKeyProfile(profile.Path);
            AppendLog($"Saved profile: {profile.Path}");
        }

        //-------------------------------------------------------------------------------
        // cfgプロファイルフォルダを選択する処理
        //-------------------------------------------------------------------------------
        private void ProfileFolderButton_Click(object? sender, EventArgs e)
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
            SaveProfileDirectorySetting();
            RefreshProfiles();
            AppendLog($"Profile directory: {profileDirectory}");
        }

        //-------------------------------------------------------------------------------
        // プロファイルフォルダ設定を読み込む処理
        //-------------------------------------------------------------------------------
        private string? LoadProfileDirectorySetting()
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
                return settings is not null && Directory.Exists(settings.ProfileDirectory)
                    ? settings.ProfileDirectory
                    : null;
            }
            catch
            {
                return null;
            }
        }

        //-------------------------------------------------------------------------------
        // プロファイルフォルダ設定を保存する処理
        //-------------------------------------------------------------------------------
        private void SaveProfileDirectorySetting()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var settings = new AppSettings(profileDirectory);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }

        //-------------------------------------------------------------------------------
        // プロファイル選択変更時にキーマッピングを読み込む処理
        //-------------------------------------------------------------------------------
        private void ProfileListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (profileListBox.SelectedItem is not ProfileInfo profile)
            {
                return;
            }

            currentProfileLabel.Text = profile.Name;

            if (profile.IsJsonDefault)
            {
                LoadOrCreateConfig();
                AddEditableMappingRows();
                RefreshMappingView();
                configPathTextBox.Text = configPath;
                return;
            }

            LoadJoyToKeyProfile(profile.Path);
            configPathTextBox.Text = profile.Path;
        }

        //-------------------------------------------------------------------------------
        // JoyToKey形式cfgを読み込む処理
        //-------------------------------------------------------------------------------
        private void LoadJoyToKeyProfile(string filePath)
        {
            keyMappings.Clear();

            foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Length == 0 || trimmedLine.StartsWith("[") || !trimmedLine.Contains('='))
                {
                    continue;
                }

                var splitIndex = trimmedLine.IndexOf('=');
                var inputName = trimmedLine[..splitIndex].Trim();
                var settingValue = trimmedLine[(splitIndex + 1)..].Trim();

                if (!TryParseJoyToKeyKeys(settingValue, out var keys))
                {
                    continue;
                }

                foreach (var mappedInputName in ConvertJoyToKeyInputNames(inputName))
                {
                    keyMappings[mappedInputName] = keys;
                }
            }

            AddEditableMappingRows();
            RefreshMappingView();
            AppendLog($"Loaded profile: {filePath}");
        }

        //-------------------------------------------------------------------------------
        // UI編集用の基本入力行を追加する処理
        //-------------------------------------------------------------------------------
        private void AddEditableMappingRows()
        {
            keyMappings.TryAdd("SwitchDPadUp", []);
            keyMappings.TryAdd("SwitchDPadRight", []);
            keyMappings.TryAdd("SwitchDPadDown", []);
            keyMappings.TryAdd("SwitchDPadLeft", []);
            keyMappings.TryAdd("SwitchAxisUp", []);
            keyMappings.TryAdd("SwitchAxisRight", []);
            keyMappings.TryAdd("SwitchAxisDown", []);
            keyMappings.TryAdd("SwitchAxisLeft", []);
            keyMappings.TryAdd("SwitchRightAxisUp", []);
            keyMappings.TryAdd("SwitchRightAxisRight", []);
            keyMappings.TryAdd("SwitchRightAxisDown", []);
            keyMappings.TryAdd("SwitchRightAxisLeft", []);

            for (var buttonNumber = 1; buttonNumber <= 16; buttonNumber++)
            {
                keyMappings.TryAdd($"Button{buttonNumber:D2}", []);
            }
        }

        //-------------------------------------------------------------------------------
        // keymap.jsonへマッピングを保存する処理
        //-------------------------------------------------------------------------------
        private void SaveJsonProfile()
        {
            var config = keyMappings
                .Where(x => x.Value.Count > 0)
                .ToDictionary(x => x.Key, x => string.Join("+", x.Value.Select(ConvertKeyToConfigName)));

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }

        //-------------------------------------------------------------------------------
        // JoyToKey形式cfgへマッピングを保存する処理
        //-------------------------------------------------------------------------------
        private void SaveJoyToKeyProfile(string filePath)
        {
            var lines = new List<string>
            {
                "[General]",
                "FileVersion=61",
                "NumberOfJoysticks=2",
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
                "[Joystick 1]",
            };

            foreach (var mapping in keyMappings.OrderBy(x => x.Key))
            {
                if (mapping.Value.Count == 0 || !TryConvertInputNameToJoyToKeyName(mapping.Key, out var joyToKeyName))
                {
                    continue;
                }

                lines.Add($"{joyToKeyName}=1, {ConvertKeysToJoyToKeyCode(mapping.Value)}, 0.000, 0, 0");
            }

            File.WriteAllLines(filePath, lines, Encoding.UTF8);
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

            if (inputName.StartsWith("Button", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(inputName[6..], out var buttonNumber))
            {
                joyToKeyName = $"Button{buttonNumber:D2}";
                return true;
            }

            return false;
        }

        //-------------------------------------------------------------------------------
        // キー一覧をJoyToKeyのキーコード文字列へ変換する処理
        //-------------------------------------------------------------------------------
        private static string ConvertKeysToJoyToKeyCode(List<Keys> keys)
        {
            var keyCodes = keys
                .Take(4)
                .Select(x => ((int)x).ToString("X2"))
                .ToList();

            while (keyCodes.Count < 4)
            {
                keyCodes.Add("00");
            }

            return string.Join(":", keyCodes);
        }

        //-------------------------------------------------------------------------------
        // JoyToKeyのキー設定値をキーコードへ変換する処理
        //-------------------------------------------------------------------------------
        private static bool TryParseJoyToKeyKeys(string settingValue, out List<Keys> keys)
        {
            keys = [];
            var parts = settingValue.Split(',', StringSplitOptions.TrimEntries);

            if (parts.Length < 2 || parts[0] != "1")
            {
                return false;
            }

            var keyCodes = parts[1].Split(':');

            foreach (var keyCode in keyCodes)
            {
                if (!int.TryParse(keyCode, System.Globalization.NumberStyles.HexNumber, null, out var virtualKey)
                    || virtualKey == 0)
                {
                    continue;
                }

                keys.Add((Keys)virtualKey);
            }

            return keys.Count > 0;
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
            var inputNames = inputName.ToUpperInvariant() switch
            {
                "POV1-1" => ["SwitchDPadUp", "PovUp"],
                "POV1-3" => ["SwitchDPadRight", "PovRight"],
                "POV1-5" => ["SwitchDPadDown", "PovDown"],
                "POV1-7" => ["SwitchDPadLeft", "PovLeft"],
                _ => Array.Empty<string>(),
            };

            foreach (var sad2KeyInputName in inputNames)
            {
                yield return sad2KeyInputName;
            }
        }

        //-------------------------------------------------------------------------------
        // JoyToKeyのボタン番号をSwitch入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private static IEnumerable<string> ConvertJoyToKeyButtonToSwitchInputs(int buttonNumber)
        {
            var inputNames = buttonNumber switch
            {
                1 => ["SwitchB"],
                2 => ["SwitchA"],
                3 => ["SwitchY"],
                4 => ["SwitchX"],
                5 => ["SwitchL"],
                6 => ["SwitchR"],
                7 => ["SwitchZL"],
                8 => ["SwitchZR"],
                9 => ["SwitchMinus"],
                10 => ["SwitchPlus"],
                11 => ["SwitchStick"],
                12 => ["SwitchRightStick"],
                13 => ["SwitchHome"],
                14 => ["SwitchCapture"],
                _ => Array.Empty<string>(),
            };

            foreach (var inputName in inputNames)
            {
                yield return inputName;
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
            stateListView.Items.Clear();
            lastDisplayedInputs.Clear();

            foreach (var mapping in keyMappings.OrderBy(x => x.Key))
            {
                var item = new ListViewItem(mapping.Key);
                item.SubItems.Add(string.Join(" + ", mapping.Value));
                item.SubItems.Add(string.Empty);
                stateListView.Items.Add(item);
            }
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

            var item = stateListView.SelectedItems[0];
            var inputName = item.Text;
            keyMappings.TryGetValue(inputName, out var currentKeys);

            using var dialog = new MappingEditDialog(inputName, currentKeys ?? []);

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            keyMappings[inputName] = dialog.SelectedKeys;
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
            deviceComboBox.Items.Clear();
            deviceComboBox.Items.Add(DeviceInfo.AllDevices);

            foreach (var switchHidController in SwitchHidController.Enumerate())
            {
                switchHidControllers.Add(switchHidController);
                var device = new DeviceInfo(InputSource.SwitchHid, 0, switchHidController.Name, null, null, switchHidController);
                devices.Add(device);
                deviceComboBox.Items.Add(device);
            }

            foreach (var gamepad in Gamepad.Gamepads)
            {
                var device = new DeviceInfo(InputSource.WindowsGamingInput, 0, "Windows Gaming Gamepad", gamepad, null, null);
                devices.Add(device);
                deviceComboBox.Items.Add(device);
            }

            foreach (var rawController in RawGameController.RawGameControllers)
            {
                var name = string.IsNullOrWhiteSpace(rawController.DisplayName) ? "Raw Game Controller" : rawController.DisplayName;
                var device = new DeviceInfo(InputSource.RawGameController, 0, name, null, rawController, null);
                devices.Add(device);
                deviceComboBox.Items.Add(device);
            }

            for (uint deviceId = 0; deviceId < MaxDevices; deviceId++)
            {
                var caps = new JoyCaps();
                var result = JoyGetDevCaps(deviceId, ref caps, Marshal.SizeOf<JoyCaps>());

                if (result == 0 && TryGetJoyInfo(deviceId, out _))
                {
                    var name = string.IsNullOrWhiteSpace(caps.ProductName) ? $"Controller {deviceId}" : caps.ProductName;
                    var device = new DeviceInfo(InputSource.WinMm, deviceId, name, null, null, null);
                    devices.Add(device);
                    deviceComboBox.Items.Add(device);
                }
            }

            if (deviceComboBox.Items.Count > 0)
            {
                deviceComboBox.SelectedIndex = 0;
                startButton.Enabled = true;
                statusLabel.Text = $"{deviceComboBox.Items.Count - 1} controller(s) found";
            }
            else
            {
                startButton.Enabled = false;
                statusLabel.Text = "No controller found";
            }
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
        // Refreshボタン押下時にコントローラーを再検出する処理
        //-------------------------------------------------------------------------------
        private void RefreshButton_Click(object? sender, EventArgs e)
        {
            StopMapping();
            RefreshDevices();
        }

        //-------------------------------------------------------------------------------
        // Startボタン押下時にキー変換を開始する処理
        //-------------------------------------------------------------------------------
        private void StartButton_Click(object? sender, EventArgs e)
        {
            if (deviceComboBox.SelectedItem is not DeviceInfo)
            {
                AppendLog("Controller is not selected");
                return;
            }

            lock (inputLock)
            {
                activeInputs.Clear();
            }

            activeDevice = (DeviceInfo)deviceComboBox.SelectedItem;
            pollingCancellationTokenSource = new CancellationTokenSource();
            pollingTask = Task.Run(() => PollInputLoop(pollingCancellationTokenSource.Token));
            startButton.Enabled = false;
            stopButton.Enabled = true;
            refreshButton.Enabled = false;
            profileListBox.Enabled = false;
            profileRefreshButton.Enabled = false;
            profileNewButton.Enabled = false;
            profileSaveButton.Enabled = false;
            profileFolderButton.Enabled = false;
            focusSinkTextBox.Focus();
            statusLabel.Text = "Mapping started";
        }

        //-------------------------------------------------------------------------------
        // Stopボタン押下時にキー変換を停止する処理
        //-------------------------------------------------------------------------------
        private void StopButton_Click(object? sender, EventArgs e)
        {
            StopMapping();
        }

        //-------------------------------------------------------------------------------
        // 入力ログ出力設定を変更する処理
        //-------------------------------------------------------------------------------
        private void InputLogCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            enableInputLog = inputLogCheckBox.Checked;
        }

        //-------------------------------------------------------------------------------
        // Test Keyボタン押下時にキー送信だけを確認する処理
        //-------------------------------------------------------------------------------
        private void TestKeyButton_Click(object? sender, EventArgs e)
        {
            SendKeyboardInput(Keys.A, false, AppendLog);
            SendKeyboardInput(Keys.A, true, AppendLog);
            AppendLog("TestKey -> A");
        }

        //-------------------------------------------------------------------------------
        // 画面終了時に押下中のキーを解除する処理
        //-------------------------------------------------------------------------------
        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            StopMapping();
            DisposeSwitchHidControllers();
        }

        //-------------------------------------------------------------------------------
        // キー変換を停止する処理
        //-------------------------------------------------------------------------------
        private void StopMapping()
        {
            pollingCancellationTokenSource?.Cancel();
            pollingTask?.Wait(300);
            pollingCancellationTokenSource?.Dispose();
            pollingCancellationTokenSource = null;
            pollingTask = null;

            lock (inputLock)
            {
                foreach (var inputName in activeInputs.ToArray())
                {
                    ReleaseMappedInput(inputName);
                }

                activeInputs.Clear();
            }

            activeDevice = null;
            startButton.Enabled = deviceComboBox.Items.Count > 0;
            stopButton.Enabled = false;
            refreshButton.Enabled = true;
            profileListBox.Enabled = true;
            profileRefreshButton.Enabled = true;
            profileNewButton.Enabled = true;
            profileSaveButton.Enabled = true;
            profileFolderButton.Enabled = true;
            statusLabel.Text = "Mapping stopped";
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

                lock (inputLock)
                {
                    foreach (var inputName in currentInputs)
                    {
                        PressMappedInput(inputName);
                    }

                    foreach (var inputName in activeInputs.Except(currentInputs).ToArray())
                    {
                        ReleaseMappedInput(inputName);
                    }
                }

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
                    if (!TryGetPressedInputs(currentDevice, allDevices, out var currentInputs))
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
        // 割り当てられたキーを押下する処理
        //-------------------------------------------------------------------------------
        private void PressMappedInput(string inputName)
        {
            if (activeInputs.Contains(inputName) || !keyMappings.TryGetValue(inputName, out var keys) || keys.Count == 0)
            {
                return;
            }

            foreach (var key in keys)
            {
                SendKeyboardInput(key, false, AppendLog);
            }

            activeInputs.Add(inputName);

            if (enableInputLog)
            {
                AppendLog($"{inputName} -> {string.Join(" + ", keys)} Down");
            }
        }

        //-------------------------------------------------------------------------------
        // 割り当てられたキーを解除する処理
        //-------------------------------------------------------------------------------
        private void ReleaseMappedInput(string inputName)
        {
            if (!activeInputs.Contains(inputName) || !keyMappings.TryGetValue(inputName, out var keys))
            {
                return;
            }

            foreach (var key in keys.AsEnumerable().Reverse())
            {
                SendKeyboardInput(key, true, AppendLog);
            }

            activeInputs.Remove(inputName);

            if (enableInputLog)
            {
                AppendLog($"{inputName} -> {string.Join(" + ", keys)} Up");
            }
        }

        //-------------------------------------------------------------------------------
        // SendInputでキーボード入力を送信する処理
        //-------------------------------------------------------------------------------
        private static void SendKeyboardInput(Keys key, bool keyUp, Action<string>? logAction = null)
        {
            var input = new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = (ushort)key,
                        Scan = 0,
                        Flags = GetExtendedKeyFlag(key) | (keyUp ? KeyEventFKeyUp : 0),
                    },
                },
            };

            var inputSize = Marshal.SizeOf<Input>();
            var sentCount = SendInput(1, [input], inputSize);

            if (sentCount == 0)
            {
                logAction?.Invoke($"SendInput failed: key={key}, size={inputSize}, error={Marshal.GetLastWin32Error()}");
            }
        }

        //-------------------------------------------------------------------------------
        // 拡張キー用の送信フラグを取得する処理
        //-------------------------------------------------------------------------------
        private static uint GetExtendedKeyFlag(Keys key)
        {
            return key is Keys.Up or Keys.Down or Keys.Left or Keys.Right
                or Keys.Insert or Keys.Delete or Keys.Home or Keys.End
                or Keys.PageUp or Keys.PageDown or Keys.RControlKey or Keys.RMenu
                ? KeyEventFExtendedKey
                : 0;
        }

        //-------------------------------------------------------------------------------
        // 入力状態一覧を更新する処理
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
                item.SubItems.Add(string.Empty);
                stateListView.Items.Add(item);
            }

            foreach (ListViewItem item in stateListView.Items)
            {
                var isPressed = currentInputs.Contains(item.Text);
                item.BackColor = isPressed ? Color.Khaki : SystemColors.Window;
                item.ForeColor = SystemColors.WindowText;
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

        //-------------------------------------------------------------------------------
        // キーボード入力を送信するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

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

        private sealed record AppSettings(string ProfileDirectory);

        private sealed record ProfileInfo(string Name, string Path, bool IsJsonDefault)
        {
            public static readonly ProfileInfo JsonDefault = new("keymap.json", string.Empty, true);

            //-------------------------------------------------------------------------------
            // プロファイル表示名を返す処理
            //-------------------------------------------------------------------------------
            public override string ToString()
            {
                return Name;
            }
        }

        private sealed class MappingEditDialog : Form
        {
            private readonly Label keyLabel;
            private readonly Button okButton;
            private readonly Button clearButton;

            public List<Keys> SelectedKeys { get; private set; }

            //-------------------------------------------------------------------------------
            // キー割り当て編集画面を初期化する処理
            //-------------------------------------------------------------------------------
            public MappingEditDialog(string inputName, List<Keys> currentKeys)
            {
                Text = $"Edit {inputName}";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(360, 150);
                KeyPreview = true;
                SelectedKeys = currentKeys.ToList();

                var titleLabel = new Label
                {
                    Text = inputName,
                    Dock = DockStyle.Top,
                    Height = 32,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 0, 0, 0),
                };
                Controls.Add(titleLabel);

                keyLabel = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 48,
                    BorderStyle = BorderStyle.FixedSingle,
                    TextAlign = ContentAlignment.MiddleCenter,
                };
                Controls.Add(keyLabel);

                var buttonLayout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 44,
                    Padding = new Padding(8),
                };
                Controls.Add(buttonLayout);

                okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Width = 80,
                };
                buttonLayout.Controls.Add(okButton);

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Width = 80,
                };
                buttonLayout.Controls.Add(cancelButton);

                clearButton = new Button
                {
                    Text = "Clear",
                    Width = 80,
                };
                clearButton.Click += ClearButton_Click;
                buttonLayout.Controls.Add(clearButton);

                AcceptButton = okButton;
                CancelButton = cancelButton;
                KeyDown += MappingEditDialog_KeyDown;
                RefreshKeyLabel();
            }

            //-------------------------------------------------------------------------------
            // Clearボタン押下時にキー割り当てを削除する処理
            //-------------------------------------------------------------------------------
            private void ClearButton_Click(object? sender, EventArgs e)
            {
                SelectedKeys.Clear();
                RefreshKeyLabel();
            }

            //-------------------------------------------------------------------------------
            // キー押下時に割り当てキーを取り込む処理
            //-------------------------------------------------------------------------------
            private void MappingEditDialog_KeyDown(object? sender, KeyEventArgs e)
            {
                if (e.KeyCode is Keys.Enter or Keys.Escape)
                {
                    return;
                }

                var keys = new List<Keys>();

                if (e.Control)
                {
                    keys.Add(Keys.ControlKey);
                }

                if (e.Shift)
                {
                    keys.Add(Keys.ShiftKey);
                }

                if (e.Alt)
                {
                    keys.Add(Keys.Menu);
                }

                if (e.KeyCode is not Keys.ControlKey and not Keys.ShiftKey and not Keys.Menu)
                {
                    keys.Add(e.KeyCode);
                }

                SelectedKeys = keys.Distinct().Take(4).ToList();
                RefreshKeyLabel();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }

            //-------------------------------------------------------------------------------
            // 割り当てキー表示を更新する処理
            //-------------------------------------------------------------------------------
            private void RefreshKeyLabel()
            {
                keyLabel.Text = SelectedKeys.Count == 0
                    ? "No key assigned"
                    : string.Join(" + ", SelectedKeys);
            }
        }

        private sealed class ProfileNameDialog : Form
        {
            private readonly TextBox profileNameTextBox;

            public string ProfileName => SanitizeProfileName(profileNameTextBox.Text);

            //-------------------------------------------------------------------------------
            // プロファイル名入力画面を初期化する処理
            //-------------------------------------------------------------------------------
            public ProfileNameDialog()
            {
                Text = "New Profile";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(320, 120);

                var label = new Label
                {
                    Text = "Profile name",
                    Dock = DockStyle.Top,
                    Height = 28,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(12, 0, 0, 0),
                };
                Controls.Add(label);

                profileNameTextBox = new TextBox
                {
                    Dock = DockStyle.Top,
                    Margin = new Padding(12),
                    Text = $"NewProfile_{DateTime.Now:yyyyMMdd_HHmmss}",
                };
                Controls.Add(profileNameTextBox);

                var buttonLayout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 44,
                    Padding = new Padding(8),
                };
                Controls.Add(buttonLayout);

                var okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Width = 80,
                };
                buttonLayout.Controls.Add(okButton);

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Width = 80,
                };
                buttonLayout.Controls.Add(cancelButton);

                AcceptButton = okButton;
                CancelButton = cancelButton;
            }

            //-------------------------------------------------------------------------------
            // ファイル名として使えるプロファイル名へ変換する処理
            //-------------------------------------------------------------------------------
            private static string SanitizeProfileName(string profileName)
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var sanitizedName = new string(profileName.Select(x => invalidChars.Contains(x) ? '_' : x).ToArray()).Trim();
                return sanitizedName.Length == 0 ? "NewProfile" : sanitizedName;
            }
        }

        private sealed class DoubleBufferedListView : ListView
        {
            //-------------------------------------------------------------------------------
            // リストビューの描画ちらつきを抑える処理
            //-------------------------------------------------------------------------------
            public DoubleBufferedListView()
            {
                DoubleBuffered = true;
            }
        }

        private sealed class NonKeyboardListBox : ListBox
        {
            //-------------------------------------------------------------------------------
            // 送信キーでプロファイル選択が変わらないようキー操作を無視する処理
            //-------------------------------------------------------------------------------
            protected override void OnKeyDown(KeyEventArgs e)
            {
                e.Handled = true;
            }

            //-------------------------------------------------------------------------------
            // 送信キーでプロファイル選択が変わらないようキー入力を無視する処理
            //-------------------------------------------------------------------------------
            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                e.Handled = true;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KeyboardInput Keyboard;

            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint Message;
            public ushort ParamL;
            public ushort ParamH;
        }
    }
}
