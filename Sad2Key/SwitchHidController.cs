using System.Runtime.InteropServices;

namespace Sad2Key
{
    internal sealed class SwitchHidController : IDisposable
    {
        private const ushort NintendoVendorId = 0x057E;
        private const ushort LeftJoyConProductId = 0x2006;
        private const ushort RightJoyConProductId = 0x2007;
        private const ushort ProControllerProductId = 0x2009;
        private const uint ReportLength = 49;

        private readonly bool isLeft;
        private readonly bool isPro;
        private readonly IntPtr handle;
        private readonly HashSet<string> lastInputs = [];
        private byte packetNumber;
        private bool disposed;

        public string Name { get; }
        public string Path { get; }

        //-------------------------------------------------------------------------------
        // Joy-Con単体のスティックを横持ち（レールが上）基準でPOVへ変換するか
        // JoyToKeyがBluetooth簡易HIDモードで見るハットスイッチの向きに合わせるための設定
        //-------------------------------------------------------------------------------
        public bool SidewaysStick { get; set; } = true;

        //-------------------------------------------------------------------------------
        // Nintendo Switch系コントローラーを初期化する処理
        //-------------------------------------------------------------------------------
        private SwitchHidController(IntPtr handle, string path, string name, bool isLeft, bool isPro)
        {
            this.handle = handle;
            Path = path;
            Name = name;
            this.isLeft = isLeft;
            this.isPro = isPro;

            HidApi.hid_set_nonblocking(handle, 1);
            InitializeInputMode();
        }

        //-------------------------------------------------------------------------------
        // Nintendo Switch系コントローラーを列挙する処理
        //-------------------------------------------------------------------------------
        public static List<SwitchHidController> Enumerate()
        {
            var controllers = new List<SwitchHidController>();

            try
            {
                HidApi.hid_init();
                var currentPointer = HidApi.hid_enumerate(NintendoVendorId, 0);
                var topPointer = currentPointer;

                try
                {
                    while (currentPointer != IntPtr.Zero)
                    {
                        var deviceInfo = Marshal.PtrToStructure<HidApi.HidDeviceInfo>(currentPointer);

                        if (IsSupportedProduct(deviceInfo.ProductId) && !string.IsNullOrWhiteSpace(deviceInfo.Path))
                        {
                            var handle = HidApi.hid_open_path(deviceInfo.Path);

                            if (handle != IntPtr.Zero)
                            {
                                var controller = CreateController(handle, deviceInfo);
                                controllers.Add(controller);
                            }
                        }

                        currentPointer = deviceInfo.Next;
                    }
                }
                finally
                {
                    if (topPointer != IntPtr.Zero)
                    {
                        HidApi.hid_free_enumeration(topPointer);
                    }
                }
            }
            catch
            {
                foreach (var controller in controllers)
                {
                    controller.Dispose();
                }

                controllers.Clear();
            }

            return controllers;
        }

        //-------------------------------------------------------------------------------
        // Nintendo Switch系コントローラーか判定する処理
        //-------------------------------------------------------------------------------
        private static bool IsSupportedProduct(ushort productId)
        {
            return productId is LeftJoyConProductId or RightJoyConProductId or ProControllerProductId;
        }

        //-------------------------------------------------------------------------------
        // HID列挙情報からコントローラーを作成する処理
        //-------------------------------------------------------------------------------
        private static SwitchHidController CreateController(IntPtr handle, HidApi.HidDeviceInfo deviceInfo)
        {
            var isLeft = deviceInfo.ProductId is LeftJoyConProductId or ProControllerProductId;
            var isPro = deviceInfo.ProductId == ProControllerProductId;
            var name = deviceInfo.ProductId switch
            {
                LeftJoyConProductId => "Joy-Con L",
                RightJoyConProductId => "Joy-Con R",
                ProControllerProductId => "Pro Controller",
                _ => "Nintendo Controller",
            };

            return new SwitchHidController(handle, deviceInfo.Path, name, isLeft, isPro);
        }

        //-------------------------------------------------------------------------------
        // Switch標準入力レポートへ切り替える処理
        //-------------------------------------------------------------------------------
        private void InitializeInputMode()
        {
            SendSubcommand(0x40, [0x00]); // IMUを無効化
            SendSubcommand(0x48, [0x01]); // 振動用データを有効化
            SendSubcommand(0x03, [0x30]); // 標準フル入力レポートへ切替
        }

        //-------------------------------------------------------------------------------
        // Switchサブコマンドを送信する処理
        //-------------------------------------------------------------------------------
        private void SendSubcommand(byte subcommand, byte[] data)
        {
            var buffer = new byte[ReportLength];
            buffer[0] = 0x01;
            buffer[1] = packetNumber;
            buffer[2] = 0x00;
            buffer[3] = 0x01;
            buffer[4] = 0x40;
            buffer[5] = 0x40;
            buffer[6] = 0x00;
            buffer[7] = 0x01;
            buffer[8] = 0x40;
            buffer[9] = 0x40;
            buffer[10] = subcommand;
            Array.Copy(data, 0, buffer, 11, data.Length);
            packetNumber = (byte)((packetNumber + 1) & 0x0F);

            HidApi.hid_write(handle, buffer, new UIntPtr((uint)(11 + data.Length)));

            var response = new byte[ReportLength];
            for (var retryIndex = 0; retryIndex < 3; retryIndex++)
            {
                var readLength = HidApi.hid_read_timeout(handle, response, new UIntPtr(ReportLength), 30);

                if (readLength > 0 && response[0] == 0x21 && response[14] == subcommand)
                {
                    return;
                }
            }
        }

        //-------------------------------------------------------------------------------
        // HIDレポートから押下中の入力名を取得する処理
        //-------------------------------------------------------------------------------
        public bool TryGetPressedInputs(out HashSet<string> inputs, int timeoutMilliseconds = 1)
        {
            inputs = [];
            var report = new byte[ReportLength];
            var readLength = HidApi.hid_read_timeout(handle, report, new UIntPtr(ReportLength), timeoutMilliseconds);

            if (readLength <= 0)
            {
                inputs = lastInputs.ToHashSet();
                return true;
            }

            if (report[0] != 0x30 && report[0] != 0x31)
            {
                inputs = lastInputs.ToHashSet();
                return true;
            }

            AddButtonInputs(inputs, report);
            AddStickInputs(inputs, report);
            AddJoyToKeyCompatibleInputs(inputs);
            lastInputs.Clear();

            foreach (var inputName in inputs)
            {
                lastInputs.Add(inputName);
            }

            return true;
        }

        //-------------------------------------------------------------------------------
        // HIDレポートのボタン情報を入力名へ変換する処理
        // report[3]=右側ボタン，report[4]=共通ボタン，report[5]=左側ボタン（BetterJoy準拠）
        //-------------------------------------------------------------------------------
        private void AddButtonInputs(HashSet<string> inputs, byte[] report)
        {
            var rightByte = report[3];
            var sharedByte = report[4];
            var leftByte = report[5];

            AddInput(inputs, "SwitchMinus", (sharedByte & 0x01) != 0);
            AddInput(inputs, "SwitchPlus", (sharedByte & 0x02) != 0);
            AddInput(inputs, "SwitchHome", (sharedByte & 0x10) != 0);
            AddInput(inputs, "SwitchCapture", (sharedByte & 0x20) != 0);

            if (isLeft)
            {
                AddInput(inputs, "SwitchDPadDown", (leftByte & 0x01) != 0);
                AddInput(inputs, "SwitchDPadUp", (leftByte & 0x02) != 0);
                AddInput(inputs, "SwitchDPadRight", (leftByte & 0x04) != 0);
                AddInput(inputs, "SwitchDPadLeft", (leftByte & 0x08) != 0);
                AddInput(inputs, "SwitchSR", (leftByte & 0x10) != 0);
                AddInput(inputs, "SwitchSL", (leftByte & 0x20) != 0);
                AddInput(inputs, "SwitchL", (leftByte & 0x40) != 0);
                AddInput(inputs, "SwitchZL", (leftByte & 0x80) != 0);
                AddInput(inputs, "SwitchStick", (sharedByte & 0x08) != 0);
            }

            if (!isLeft || isPro)
            {
                AddInput(inputs, "SwitchY", (rightByte & 0x01) != 0);
                AddInput(inputs, "SwitchX", (rightByte & 0x02) != 0);
                AddInput(inputs, "SwitchB", (rightByte & 0x04) != 0);
                AddInput(inputs, "SwitchA", (rightByte & 0x08) != 0);
                AddInput(inputs, "SwitchR", (rightByte & 0x40) != 0);
                AddInput(inputs, "SwitchZR", (rightByte & 0x80) != 0);
                AddInput(inputs, "SwitchRightStick", (sharedByte & 0x04) != 0);
            }

            if (!isLeft)
            {
                AddInput(inputs, "SwitchSR", (rightByte & 0x10) != 0);   // Joy-Con R単体のSL/SRは右側バイトにある
                AddInput(inputs, "SwitchSL", (rightByte & 0x20) != 0);
            }
        }

        //-------------------------------------------------------------------------------
        // Switch入力名からJoyToKey互換のボタン番号とPOV名を追加する処理
        // Bluetooth接続時にJoyToKey（DirectInput）が見る番号に合わせて種別ごとに変える
        //-------------------------------------------------------------------------------
        private void AddJoyToKeyCompatibleInputs(HashSet<string> inputs)
        {
            if (isPro)
            {
                AddAliasInput(inputs, "SwitchB", "Button01");
                AddAliasInput(inputs, "SwitchA", "Button02");
                AddAliasInput(inputs, "SwitchY", "Button03");
                AddAliasInput(inputs, "SwitchX", "Button04");
                AddAliasInput(inputs, "SwitchL", "Button05");
                AddAliasInput(inputs, "SwitchR", "Button06");
                AddAliasInput(inputs, "SwitchZL", "Button07");
                AddAliasInput(inputs, "SwitchZR", "Button08");
                AddAliasInput(inputs, "SwitchMinus", "Button09");
                AddAliasInput(inputs, "SwitchPlus", "Button10");
                AddAliasInput(inputs, "SwitchStick", "Button11");
                AddAliasInput(inputs, "SwitchRightStick", "Button12");
                AddAliasInput(inputs, "SwitchHome", "Button13");
                AddAliasInput(inputs, "SwitchCapture", "Button14");
                AddAliasInput(inputs, "SwitchDPadUp", "PovUp");
                AddAliasInput(inputs, "SwitchDPadRight", "PovRight");
                AddAliasInput(inputs, "SwitchDPadDown", "PovDown");
                AddAliasInput(inputs, "SwitchDPadLeft", "PovLeft");
                return;
            }

            if (isLeft)
            {
                AddAliasInput(inputs, "SwitchDPadLeft", "Button01");     // 横持ち時の下＝Button01
                AddAliasInput(inputs, "SwitchDPadDown", "Button02");     // 横持ち時の右＝Button02
                AddAliasInput(inputs, "SwitchDPadUp", "Button03");       // 横持ち時の左＝Button03
                AddAliasInput(inputs, "SwitchDPadRight", "Button04");    // 横持ち時の上＝Button04
                AddAliasInput(inputs, "SwitchSL", "Button05");
                AddAliasInput(inputs, "SwitchSR", "Button06");
                AddAliasInput(inputs, "SwitchMinus", "Button09");
                AddAliasInput(inputs, "SwitchStick", "Button11");
                AddAliasInput(inputs, "SwitchCapture", "Button14");
                AddAliasInput(inputs, "SwitchL", "Button15");
                AddAliasInput(inputs, "SwitchZL", "Button16");
                AddJoyConStickPovInputs(inputs, "SwitchAxisRight", "SwitchAxisDown", "SwitchAxisLeft", "SwitchAxisUp");
                return;
            }

            AddAliasInput(inputs, "SwitchA", "Button01");                // 横持ち時の下＝Button01
            AddAliasInput(inputs, "SwitchX", "Button02");                // 横持ち時の右＝Button02
            AddAliasInput(inputs, "SwitchB", "Button03");                // 横持ち時の左＝Button03
            AddAliasInput(inputs, "SwitchY", "Button04");                // 横持ち時の上＝Button04
            AddAliasInput(inputs, "SwitchSL", "Button05");
            AddAliasInput(inputs, "SwitchSR", "Button06");
            AddAliasInput(inputs, "SwitchPlus", "Button10");
            AddAliasInput(inputs, "SwitchRightStick", "Button12");
            AddAliasInput(inputs, "SwitchHome", "Button13");
            AddAliasInput(inputs, "SwitchR", "Button15");
            AddAliasInput(inputs, "SwitchZR", "Button16");
            AddJoyConStickPovInputs(inputs, "SwitchAxisLeft", "SwitchAxisUp", "SwitchAxisRight", "SwitchAxisDown");
        }

        //-------------------------------------------------------------------------------
        // Joy-Con単体のスティック入力をPOV名へ変換する処理
        // 引数は横持ち時に上・右・下・左になる縦持ち基準の軸入力名
        //-------------------------------------------------------------------------------
        private void AddJoyConStickPovInputs(HashSet<string> inputs, string sidewaysUp, string sidewaysRight, string sidewaysDown, string sidewaysLeft)
        {
            if (SidewaysStick)
            {
                AddAliasInput(inputs, sidewaysUp, "PovUp");
                AddAliasInput(inputs, sidewaysRight, "PovRight");
                AddAliasInput(inputs, sidewaysDown, "PovDown");
                AddAliasInput(inputs, sidewaysLeft, "PovLeft");
                return;
            }

            AddAliasInput(inputs, "SwitchAxisUp", "PovUp");
            AddAliasInput(inputs, "SwitchAxisRight", "PovRight");
            AddAliasInput(inputs, "SwitchAxisDown", "PovDown");
            AddAliasInput(inputs, "SwitchAxisLeft", "PovLeft");
        }

        //-------------------------------------------------------------------------------
        // 入力が存在する場合に別名入力を追加する処理
        //-------------------------------------------------------------------------------
        private static void AddAliasInput(HashSet<string> inputs, string sourceInputName, string aliasInputName)
        {
            if (inputs.Contains(sourceInputName))
            {
                inputs.Add(aliasInputName);
            }
        }

        //-------------------------------------------------------------------------------
        // HIDレポートのスティック情報を入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private void AddStickInputs(HashSet<string> inputs, byte[] report)
        {
            var offset = isLeft ? 6 : 9;
            AddStickInputs(inputs, report, offset, "SwitchAxis");

            if (isPro)
            {
                AddStickInputs(inputs, report, 9, "SwitchRightAxis");
            }
        }

        //-------------------------------------------------------------------------------
        // 指定位置のスティック情報を入力名へ変換する処理
        //-------------------------------------------------------------------------------
        private static void AddStickInputs(HashSet<string> inputs, byte[] report, int offset, string prefix)
        {
            var x = report[offset] | ((report[offset + 1] & 0x0F) << 8);
            var y = (report[offset + 1] >> 4) | (report[offset + 2] << 4);

            if (x < 1200)
            {
                inputs.Add($"{prefix}Left");
            }
            else if (x > 2900)
            {
                inputs.Add($"{prefix}Right");
            }

            if (y < 1200)
            {
                inputs.Add($"{prefix}Down");
            }
            else if (y > 2900)
            {
                inputs.Add($"{prefix}Up");
            }
        }

        //-------------------------------------------------------------------------------
        // 条件成立時に入力名を追加する処理
        //-------------------------------------------------------------------------------
        private static void AddInput(HashSet<string> inputs, string inputName, bool isPressed)
        {
            if (isPressed)
            {
                inputs.Add(inputName);
            }
        }

        //-------------------------------------------------------------------------------
        // HIDデバイスを閉じる処理
        //-------------------------------------------------------------------------------
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            if (handle != IntPtr.Zero)
            {
                HidApi.hid_close(handle);
            }
        }
    }
}
