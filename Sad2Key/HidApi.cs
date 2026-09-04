using System.Runtime.InteropServices;

namespace Sad2Key
{
    internal static class HidApi
    {
        private const string DllName = "hidapi.dll";

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidDeviceInfo
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string Path;
            public ushort VendorId;
            public ushort ProductId;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string SerialNumber;
            public ushort ReleaseNumber;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ManufacturerString;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ProductString;
            public ushort UsagePage;
            public ushort Usage;
            public int InterfaceNumber;
            public IntPtr Next;
        }

        //-------------------------------------------------------------------------------
        // hidapiライブラリを初期化する処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_init();

        //-------------------------------------------------------------------------------
        // HIDデバイスを列挙する処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hid_enumerate(ushort vendorId, ushort productId);

        //-------------------------------------------------------------------------------
        // HIDデバイス列挙結果を解放する処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void hid_free_enumeration(IntPtr deviceInfo);

        //-------------------------------------------------------------------------------
        // HIDデバイスをパス指定で開く処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr hid_open_path([MarshalAs(UnmanagedType.LPStr)] string path);

        //-------------------------------------------------------------------------------
        // HIDデバイスへデータを書き込む処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_write(IntPtr device, byte[] data, UIntPtr length);

        //-------------------------------------------------------------------------------
        // HIDデバイスからタイムアウト付きでデータを読み込む処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_read_timeout(IntPtr device, byte[] data, UIntPtr length, int milliseconds);

        //-------------------------------------------------------------------------------
        // HIDデバイスのブロッキング設定を変更する処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int hid_set_nonblocking(IntPtr device, int nonblock);

        //-------------------------------------------------------------------------------
        // HIDデバイスを閉じる処理
        //-------------------------------------------------------------------------------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void hid_close(IntPtr device);
    }
}
