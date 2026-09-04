namespace Sad2Key
{
    //-------------------------------------------------------------------------------
    // 入力名の押下状態をキー割り当てに従ってキー操作へ変換するクラス
    // 押しっぱなし・短押し／長押し・タップのUp待ちをここで管理する
    //-------------------------------------------------------------------------------
    internal sealed class InputEngine
    {
        private const int TapDurationMilliseconds = 40;

        private readonly KeySender keySender;
        private readonly IReadOnlyDictionary<string, KeyMapping> mappings;
        private readonly Action<string>? logAction;
        private readonly Dictionary<string, InputState> heldInputs = [];
        private readonly List<PendingRelease> pendingReleases = [];
        private readonly object engineLock = new();

        public bool EnableLog { get; set; }

        //-------------------------------------------------------------------------------
        // 入力エンジンを初期化する処理
        //-------------------------------------------------------------------------------
        public InputEngine(KeySender keySender, IReadOnlyDictionary<string, KeyMapping> mappings, Action<string>? logAction = null)
        {
            this.keySender = keySender;
            this.mappings = mappings;
            this.logAction = logAction;
        }

        //-------------------------------------------------------------------------------
        // 現在押されている入力名の一覧を受け取り，キー操作へ反映する処理
        //-------------------------------------------------------------------------------
        public void Update(HashSet<string> currentInputs, long nowMilliseconds)
        {
            lock (engineLock)
            {
                foreach (var inputName in currentInputs)
                {
                    if (!heldInputs.ContainsKey(inputName))
                    {
                        OnPress(inputName, nowMilliseconds);
                    }
                }

                foreach (var inputName in heldInputs.Keys.Except(currentInputs).ToArray())
                {
                    OnRelease(inputName, nowMilliseconds);
                }

                foreach (var pair in heldInputs)
                {
                    UpdateLongPress(pair.Key, pair.Value, nowMilliseconds);
                }

                ProcessPendingReleases(nowMilliseconds);
            }
        }

        //-------------------------------------------------------------------------------
        // 押下状態とタップ待ちをすべて解放する処理
        //-------------------------------------------------------------------------------
        public void ReleaseAll()
        {
            lock (engineLock)
            {
                heldInputs.Clear();
                pendingReleases.Clear();
                keySender.ReleaseAll();
            }
        }

        //-------------------------------------------------------------------------------
        // 入力名が押されたときの処理
        //-------------------------------------------------------------------------------
        private void OnPress(string inputName, long nowMilliseconds)
        {
            if (!mappings.TryGetValue(inputName, out var mapping) || mapping.IsEmpty)
            {
                return;
            }

            var state = new InputState(mapping, nowMilliseconds);
            heldInputs[inputName] = state;

            switch (mapping.Kind)
            {
                case MappingKind.Hold:
                    keySender.Press(mapping.Keys);
                    Log($"{inputName} -> {KeyMapping.DescribeKeys(mapping.Keys)} Down");
                    break;

                case MappingKind.MultiKeyOther:
                    keySender.Press(mapping.Inputs[0]);                    // 未対応モードは入力1を押しっぱなしにする
                    Log($"{inputName} -> {KeyMapping.DescribeKeys(mapping.Inputs[0])} Down");
                    break;

                case MappingKind.ShortLongPress:
                    UpdateLongPress(inputName, state, nowMilliseconds);     // しきい値0などの場合に備えて即判定する
                    break;
            }
        }

        //-------------------------------------------------------------------------------
        // 入力名が離されたときの処理
        //-------------------------------------------------------------------------------
        private void OnRelease(string inputName, long nowMilliseconds)
        {
            if (!heldInputs.Remove(inputName, out var state))
            {
                return;
            }

            var mapping = state.Mapping;

            switch (mapping.Kind)
            {
                case MappingKind.Hold:
                    keySender.Release(mapping.Keys);
                    Log($"{inputName} -> {KeyMapping.DescribeKeys(mapping.Keys)} Up");
                    break;

                case MappingKind.MultiKeyOther:
                    keySender.Release(mapping.Inputs[0]);
                    Log($"{inputName} -> {KeyMapping.DescribeKeys(mapping.Inputs[0])} Up");
                    break;

                case MappingKind.ShortLongPress:
                    if (state.IsLongPressActive)
                    {
                        keySender.Release(mapping.Inputs[1]);
                        Log($"{inputName} -> Long: {KeyMapping.DescribeKeys(mapping.Inputs[1])} Up");
                        Tap(inputName, "After", mapping.Inputs[2], nowMilliseconds);
                    }
                    else
                    {
                        Tap(inputName, "Short", mapping.Inputs[0], nowMilliseconds);
                    }

                    break;
            }
        }

        //-------------------------------------------------------------------------------
        // 押下中の短押し／長押し入力がしきい値に達したか判定する処理
        //-------------------------------------------------------------------------------
        private void UpdateLongPress(string inputName, InputState state, long nowMilliseconds)
        {
            var mapping = state.Mapping;

            if (mapping.Kind != MappingKind.ShortLongPress
                || state.IsLongPressActive
                || nowMilliseconds - state.PressedAtMilliseconds < mapping.ThresholdMilliseconds)
            {
                return;
            }

            state.IsLongPressActive = true;
            keySender.Press(mapping.Inputs[1]);
            Log($"{inputName} -> Long: {KeyMapping.DescribeKeys(mapping.Inputs[1])} Down");
        }

        //-------------------------------------------------------------------------------
        // キー一覧をタップ（Down後に一定時間でUp）する処理
        //-------------------------------------------------------------------------------
        private void Tap(string inputName, string label, List<Keys> keys, long nowMilliseconds)
        {
            if (keys.Count == 0)
            {
                return;
            }

            keySender.Press(keys);
            pendingReleases.Add(new PendingRelease(keys, nowMilliseconds + TapDurationMilliseconds));
            Log($"{inputName} -> {label}: {KeyMapping.DescribeKeys(keys)} Tap");
        }

        //-------------------------------------------------------------------------------
        // 期限を過ぎたタップのUpを送信する処理
        //-------------------------------------------------------------------------------
        private void ProcessPendingReleases(long nowMilliseconds)
        {
            for (var index = pendingReleases.Count - 1; index >= 0; index--)
            {
                var pendingRelease = pendingReleases[index];

                if (nowMilliseconds < pendingRelease.ReleaseAtMilliseconds)
                {
                    continue;
                }

                pendingReleases.RemoveAt(index);
                keySender.Release(pendingRelease.Keys);
            }
        }

        //-------------------------------------------------------------------------------
        // 入力ログを出力する処理
        //-------------------------------------------------------------------------------
        private void Log(string message)
        {
            if (EnableLog)
            {
                logAction?.Invoke(message);
            }
        }

        //-------------------------------------------------------------------------------
        // 押下中の入力名ごとの状態
        //-------------------------------------------------------------------------------
        private sealed class InputState(KeyMapping mapping, long pressedAtMilliseconds)
        {
            public KeyMapping Mapping { get; } = mapping;
            public long PressedAtMilliseconds { get; } = pressedAtMilliseconds;
            public bool IsLongPressActive { get; set; }
        }

        private sealed record PendingRelease(List<Keys> Keys, long ReleaseAtMilliseconds);
    }
}
