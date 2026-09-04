namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // 配色モード
    //-------------------------------------------------------------------------------
    internal enum ThemeMode
    {
        Dark,
        Light,
    }

    //-------------------------------------------------------------------------------
    // 画面全体の配色とフォントを定義するクラス
    // Dark: 黒ベース＋水色アクセント，Light: 白ベース＋#4ECCA8アクセント
    //-------------------------------------------------------------------------------
    internal sealed class Theme
    {
        public ThemeMode Mode { get; private init; }
        public Color Background { get; private init; }          // ウィンドウ背景
        public Color Surface { get; private init; }             // OFFタイル・パネル
        public Color SurfaceHover { get; private init; }        // タイルのホバー
        public Color SurfacePressed { get; private init; }      // タイルの押下中
        public Color Accent { get; private init; }              // ONタイル・実行中表示
        public Color AccentHover { get; private init; }
        public Color AccentPressed { get; private init; }
        public Color OnAccent { get; private init; }            // アクセント上の文字
        public Color OnAccentSecondary { get; private init; }   // アクセント上の補助文字
        public Color Text { get; private init; }
        public Color TextSecondary { get; private init; }
        public Color TextDisabled { get; private init; }
        public Color Border { get; private init; }
        public Color ListHeader { get; private init; }
        public Color InputBackground { get; private init; }     // テキスト入力欄

        public static readonly Theme Dark = new()
        {
            Mode = ThemeMode.Dark,
            Background = ColorTranslator.FromHtml("#121416"),
            Surface = ColorTranslator.FromHtml("#2A2E31"),
            SurfaceHover = ColorTranslator.FromHtml("#35393D"),
            SurfacePressed = ColorTranslator.FromHtml("#3F4448"),
            Accent = ColorTranslator.FromHtml("#C2E7FF"),
            AccentHover = ColorTranslator.FromHtml("#D3EEFF"),
            AccentPressed = ColorTranslator.FromHtml("#A9DBFB"),
            OnAccent = ColorTranslator.FromHtml("#0B1E2E"),
            OnAccentSecondary = ColorTranslator.FromHtml("#2F4A5E"),
            Text = ColorTranslator.FromHtml("#F2F4F5"),
            TextSecondary = ColorTranslator.FromHtml("#B3BAC0"),
            TextDisabled = ColorTranslator.FromHtml("#6C7479"),
            Border = ColorTranslator.FromHtml("#3A3F43"),
            ListHeader = ColorTranslator.FromHtml("#1F2225"),
            InputBackground = ColorTranslator.FromHtml("#1B1E21"),
        };

        public static readonly Theme Light = new()
        {
            Mode = ThemeMode.Light,
            Background = ColorTranslator.FromHtml("#FFFFFF"),
            Surface = ColorTranslator.FromHtml("#F1F3F4"),
            SurfaceHover = ColorTranslator.FromHtml("#E6E9EB"),
            SurfacePressed = ColorTranslator.FromHtml("#DCE0E3"),
            Accent = ColorTranslator.FromHtml("#4ECCA8"),
            AccentHover = ColorTranslator.FromHtml("#62D4B3"),
            AccentPressed = ColorTranslator.FromHtml("#3FBF9A"),
            OnAccent = ColorTranslator.FromHtml("#0B2F27"),
            OnAccentSecondary = ColorTranslator.FromHtml("#1E5A4C"),
            Text = ColorTranslator.FromHtml("#1F2328"),
            TextSecondary = ColorTranslator.FromHtml("#5F6B72"),
            TextDisabled = ColorTranslator.FromHtml("#A3ACB2"),
            Border = ColorTranslator.FromHtml("#DDE1E4"),
            ListHeader = ColorTranslator.FromHtml("#F7F8F9"),
            InputBackground = ColorTranslator.FromHtml("#FFFFFF"),
        };

        public static readonly Font BodyFont = new("Segoe UI", 10f);
        public static readonly Font TitleFont = new("Segoe UI Semibold", 10.5f);
        public static readonly Font SmallFont = new("Segoe UI", 9f);
        public static readonly Font MonoFont = new("Consolas", 9.5f);
        public static readonly Font GlyphFont = new("Segoe MDL2 Assets", 14f);
        public static readonly Font LargeGlyphFont = new("Segoe MDL2 Assets", 16f);

        //-------------------------------------------------------------------------------
        // モードから配色を取得する処理
        //-------------------------------------------------------------------------------
        public static Theme FromMode(ThemeMode mode)
        {
            return mode == ThemeMode.Light ? Light : Dark;
        }

        //-------------------------------------------------------------------------------
        // ダークモードか判定する処理
        //-------------------------------------------------------------------------------
        public bool IsDark => Mode == ThemeMode.Dark;
    }

    //-------------------------------------------------------------------------------
    // Segoe MDL2 Assetsのアイコン文字
    //-------------------------------------------------------------------------------
    internal static class Glyphs
    {
        public const string Game = "\uE7FC";
        public const string Document = "\uE8A5";
        public const string Rotate = "\uE7AD";
        public const string Log = "\uE756";
        public const string Keyboard = "\uE765";
        public const string Sun = "\uE706";
        public const string Moon = "\uE708";
        public const string Folder = "\uE8B7";
        public const string Save = "\uE74E";
        public const string Power = "\uE7E8";
        public const string Refresh = "\uE72C";
        public const string ChevronRight = "\uE76C";
        public const string ChevronDown = "\uE70D";
        public const string ChevronUp = "\uE70E";
        public const string Add = "\uE710";
        public const string Link = "\uE71B";
        public const string Play = "\uE768";
        public const string Stop = "\uE71A";
        public const string Check = "\uE73E";
    }
}
