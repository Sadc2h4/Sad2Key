namespace Sad2Key
{
    internal static class Program
    {
        //-------------------------------------------------------------------------------
        // アプリケーションを開始する処理
        //-------------------------------------------------------------------------------
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
