using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // 角丸描画などテーマ付きコントロール共通の補助処理
    //-------------------------------------------------------------------------------
    internal static class ThemedDrawing
    {
        //-------------------------------------------------------------------------------
        // 角丸矩形のパスを作成する処理
        //-------------------------------------------------------------------------------
        public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        //-------------------------------------------------------------------------------
        // 高品質描画の設定をまとめて行う処理
        //-------------------------------------------------------------------------------
        public static void PrepareGraphics(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        //-------------------------------------------------------------------------------
        // 親の背景色で塗りつぶす処理（角丸の外側を親と同じ色にする）
        //-------------------------------------------------------------------------------
        public static void FillParentBackground(Graphics graphics, Control control)
        {
            var parentColor = control.Parent?.BackColor ?? control.BackColor;
            graphics.Clear(parentColor);
        }

        //-------------------------------------------------------------------------------
        // 論理ピクセルをDPIに合わせた実ピクセルへ変換する処理
        //-------------------------------------------------------------------------------
        public static int Scale(Control control, int value)
        {
            return (int)Math.Round(value * control.DeviceDpi / 96f);
        }
    }

    //-------------------------------------------------------------------------------
    // 参考UIの「タイル」を表すボタン（アイコン＋タイトル＋状態テキスト＋任意の右矢印）
    // IsOn=trueでアクセント色，falseで面色になる
    //-------------------------------------------------------------------------------
    internal sealed class TileButton : Control
    {
        private Theme theme = Theme.Dark;
        private string glyph = string.Empty;
        private string title = string.Empty;
        private string subtitle = string.Empty;
        private bool showChevron;
        private bool isOn;
        private bool interactive = true;
        private bool isHovered;
        private bool isPressed;

        //-------------------------------------------------------------------------------
        // タイルボタンを初期化する処理
        //-------------------------------------------------------------------------------
        public TileButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public Theme Theme { get => theme; set { theme = value; Invalidate(); } }
        public string Glyph { get => glyph; set { glyph = value; Invalidate(); } }
        public string Title { get => title; set { title = value; Invalidate(); } }
        public string Subtitle { get => subtitle; set { subtitle = value; Invalidate(); } }
        public bool ShowChevron { get => showChevron; set { showChevron = value; Invalidate(); } }
        public bool IsOn { get => isOn; set { isOn = value; Invalidate(); } }

        //-------------------------------------------------------------------------------
        // クリック可能かどうか（表示専用タイルではホバー効果を出さない）
        //-------------------------------------------------------------------------------
        public bool Interactive
        {
            get => interactive;
            set
            {
                interactive = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                TabStop = value;
                Invalidate();
            }
        }

        //-------------------------------------------------------------------------------
        // タイルを描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            ThemedDrawing.PrepareGraphics(graphics);
            ThemedDrawing.FillParentBackground(graphics, this);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            var radius = Math.Min(ThemedDrawing.Scale(this, 28), Height / 2);
            using var path = ThemedDrawing.CreateRoundedRectangle(bounds, radius);
            using var fillBrush = new SolidBrush(GetFillColor());
            graphics.FillPath(fillBrush, path);

            if (Focused && ShowFocusCues && interactive)
            {
                using var focusPen = new Pen(isOn ? theme.OnAccent : theme.Accent, 1.5f);
                graphics.DrawPath(focusPen, path);
            }

            var textColor = !Enabled ? theme.TextDisabled : isOn ? theme.OnAccent : theme.Text;
            var secondaryColor = !Enabled ? theme.TextDisabled : isOn ? theme.OnAccentSecondary : theme.TextSecondary;
            var padding = ThemedDrawing.Scale(this, 22);
            var hasGlyph = glyph.Length > 0;
            var hasSubtitle = subtitle.Length > 0;
            var textLeft = padding;

            if (hasGlyph)
            {
                var glyphSize = TextRenderer.MeasureText(graphics, glyph, Theme.LargeGlyphFont, Size.Empty, TextFormatFlags.NoPadding);
                var glyphBounds = new Rectangle(padding, (Height - glyphSize.Height) / 2, glyphSize.Width, glyphSize.Height);
                TextRenderer.DrawText(graphics, glyph, Theme.LargeGlyphFont, glyphBounds, textColor, TextFormatFlags.NoPadding);
                textLeft = padding + glyphSize.Width + ThemedDrawing.Scale(this, 18);
            }

            var textRight = Width - padding;

            if (showChevron)
            {
                var chevronSize = TextRenderer.MeasureText(graphics, Glyphs.ChevronRight, Theme.GlyphFont, Size.Empty, TextFormatFlags.NoPadding);
                var chevronBounds = new Rectangle(Width - padding - chevronSize.Width, (Height - chevronSize.Height) / 2, chevronSize.Width, chevronSize.Height);
                TextRenderer.DrawText(graphics, Glyphs.ChevronRight, Theme.GlyphFont, chevronBounds, secondaryColor, TextFormatFlags.NoPadding);
                textRight = chevronBounds.Left - ThemedDrawing.Scale(this, 12);
            }

            var textFlags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

            if (!hasGlyph && !hasSubtitle)
            {
                TextRenderer.DrawText(graphics, title, Theme.TitleFont, bounds, textColor, textFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var titleHeight = TextRenderer.MeasureText(graphics, "Ag", Theme.TitleFont, Size.Empty, TextFormatFlags.NoPadding).Height;
            var subtitleHeight = hasSubtitle ? TextRenderer.MeasureText(graphics, "Ag", Theme.BodyFont, Size.Empty, TextFormatFlags.NoPadding).Height : 0;
            var lineGap = hasSubtitle ? ThemedDrawing.Scale(this, 2) : 0;
            var blockTop = (Height - titleHeight - lineGap - subtitleHeight) / 2;
            var titleBounds = new Rectangle(textLeft, blockTop, Math.Max(1, textRight - textLeft), titleHeight);
            TextRenderer.DrawText(graphics, title, Theme.TitleFont, titleBounds, textColor, textFlags);

            if (hasSubtitle)
            {
                var subtitleBounds = new Rectangle(textLeft, blockTop + titleHeight + lineGap, Math.Max(1, textRight - textLeft), subtitleHeight);
                TextRenderer.DrawText(graphics, subtitle, Theme.BodyFont, subtitleBounds, secondaryColor, textFlags);
            }
        }

        //-------------------------------------------------------------------------------
        // 状態（ON/OFF・ホバー・押下・無効）に応じた塗り色を取得する処理
        //-------------------------------------------------------------------------------
        private Color GetFillColor()
        {
            if (!Enabled)
            {
                return isOn ? Blend(theme.Accent, theme.Background, 0.45f) : theme.Surface;
            }

            if (!interactive)
            {
                return isOn ? theme.Accent : theme.Surface;
            }

            if (isPressed)
            {
                return isOn ? theme.AccentPressed : theme.SurfacePressed;
            }

            if (isHovered)
            {
                return isOn ? theme.AccentHover : theme.SurfaceHover;
            }

            return isOn ? theme.Accent : theme.Surface;
        }

        //-------------------------------------------------------------------------------
        // 2色を指定比率で混ぜる処理
        //-------------------------------------------------------------------------------
        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        //-------------------------------------------------------------------------------
        // マウス押下時に押下表示へ切り替える処理
        //-------------------------------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (interactive && e.Button == MouseButtons.Left)
            {
                isPressed = true;
                Focus();
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        //-------------------------------------------------------------------------------
        // マウス解放時に押下表示を戻す処理
        //-------------------------------------------------------------------------------
        protected override void OnMouseUp(MouseEventArgs e)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        //-------------------------------------------------------------------------------
        // 表示専用タイルではクリックイベントを発生させない処理
        //-------------------------------------------------------------------------------
        protected override void OnClick(EventArgs e)
        {
            if (interactive && Enabled)
            {
                base.OnClick(e);
            }
        }

        //-------------------------------------------------------------------------------
        // SpaceまたはEnterでクリック扱いにする処理
        //-------------------------------------------------------------------------------
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }
    }

    //-------------------------------------------------------------------------------
    // 画面右下などに置く丸ボタン（アイコンのみ）
    //-------------------------------------------------------------------------------
    internal sealed class RoundButton : Control
    {
        private Theme theme = Theme.Dark;
        private string glyph = string.Empty;
        private bool isAccent;
        private bool isHovered;
        private bool isPressed;

        //-------------------------------------------------------------------------------
        // 丸ボタンを初期化する処理
        //-------------------------------------------------------------------------------
        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public Theme Theme { get => theme; set { theme = value; Invalidate(); } }
        public string Glyph { get => glyph; set { glyph = value; Invalidate(); } }
        public bool IsAccent { get => isAccent; set { isAccent = value; Invalidate(); } }
        public string ToolTipText { get; set; } = string.Empty;

        //-------------------------------------------------------------------------------
        // 丸ボタンを描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            ThemedDrawing.PrepareGraphics(graphics);
            ThemedDrawing.FillParentBackground(graphics, this);

            var diameter = Math.Min(Width, Height) - 1;
            var bounds = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
            var fillColor = !Enabled
                ? theme.Surface
                : isPressed ? (isAccent ? theme.AccentPressed : theme.SurfacePressed)
                : isHovered ? (isAccent ? theme.AccentHover : theme.SurfaceHover)
                : isAccent ? theme.Accent : theme.Surface;
            using var fillBrush = new SolidBrush(fillColor);
            graphics.FillEllipse(fillBrush, bounds);

            if (Focused && ShowFocusCues)
            {
                using var focusPen = new Pen(isAccent ? theme.OnAccent : theme.Accent, 1.5f);
                graphics.DrawEllipse(focusPen, bounds);
            }

            var textColor = !Enabled ? theme.TextDisabled : isAccent ? theme.OnAccent : theme.Text;
            TextRenderer.DrawText(graphics, glyph, Theme.LargeGlyphFont, bounds, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { isPressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        //-------------------------------------------------------------------------------
        // SpaceまたはEnterでクリック扱いにする処理
        //-------------------------------------------------------------------------------
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }
    }

    //-------------------------------------------------------------------------------
    // 角丸の面（一覧やログの入れ物）
    //-------------------------------------------------------------------------------
    internal sealed class RoundedPanel : Panel
    {
        private Theme theme = Theme.Dark;
        private int radius = 20;

        //-------------------------------------------------------------------------------
        // 角丸パネルを初期化する処理
        //-------------------------------------------------------------------------------
        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public Theme Theme
        {
            get => theme;
            set
            {
                theme = value;
                BackColor = value.Surface;                                  // 子コントロールの背景基準色
                Invalidate();
            }
        }

        public int Radius { get => radius; set { radius = value; Invalidate(); } }

        //-------------------------------------------------------------------------------
        // 角丸の面を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            ThemedDrawing.PrepareGraphics(graphics);
            ThemedDrawing.FillParentBackground(graphics, this);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = ThemedDrawing.CreateRoundedRectangle(bounds, ThemedDrawing.Scale(this, radius));
            using var fillBrush = new SolidBrush(theme.Surface);
            graphics.FillPath(fillBrush, path);
        }
    }

    //-------------------------------------------------------------------------------
    // テーマ配色で自前描画するリストビュー（ヘッダー・行を描画，押下行は行の色で表現）
    //-------------------------------------------------------------------------------
    internal sealed class ThemedListView : ListView
    {
        private Theme theme = Theme.Dark;

        //-------------------------------------------------------------------------------
        // テーマ付きリストビューを初期化する処理
        //-------------------------------------------------------------------------------
        public ThemedListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            BorderStyle = BorderStyle.None;
            GridLines = false;
            MultiSelect = false;
            HideSelection = false;
            ShowItemToolTips = true;
            SmallImageList = new ImageList { ImageSize = new Size(1, 30) };    // 行の高さを確保する
            DrawColumnHeader += ThemedListView_DrawColumnHeader;
            DrawSubItem += ThemedListView_DrawSubItem;
            DrawItem += (_, e) => e.DrawDefault = false;
        }

        public Theme Theme
        {
            get => theme;
            set
            {
                theme = value;
                BackColor = value.Surface;
                ForeColor = value.Text;
                Invalidate();
            }
        }

        //-------------------------------------------------------------------------------
        // 列ヘッダーを描画する処理
        //-------------------------------------------------------------------------------
        private void ThemedListView_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            using var backBrush = new SolidBrush(theme.ListHeader);
            e.Graphics.FillRectangle(backBrush, e.Bounds);

            using var linePen = new Pen(theme.Border);
            e.Graphics.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            var textBounds = Rectangle.Inflate(e.Bounds, -ThemedDrawing.Scale(this, 10), 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, Theme.SmallFont, textBounds, theme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        //-------------------------------------------------------------------------------
        // 行のセルを描画する処理（BackColor/ForeColorはUpdateStateViewが設定する）
        //-------------------------------------------------------------------------------
        private void ThemedListView_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item is null)
            {
                return;
            }

            var isPressedRow = e.Item.BackColor == theme.Accent;
            var backColor = isPressedRow ? theme.Accent : e.Item.Selected ? theme.SurfaceHover : theme.Surface;
            var textColor = isPressedRow ? theme.OnAccent : e.ColumnIndex == 0 ? theme.Text : theme.TextSecondary;

            using var backBrush = new SolidBrush(backColor);
            e.Graphics.FillRectangle(backBrush, e.Bounds);

            var textBounds = Rectangle.Inflate(e.Bounds, -ThemedDrawing.Scale(this, 10), 0);
            var font = e.ColumnIndex == 0 ? Theme.TitleFont : Theme.BodyFont;
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, font, textBounds, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        //-------------------------------------------------------------------------------
        // ハンドル作成時にスクロールバーの配色を合わせる処理
        //-------------------------------------------------------------------------------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyControlTheme(this, theme);
        }
    }

    //-------------------------------------------------------------------------------
    // テーマ配色でメニューを描画するレンダラー
    //-------------------------------------------------------------------------------
    internal sealed class ThemedMenuRenderer : ToolStripRenderer
    {
        private readonly Theme theme;

        //-------------------------------------------------------------------------------
        // メニュー描画クラスを初期化する処理
        //-------------------------------------------------------------------------------
        public ThemedMenuRenderer(Theme theme)
        {
            this.theme = theme;
        }

        //-------------------------------------------------------------------------------
        // メニュー背景を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var backBrush = new SolidBrush(theme.Surface);
            e.Graphics.FillRectangle(backBrush, e.AffectedBounds);
        }

        //-------------------------------------------------------------------------------
        // メニュー枠を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var borderPen = new Pen(theme.Border);
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(borderPen, bounds);
        }

        //-------------------------------------------------------------------------------
        // メニュー項目の背景（ホバー時）を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            using var backBrush = new SolidBrush(e.Item.Selected && e.Item.Enabled ? theme.SurfaceHover : theme.Surface);
            ThemedDrawing.PrepareGraphics(e.Graphics);
            using var path = ThemedDrawing.CreateRoundedRectangle(bounds, 6);
            e.Graphics.FillPath(backBrush, path);
        }

        //-------------------------------------------------------------------------------
        // メニュー項目の文字を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? theme.Text : theme.TextDisabled;
            e.TextFont = Theme.BodyFont;
            base.OnRenderItemText(e);
        }

        //-------------------------------------------------------------------------------
        // 選択中項目のチェック表示をアクセント色の丸で描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            ThemedDrawing.PrepareGraphics(e.Graphics);
            var size = 8;
            var bounds = new Rectangle(e.ImageRectangle.Left + (e.ImageRectangle.Width - size) / 2, e.ImageRectangle.Top + (e.ImageRectangle.Height - size) / 2, size, size);
            using var accentBrush = new SolidBrush(theme.Accent);
            e.Graphics.FillEllipse(accentBrush, bounds);
        }

        //-------------------------------------------------------------------------------
        // 区切り線を描画する処理
        //-------------------------------------------------------------------------------
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var linePen = new Pen(theme.Border);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(linePen, 12, y, e.Item.Width - 12, y);
        }

        //-------------------------------------------------------------------------------
        // 画像余白部分を描画する処理（背景と同色）
        //-------------------------------------------------------------------------------
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var backBrush = new SolidBrush(theme.Surface);
            e.Graphics.FillRectangle(backBrush, e.AffectedBounds);
        }
    }

    //-------------------------------------------------------------------------------
    // テーマ配色のコンテキストメニューを作成する補助処理
    //-------------------------------------------------------------------------------
    internal static class ThemedMenu
    {
        //-------------------------------------------------------------------------------
        // テーマ配色を適用したメニューを作成する処理
        //-------------------------------------------------------------------------------
        public static ContextMenuStrip Create(Theme theme)
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = true,
                ShowCheckMargin = false,
                Font = Theme.BodyFont,
                BackColor = theme.Surface,
                ForeColor = theme.Text,
                Padding = new Padding(4, 6, 4, 6),
            };
            Apply(menu, theme);
            return menu;
        }

        //-------------------------------------------------------------------------------
        // 既存メニューへテーマ配色を適用する処理
        //-------------------------------------------------------------------------------
        public static void Apply(ContextMenuStrip menu, Theme theme)
        {
            menu.Renderer = new ThemedMenuRenderer(theme);
            menu.BackColor = theme.Surface;
            menu.ForeColor = theme.Text;
        }

        //-------------------------------------------------------------------------------
        // メニュー項目を作成する処理
        //-------------------------------------------------------------------------------
        public static ToolStripMenuItem CreateItem(string text, EventHandler onClick, bool isChecked = false, bool isEnabled = true)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = isChecked,
                Enabled = isEnabled,
                Padding = new Padding(6, 6, 6, 6),
            };
            item.Click += onClick;
            return item;
        }
    }
}
