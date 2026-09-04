using System.Runtime.InteropServices;

namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // 仮想キーごとの参照カウントを持ち，SendInputでキーボード入力を送信するクラス
    // 複数の入力が同じ修飾キーを共有しても，最後の1つが離れるまでKeyUpしない
    //-------------------------------------------------------------------------------
    internal class KeySender
    {
        private const uint InputKeyboard = 1;
        private const uint KeyEventFExtendedKey = 0x0001;
        private const uint KeyEventFKeyUp = 0x0002;
        private const uint MapVirtualKeyToScanCode = 0;

        private readonly Dictionary<Keys, int> holdCounts = [];
        private readonly object sendLock = new();
        private readonly Action<string>? logAction;

        //-------------------------------------------------------------------------------
        // キー送信クラスを初期化する処理
        //-------------------------------------------------------------------------------
        public KeySender(Action<string>? logAction = null)
        {
            this.logAction = logAction;
        }

        //-------------------------------------------------------------------------------
        // キー一覧を順にKeyDownする処理（参照カウントが0→1のときだけ送信）
        //-------------------------------------------------------------------------------
        public void Press(IEnumerable<Keys> keys)
        {
            lock (sendLock)
            {
                foreach (var key in keys)
                {
                    holdCounts.TryGetValue(key, out var count);
                    holdCounts[key] = count + 1;

                    if (count == 0)
                    {
                        SendKey(key, false);
                    }
                }
            }
        }

        //-------------------------------------------------------------------------------
        // キー一覧を逆順にKeyUpする処理（参照カウントが1→0のときだけ送信）
        //-------------------------------------------------------------------------------
        public void Release(IEnumerable<Keys> keys)
        {
            lock (sendLock)
            {
                foreach (var key in keys.Reverse())
                {
                    if (!holdCounts.TryGetValue(key, out var count) || count <= 0)
                    {
                        continue;                                           // 押していないキーは無視する
                    }

                    if (count == 1)
                    {
                        holdCounts.Remove(key);
                        SendKey(key, true);
                    }
                    else
                    {
                        holdCounts[key] = count - 1;
                    }
                }
            }
        }

        //-------------------------------------------------------------------------------
        // 押下中のキーをすべてKeyUpして初期化する処理
        //-------------------------------------------------------------------------------
        public void ReleaseAll()
        {
            lock (sendLock)
            {
                foreach (var key in holdCounts.Keys.ToArray())
                {
                    SendKey(key, true);
                }

                holdCounts.Clear();
            }
        }

        //-------------------------------------------------------------------------------
        // 参照カウントを介さずにキーのDown→Upを1回送信する処理（動作確認用）
        //-------------------------------------------------------------------------------
        public void SendTap(Keys key)
        {
            lock (sendLock)
            {
                SendKey(key, false);
                SendKey(key, true);
            }
        }

        //-------------------------------------------------------------------------------
        // 1キー分のDownまたはUpを送信する処理（テスト時は派生クラスで差し替える）
        //-------------------------------------------------------------------------------
        protected virtual void SendKey(Keys key, bool keyUp)
        {
            SendKeyboardInput(key, keyUp, logAction);
        }

        //-------------------------------------------------------------------------------
        // SendInputでキーボード入力を送信する処理
        //-------------------------------------------------------------------------------
        private static void SendKeyboardInput(Keys key, bool keyUp, Action<string>? logAction)
        {
            var input = new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = (ushort)key,
                        Scan = (ushort)MapVirtualKey((uint)key, MapVirtualKeyToScanCode),  // 仮想キー方式のままスキャンコードも埋める
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
                or Keys.NumLock or Keys.Divide or Keys.PrintScreen
                ? KeyEventFExtendedKey
                : 0;
        }

        //-------------------------------------------------------------------------------
        // キーボード入力を送信するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

        //-------------------------------------------------------------------------------
        // 仮想キーコードをスキャンコードへ変換するWindows API
        //-------------------------------------------------------------------------------
        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

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
