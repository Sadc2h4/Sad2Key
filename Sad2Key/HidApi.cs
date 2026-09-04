using System.Reflection;
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
        // hidapi.dllの探索先を登録する処理（単一exe配布時は埋め込みリソースから展開する）
        //-------------------------------------------------------------------------------
        static HidApi()
        {
            NativeLibrary.SetDllImportResolver(typeof(HidApi).Assembly, ResolveNativeLibrary);
        }

        //-------------------------------------------------------------------------------
        // hidapi.dllをexeフォルダ→展開済みフォルダの順に探して読み込む処理
        //-------------------------------------------------------------------------------
        private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!libraryName.Equals(DllName, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            foreach (var candidatePath in EnumerateCandidatePaths())
            {
                if (File.Exists(candidatePath) && NativeLibrary.TryLoad(candidatePath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;                                             // 見つからなければ既定の探索に任せる
        }

        //-------------------------------------------------------------------------------
        // hidapi.dllの候補パスを列挙する処理
        //-------------------------------------------------------------------------------
        private static IEnumerable<string> EnumerateCandidatePaths()
        {
            var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath);

            if (!string.IsNullOrEmpty(executableDirectory))
            {
                yield return Path.Combine(executableDirectory, DllName);
            }

            yield return Path.Combine(AppContext.BaseDirectory, DllName);

            var extractedPath = TryExtractEmbeddedLibrary();

            if (extractedPath is not null)
            {
                yield return extractedPath;
            }
        }

        //-------------------------------------------------------------------------------
        // 埋め込みリソースのhidapi.dllをローカルフォルダへ展開しパスを返す処理
        //-------------------------------------------------------------------------------
        private static string? TryExtractEmbeddedLibrary()
        {
            try
            {
                using var resourceStream = typeof(HidApi).Assembly.GetManifestResourceStream(DllName);

                if (resourceStream is null)
                {
                    return null;
                }

                var extractDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Sad2Key",
                    "native");
                Directory.CreateDirectory(extractDirectory);
                var extractedPath = Path.Combine(extractDirectory, DllName);

                if (!File.Exists(extractedPath) || new FileInfo(extractedPath).Length != resourceStream.Length)
                {
                    using var fileStream = File.Create(extractedPath);
                    resourceStream.CopyTo(fileStream);                      // サイズが違うときだけ書き直す
                }

                return extractedPath;
            }
            catch
            {
                return null;
            }
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
