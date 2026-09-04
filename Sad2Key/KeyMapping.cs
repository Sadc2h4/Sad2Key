using System.Globalization;
using System.Text;

namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // 割り当ての種類
    //-------------------------------------------------------------------------------
    internal enum MappingKind
    {
        Hold,               // cfg種別1: 押している間キーを押しっぱなしにする
        ShortLongPress,     // cfg種別7 モード3: 押す長さで入力1/入力2を切り替える
        MultiKeyOther,      // cfg種別7 その他モード: 暫定で入力1を押しっぱなしにする
        Unsupported,        // 解析できない行: 原文を保持するだけで動作しない
    }

    //-------------------------------------------------------------------------------
    // 1つの入力に対するキー割り当てを表すクラス（JoyToKey形式cfgの1行に対応）
    //-------------------------------------------------------------------------------
    internal sealed class KeyMapping
    {
        public const int InputCount = 4;
        public const int MaxKeysPerInput = 4;
        public const int ShortLongPressMode = 3;
        public const string DefaultHoldTail = "0.000, 0, 0";
        public const string DefaultMultiKeyTail = "95, 100, 0.000, 0, 0, 20";
        private const int MaxVirtualKeyCode = 0xFE;

        public MappingKind Kind { get; private set; }
        public List<Keys> Keys { get; private set; } = [];
        public int MultiMode { get; private set; }
        public int ThresholdMilliseconds { get; private set; }
        public List<Keys>[] Inputs { get; private set; } = CreateEmptyInputs();
        public string Tail { get; private set; } = DefaultHoldTail;
        public string? RawValue { get; private set; }

        //-------------------------------------------------------------------------------
        // 動作する割り当てが何も無いか判定する処理
        //-------------------------------------------------------------------------------
        public bool IsEmpty => Kind switch
        {
            MappingKind.Hold => Keys.Count == 0,
            MappingKind.ShortLongPress or MappingKind.MultiKeyOther => Inputs.All(x => x.Count == 0),
            MappingKind.Unsupported => string.IsNullOrWhiteSpace(RawValue),
            _ => true,
        };

        //-------------------------------------------------------------------------------
        // 押しっぱなし型の割り当てを作成する処理
        //-------------------------------------------------------------------------------
        public static KeyMapping CreateHold(IEnumerable<Keys> keys)
        {
            return new KeyMapping
            {
                Kind = MappingKind.Hold,
                Keys = keys.Distinct().Take(MaxKeysPerInput).ToList(),
                Tail = DefaultHoldTail,
            };
        }

        //-------------------------------------------------------------------------------
        // 短押し／長押し型の割り当てを作成する処理
        //-------------------------------------------------------------------------------
        public static KeyMapping CreateShortLongPress(int thresholdMilliseconds, IEnumerable<Keys> input1, IEnumerable<Keys> input2, IEnumerable<Keys> input3)
        {
            var inputs = CreateEmptyInputs();
            inputs[0] = input1.Distinct().Take(MaxKeysPerInput).ToList();
            inputs[1] = input2.Distinct().Take(MaxKeysPerInput).ToList();
            inputs[2] = input3.Distinct().Take(MaxKeysPerInput).ToList();

            return new KeyMapping
            {
                Kind = MappingKind.ShortLongPress,
                MultiMode = ShortLongPressMode,
                ThresholdMilliseconds = thresholdMilliseconds,
                Inputs = inputs,
                Tail = DefaultMultiKeyTail,
            };
        }

        //-------------------------------------------------------------------------------
        // 解析できない行を原文のまま保持する割り当てを作成する処理
        //-------------------------------------------------------------------------------
        public static KeyMapping CreateUnsupported(string rawValue)
        {
            return new KeyMapping
            {
                Kind = MappingKind.Unsupported,
                RawValue = rawValue,
            };
        }

        //-------------------------------------------------------------------------------
        // JoyToKey形式cfgの「=」右側の文字列を割り当てへ変換する処理
        //-------------------------------------------------------------------------------
        public static bool TryParseJoyToKeyValue(string value, out KeyMapping mapping)
        {
            mapping = CreateUnsupported(value);
            var trimmedValue = value.Trim();

            if (trimmedValue.Length == 0)
            {
                return false;
            }

            var parts = trimmedValue.Split(',', StringSplitOptions.TrimEntries);

            if (parts[0] == "1" && TryParseHold(parts, out var holdMapping))
            {
                mapping = holdMapping;
                return true;
            }

            if (parts[0] == "7" && TryParseMultiKey(parts, out var multiKeyMapping))
            {
                mapping = multiKeyMapping;
                return true;
            }

            return true;                                                    // 未対応行は原文保持で成功扱いにする
        }

        //-------------------------------------------------------------------------------
        // 種別1（キーボード同時押し）の行を解析する処理
        //-------------------------------------------------------------------------------
        private static bool TryParseHold(string[] parts, out KeyMapping mapping)
        {
            mapping = CreateHold([]);

            if (parts.Length < 2 || !TryParseKeyCodes(parts[1], out var keys))
            {
                return false;
            }

            mapping.Keys = keys;
            mapping.Tail = parts.Length > 2 ? string.Join(", ", parts.Skip(2)) : DefaultHoldTail;
            return true;
        }

        //-------------------------------------------------------------------------------
        // 種別7（キーボード（複数））の行を解析する処理
        //-------------------------------------------------------------------------------
        private static bool TryParseMultiKey(string[] parts, out KeyMapping mapping)
        {
            mapping = CreateHold([]);

            if (parts.Length < 3 + InputCount
                || !int.TryParse(parts[1], out var multiMode)
                || !int.TryParse(parts[2], out var thresholdMilliseconds))
            {
                return false;
            }

            var inputs = CreateEmptyInputs();

            for (var inputIndex = 0; inputIndex < InputCount; inputIndex++)
            {
                if (!TryParseKeyCodes(parts[3 + inputIndex], out var keys))
                {
                    return false;
                }

                inputs[inputIndex] = keys;
            }

            mapping.Kind = multiMode == ShortLongPressMode ? MappingKind.ShortLongPress : MappingKind.MultiKeyOther;
            mapping.MultiMode = multiMode;
            mapping.ThresholdMilliseconds = thresholdMilliseconds;
            mapping.Inputs = inputs;
            mapping.Tail = parts.Length > 3 + InputCount ? string.Join(", ", parts.Skip(3 + InputCount)) : DefaultMultiKeyTail;
            return true;
        }

        //-------------------------------------------------------------------------------
        // 「11:5A:00:00」形式のキーコード列をキー一覧へ変換する処理
        //-------------------------------------------------------------------------------
        private static bool TryParseKeyCodes(string keyCodeText, out List<Keys> keys)
        {
            keys = [];

            foreach (var keyCode in keyCodeText.Split(':', StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(keyCode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var virtualKey))
                {
                    return false;
                }

                if (virtualKey == 0)
                {
                    continue;
                }

                if (virtualKey > MaxVirtualKeyCode)
                {
                    return false;                                           // マウスボタンなどの特殊コードは未対応
                }

                keys.Add((Keys)virtualKey);
            }

            return true;
        }

        //-------------------------------------------------------------------------------
        // 割り当てをJoyToKey形式cfgの「=」右側の文字列へ変換する処理
        //-------------------------------------------------------------------------------
        public string ToJoyToKeyValue()
        {
            return Kind switch
            {
                MappingKind.Hold => $"1, {FormatKeyCodes(Keys, "00")}, {Tail}",
                MappingKind.ShortLongPress or MappingKind.MultiKeyOther =>
                    $"7, {MultiMode}, {ThresholdMilliseconds}, {string.Join(", ", Inputs.Select(x => FormatKeyCodes(x, "0")))}, {Tail}",
                _ => RawValue ?? string.Empty,
            };
        }

        //-------------------------------------------------------------------------------
        // キー一覧を「11:5A:00:00」形式の文字列へ変換する処理
        //-------------------------------------------------------------------------------
        private static string FormatKeyCodes(List<Keys> keys, string emptyCode)
        {
            var keyCodes = keys
                .Take(MaxKeysPerInput)
                .Select(x => ((int)x).ToString("X2"))
                .ToList();

            while (keyCodes.Count < MaxKeysPerInput)
            {
                keyCodes.Add(emptyCode);
            }

            return string.Join(":", keyCodes);
        }

        //-------------------------------------------------------------------------------
        // 一覧表示用の説明文字列を作成する処理
        //-------------------------------------------------------------------------------
        public string Describe()
        {
            switch (Kind)
            {
                case MappingKind.Hold:
                    return DescribeKeys(Keys);

                case MappingKind.ShortLongPress:
                    {
                        var builder = new StringBuilder();
                        builder.Append("Short: ").Append(DescribeKeys(Inputs[0]));
                        builder.Append(" | Long(").Append(ThresholdMilliseconds).Append("ms): ").Append(DescribeKeys(Inputs[1]));

                        if (Inputs[2].Count > 0)
                        {
                            builder.Append(" | After: ").Append(DescribeKeys(Inputs[2]));
                        }

                        return builder.ToString();
                    }

                case MappingKind.MultiKeyOther:
                    return $"Multi(mode {MultiMode}): {DescribeKeys(Inputs[0])}";

                default:
                    {
                        var rawValue = RawValue ?? string.Empty;
                        return $"(unsupported) {(rawValue.Length > 30 ? rawValue[..30] + "..." : rawValue)}";
                    }
            }
        }

        //-------------------------------------------------------------------------------
        // キー一覧を「Ctrl + Z」のような表示文字列へ変換する処理
        //-------------------------------------------------------------------------------
        public static string DescribeKeys(List<Keys> keys)
        {
            return keys.Count == 0 ? "-" : string.Join(" + ", keys.Select(DescribeKey));
        }

        //-------------------------------------------------------------------------------
        // キーを読みやすい表示名へ変換する処理
        //-------------------------------------------------------------------------------
        public static string DescribeKey(Keys key)
        {
            return key switch
            {
                System.Windows.Forms.Keys.ControlKey or System.Windows.Forms.Keys.LControlKey => "Ctrl",
                System.Windows.Forms.Keys.RControlKey => "RCtrl",
                System.Windows.Forms.Keys.ShiftKey or System.Windows.Forms.Keys.LShiftKey => "Shift",
                System.Windows.Forms.Keys.RShiftKey => "RShift",
                System.Windows.Forms.Keys.Menu or System.Windows.Forms.Keys.LMenu => "Alt",
                System.Windows.Forms.Keys.RMenu => "RAlt",
                _ => key.ToString(),
            };
        }

        //-------------------------------------------------------------------------------
        // 空の入力1〜4を作成する処理
        //-------------------------------------------------------------------------------
        private static List<Keys>[] CreateEmptyInputs()
        {
            var inputs = new List<Keys>[InputCount];

            for (var inputIndex = 0; inputIndex < InputCount; inputIndex++)
            {
                inputs[inputIndex] = [];
            }

            return inputs;
        }
    }
}
