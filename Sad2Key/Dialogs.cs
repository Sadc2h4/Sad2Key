namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // テーマ付きダイアログの共通基底（背景色・タイトルバー配色・ボタン生成）
    //-------------------------------------------------------------------------------
    internal abstract class ThemedDialog : Form
    {
        protected Theme Theme { get; }

        //-------------------------------------------------------------------------------
        // ダイアログ共通の見た目を初期化する処理
        //-------------------------------------------------------------------------------
        protected ThemedDialog(Theme theme, string title)
        {
            Theme = theme;
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = theme.Background;
            ForeColor = theme.Text;
            Font = Theme.BodyFont;
        }

        //-------------------------------------------------------------------------------
        // ハンドル作成時にタイトルバー配色を合わせる処理
        //-------------------------------------------------------------------------------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyWindowTheme(this, Theme);
        }

        //-------------------------------------------------------------------------------
        // 論理ピクセルをDPIに合わせた実ピクセルへ変換する処理
        //-------------------------------------------------------------------------------
        protected int S(int value)
        {
            return ThemedDrawing.Scale(this, value);
        }

        //-------------------------------------------------------------------------------
        // ダイアログ用の文字ボタン（角丸）を作成する処理
        //-------------------------------------------------------------------------------
        protected TileButton CreateButton(string text, bool isAccent, EventHandler onClick)
        {
            var button = new TileButton
            {
                Theme = Theme,
                Title = text,
                IsOn = isAccent,
                Size = new Size(S(96), S(38)),
                Margin = new Padding(S(4), 0, S(4), 0),
            };
            button.Click += onClick;
            return button;
        }

        //-------------------------------------------------------------------------------
        // 説明用ラベルを作成する処理
        //-------------------------------------------------------------------------------
        protected Label CreateLabel(string text, bool isSecondary = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = isSecondary ? Theme.TextSecondary : Theme.Text,
                BackColor = Theme.Background,
                Font = isSecondary ? Theme.SmallFont : Theme.BodyFont,
            };
        }
    }

    //-------------------------------------------------------------------------------
    // キー割り当て編集画面（Hold / 短押し・長押しの切替，入力1〜3の取り込み）
    //-------------------------------------------------------------------------------
    internal sealed class MappingEditDialog : ThemedDialog
    {
        private const int EditableInputCount = 3;

        private readonly KeyMapping originalMapping;
        private readonly TileButton holdModeButton;
        private readonly TileButton shortLongModeButton;
        private readonly NumericUpDown thresholdUpDown;
        private readonly Label thresholdLabel;
        private readonly Label[] inputTitleLabels = new Label[EditableInputCount];
        private readonly TileButton[] inputKeyButtons = new TileButton[EditableInputCount];
        private readonly List<Keys>[] inputKeys = new List<Keys>[EditableInputCount];
        private readonly TextBox captureSinkTextBox;
        private readonly TileButton clearButton;
        private bool isShortLongMode;
        private int selectedInputIndex;

        public KeyMapping Result { get; private set; }

        //-------------------------------------------------------------------------------
        // キー割り当て編集画面を初期化する処理
        //-------------------------------------------------------------------------------
        public MappingEditDialog(string inputName, KeyMapping currentMapping, Theme theme)
            : base(theme, $"Edit {inputName}")
        {
            ClientSize = new Size(S(460), S(392));
            KeyPreview = true;
            originalMapping = currentMapping;
            Result = currentMapping;
            isShortLongMode = currentMapping.Kind == MappingKind.ShortLongPress;
            LoadInputKeys(currentMapping);
            var isEditable = currentMapping.Kind != MappingKind.Unsupported;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                Padding = new Padding(S(20), S(16), S(20), S(8)),
                BackColor = theme.Background,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(150)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(110)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(52)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(52)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(52)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(52)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(44)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var titleLabel = CreateLabel(inputName);
            titleLabel.Font = Theme.TitleFont;
            layout.SetColumnSpan(titleLabel, 3);
            layout.Controls.Add(titleLabel, 0, 0);

            var modePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = theme.Background,
                Margin = new Padding(0, S(4), 0, S(4)),
            };
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            modePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            holdModeButton = new TileButton { Theme = theme, Title = "Hold (while pressed)", Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(4), 0), Enabled = isEditable };
            holdModeButton.Click += (_, _) => SetMode(false);
            shortLongModeButton = new TileButton { Theme = theme, Title = "Short / Long press", Dock = DockStyle.Fill, Margin = new Padding(S(4), 0, 0, 0), Enabled = isEditable };
            shortLongModeButton.Click += (_, _) => SetMode(true);
            modePanel.Controls.Add(holdModeButton, 0, 0);
            modePanel.Controls.Add(shortLongModeButton, 1, 0);
            layout.SetColumnSpan(modePanel, 3);
            layout.Controls.Add(modePanel, 0, 1);

            thresholdLabel = CreateLabel("Threshold (ms)");
            layout.Controls.Add(thresholdLabel, 0, 2);
            thresholdUpDown = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 5000,
                Increment = 50,
                Width = S(110),
                Anchor = AnchorStyles.Left,
                Font = Theme.BodyFont,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Value = Math.Clamp(isShortLongMode ? currentMapping.ThresholdMilliseconds : 500, 50, 5000),
            };
            layout.Controls.Add(thresholdUpDown, 1, 2);

            for (var inputIndex = 0; inputIndex < EditableInputCount; inputIndex++)
            {
                var capturedIndex = inputIndex;
                inputTitleLabels[inputIndex] = CreateLabel(string.Empty);
                layout.Controls.Add(inputTitleLabels[inputIndex], 0, 3 + inputIndex);

                inputKeyButtons[inputIndex] = new TileButton
                {
                    Theme = theme,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, S(5), 0, S(5)),
                    Enabled = isEditable,
                };
                inputKeyButtons[inputIndex].Click += (_, _) => SelectInput(capturedIndex);
                layout.SetColumnSpan(inputKeyButtons[inputIndex], 2);
                layout.Controls.Add(inputKeyButtons[inputIndex], 1, 3 + inputIndex);
            }

            var noteLabel = CreateLabel(
                isEditable
                    ? "Click a box, then press keys (Ctrl / Shift / Alt can be combined)."
                    : "This mapping cannot be edited in Sad2Key. It is kept as-is when saving.",
                true);
            layout.SetColumnSpan(noteLabel, 3);
            layout.Controls.Add(noteLabel, 0, 6);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = S(58),
                Padding = new Padding(S(16), S(8), S(16), S(8)),
                BackColor = theme.Background,
            };
            Controls.Add(buttonPanel);
            buttonPanel.Controls.Add(CreateButton("OK", true, OkButton_Click));
            buttonPanel.Controls.Add(CreateButton("Cancel", false, (_, _) => DialogResult = DialogResult.Cancel));
            clearButton = CreateButton("Clear", false, ClearButton_Click);
            clearButton.Enabled = isEditable;
            buttonPanel.Controls.Add(clearButton);

            captureSinkTextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Location = new Point(-200, -200),
                Size = new Size(1, 1),
                TabStop = false,
            };
            Controls.Add(captureSinkTextBox);

            KeyDown += MappingEditDialog_KeyDown;
            RefreshModeState();
            SelectInput(0);
        }

        //-------------------------------------------------------------------------------
        // Escapeでキャンセルする処理（CancelButtonはTileButtonのため自前で扱う）
        //-------------------------------------------------------------------------------
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        //-------------------------------------------------------------------------------
        // 既存の割り当てから編集用の入力1〜3を取り込む処理
        //-------------------------------------------------------------------------------
        private void LoadInputKeys(KeyMapping currentMapping)
        {
            for (var inputIndex = 0; inputIndex < EditableInputCount; inputIndex++)
            {
                inputKeys[inputIndex] = currentMapping.Kind switch
                {
                    MappingKind.Hold => inputIndex == 0 ? currentMapping.Keys.ToList() : [],
                    MappingKind.ShortLongPress or MappingKind.MultiKeyOther => currentMapping.Inputs[inputIndex].ToList(),
                    _ => [],
                };
            }
        }

        //-------------------------------------------------------------------------------
        // モードを切り替える処理
        //-------------------------------------------------------------------------------
        private void SetMode(bool shortLong)
        {
            isShortLongMode = shortLong;
            RefreshModeState();

            if (!isShortLongMode && selectedInputIndex != 0)
            {
                SelectInput(0);
            }
            else
            {
                captureSinkTextBox.Focus();
            }
        }

        //-------------------------------------------------------------------------------
        // 現在のモードに合わせて画面部品の状態を更新する処理
        //-------------------------------------------------------------------------------
        private void RefreshModeState()
        {
            var isEditable = originalMapping.Kind != MappingKind.Unsupported;
            holdModeButton.IsOn = !isShortLongMode;
            shortLongModeButton.IsOn = isShortLongMode;
            thresholdUpDown.Enabled = isShortLongMode && isEditable;
            thresholdLabel.ForeColor = thresholdUpDown.Enabled ? Theme.Text : Theme.TextDisabled;
            inputTitleLabels[0].Text = isShortLongMode ? "Input 1 (short)" : "Keys";
            inputTitleLabels[1].Text = "Input 2 (long)";
            inputTitleLabels[2].Text = "Input 3 (after long)";

            for (var inputIndex = 0; inputIndex < EditableInputCount; inputIndex++)
            {
                var isEnabled = (inputIndex == 0 || isShortLongMode) && isEditable;
                inputTitleLabels[inputIndex].ForeColor = isEnabled ? Theme.Text : Theme.TextDisabled;
                inputKeyButtons[inputIndex].Enabled = isEnabled;
                RefreshKeyButton(inputIndex);
            }
        }

        //-------------------------------------------------------------------------------
        // キー取り込み先の入力欄を選択する処理
        //-------------------------------------------------------------------------------
        private void SelectInput(int inputIndex)
        {
            if (!inputKeyButtons[inputIndex].Enabled)
            {
                return;
            }

            selectedInputIndex = inputIndex;

            for (var index = 0; index < EditableInputCount; index++)
            {
                inputKeyButtons[index].IsOn = index == selectedInputIndex;
            }

            captureSinkTextBox.Focus();                                     // 取り込み用の隠し欄へフォーカスを移す
        }

        //-------------------------------------------------------------------------------
        // Clearボタン押下時に選択中の入力欄を空にする処理
        //-------------------------------------------------------------------------------
        private void ClearButton_Click(object? sender, EventArgs e)
        {
            inputKeys[selectedInputIndex].Clear();
            RefreshKeyButton(selectedInputIndex);
            captureSinkTextBox.Focus();
        }

        //-------------------------------------------------------------------------------
        // OKボタン押下時に編集内容から割り当てを作成する処理
        //-------------------------------------------------------------------------------
        private void OkButton_Click(object? sender, EventArgs e)
        {
            if (originalMapping.Kind == MappingKind.Unsupported)
            {
                Result = originalMapping;
            }
            else if (isShortLongMode)
            {
                Result = KeyMapping.CreateShortLongPress((int)thresholdUpDown.Value, inputKeys[0], inputKeys[1], inputKeys[2]);
            }
            else
            {
                Result = KeyMapping.CreateHold(inputKeys[0]);
            }

            DialogResult = DialogResult.OK;
        }

        //-------------------------------------------------------------------------------
        // キー押下時に選択中の入力欄へ割り当てキーを取り込む処理
        //-------------------------------------------------------------------------------
        private void MappingEditDialog_KeyDown(object? sender, KeyEventArgs e)
        {
            if (ActiveControl != captureSinkTextBox || originalMapping.Kind == MappingKind.Unsupported)
            {
                return;                                                     // 取り込み欄以外にフォーカスがあるときは通常操作
            }

            if (e.KeyCode is Keys.Escape)
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

            inputKeys[selectedInputIndex] = keys.Distinct().Take(KeyMapping.MaxKeysPerInput).ToList();
            RefreshKeyButton(selectedInputIndex);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        //-------------------------------------------------------------------------------
        // 入力欄の割り当てキー表示を更新する処理
        //-------------------------------------------------------------------------------
        private void RefreshKeyButton(int inputIndex)
        {
            inputKeyButtons[inputIndex].Title = inputKeys[inputIndex].Count == 0
                ? "No key assigned"
                : KeyMapping.DescribeKeys(inputKeys[inputIndex]);
        }
    }

    //-------------------------------------------------------------------------------
    // 新規プロファイル名の入力画面
    //-------------------------------------------------------------------------------
    internal sealed class ProfileNameDialog : ThemedDialog
    {
        private readonly TextBox profileNameTextBox;

        public string ProfileName => SanitizeProfileName(profileNameTextBox.Text);

        //-------------------------------------------------------------------------------
        // プロファイル名入力画面を初期化する処理
        //-------------------------------------------------------------------------------
        public ProfileNameDialog(Theme theme)
            : base(theme, "New Profile")
        {
            ClientSize = new Size(S(400), S(168));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(S(20), S(16), S(20), S(8)),
                BackColor = theme.Background,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(40)));
            Controls.Add(layout);

            var label = CreateLabel("Profile name");
            layout.Controls.Add(label, 0, 0);

            profileNameTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Font = Theme.BodyFont,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Text = $"NewProfile_{DateTime.Now:yyyyMMdd_HHmmss}",
            };
            layout.Controls.Add(profileNameTextBox, 0, 1);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = S(58),
                Padding = new Padding(S(16), S(8), S(16), S(8)),
                BackColor = theme.Background,
            };
            Controls.Add(buttonPanel);
            buttonPanel.Controls.Add(CreateButton("OK", true, (_, _) => DialogResult = DialogResult.OK));
            buttonPanel.Controls.Add(CreateButton("Cancel", false, (_, _) => DialogResult = DialogResult.Cancel));
        }

        //-------------------------------------------------------------------------------
        // EnterでOK，Escapeでキャンセルする処理
        //-------------------------------------------------------------------------------
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                DialogResult = DialogResult.OK;
                return true;
            }

            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }

            return base.ProcessDialogKey(keyData);
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
}
