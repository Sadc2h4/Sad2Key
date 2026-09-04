using System.Runtime.InteropServices;

namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // タイトルバーやスクロールバーなどOS描画部分の配色を切り替えるクラス
    //-------------------------------------------------------------------------------
    internal static class NativeTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        //-------------------------------------------------------------------------------
        // ウィンドウのタイトルバー配色をテーマに合わせる処理（Windows 11では色指定，10では暗色フラグのみ）
        //-------------------------------------------------------------------------------
        public static void ApplyWindowTheme(Form form, Theme theme)
        {
            if (!form.IsHandleCreated)
            {
                return;
            }

            try
            {
                var darkMode = theme.IsDark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

                var captionColor = ToColorRef(theme.Background);
                var textColor = ToColorRef(theme.Text);
                var borderColor = ToColorRef(theme.Background);
                DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref captionColor, sizeof(uint));
                DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref textColor, sizeof(uint));
                DwmSetWindowAttribute(form.Handle, DwmwaBorderColor, ref borderColor, sizeof(uint));
            }
            catch
            {
                // 古いWindowsでは属性が無いため無視する
            }
        }

        //-------------------------------------------------------------------------------
        // スクロールバーなどのシステム描画部分を暗色／明色に切り替える処理
        //-------------------------------------------------------------------------------
        public static void ApplyControlTheme(Control control, Theme theme)
        {
            if (!control.IsHandleCreated)
            {
                return;
            }

            try
            {
                SetWindowTheme(control.Handle, theme.IsDark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch
            {
                // 未対応環境では無視する
            }
        }

        //-------------------------------------------------------------------------------
        // ColorをWin32のCOLORREF（0x00BBGGRR）へ変換する処理
        //-------------------------------------------------------------------------------
        private static uint ToColorRef(Color color)
        {
            return (uint)(color.R | (color.G << 8) | (color.B << 16));
        }

        //-------------------------------------------------------------------------------
        // ウィンドウ属性（int値）を設定するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        //-------------------------------------------------------------------------------
        // ウィンドウ属性（COLORREF値）を設定するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);

        //-------------------------------------------------------------------------------
        // コントロールの視覚テーマを指定するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
    }
}
