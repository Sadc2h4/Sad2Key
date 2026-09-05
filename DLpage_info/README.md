# Sad2Key
<!-- .NET 8 / Windows x64 -->
![.NET](https://img.shields.io/badge/language-.NET%208-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square&logo=windows&logoColor=white)
![Architecture](https://img.shields.io/badge/arch-x64-gray?style=flat-square)

Sad2Key is a lightweight Windows tool that turns Nintendo Switch controller input into keyboard input.  
Bluetooth接続した Nintendo Switch Pro Controller / Joy-Con のボタンやスティックをキーボード入力へ変換します。

## Download

配布時は `Sad2Key.exe` を同梱してください。  
`Sad2Key.exe` is a self-contained Windows x64 executable.

## Features / 主な機能

| Feature | Description |
|--------|-------------|
| Switch HID 直接読み取り / Switch HID | Pro Controller, Joy-Con L, Joy-Con R を `hidapi` で直接読み取ります。Bluetooth接続時の遅延と取りこぼしを抑えます。 |
| JoyToKey形式プロファイル / JoyToKey .cfg | JoyToKey の `.cfg` を読み込み、編集して保存できます。未対応の行は原文のまま保持します。 |
| 同時押し / Simultaneous press | 最大4キーの同時押しに対応します。修飾キーを共有する入力を同時に押しても、最後の1つを離すまで修飾キーは押されたままになります。 |
| 短押し・長押し / Short & Long press | 押した長さで割り当てを切り替えます。しきい値未満で短押しキー、到達で長押しキーを保持し、離した後のキーも指定できます。 |
| マッピング編集 / Mapping editor | 一覧の行をダブルクリックして編集します。入力欄をクリックしてキーを押すだけで取り込めます。 |
| Joy-Con 横持ち / Sideways stick | Joy-Con 単体使用時のスティック方向を横持ち基準に回転します。 |
| テーマ / Theme | Dark / Light をタイルで切替できます。 |
| ポータブル / Portable | 設定は exe と同じフォルダに保存します。レジストリや `%APPDATA%` は使いません。 |

## Setup

特別なインストールは不要です。  
Place `Sad2Key.exe` in any folder and run it.

```text
Sad2Key.exe
```

## Usage / 使い方

1. コントローラーを Bluetooth で Windows に接続します。
2. `Sad2Key.exe` を起動します。Switch HID のコントローラーが見つかると自動で選択されます。
3. `Profile` タイルからプロファイル（JoyToKey の `.cfg` または `keymap.json`）を選びます。
4. 必要なら一覧の行をダブルクリックして割り当てを編集し、`Save` ボタンで保存します。
5. 右下の電源ボタンで開始します。状態ピルが `Running` になります。
6. 入力先のウィンドウへフォーカスを移して使います。
7. 止めるときはもう一度電源ボタンを押します。

## Controller Notes

`Controller` の選択肢は `〜 (Switch HID)` の個別コントローラーを推奨します。  
`All detected controllers` は複数経路を同時に見るため、遅延や重複入力の原因になることがあります。

Joy-Con 単体で縦持ちにしたときスティック方向が合わない場合は、`Joy-Con stick sideways` を OFF にしてください。

## Deletion Method / 削除方法

`Sad2Key.exe` が入ったフォルダごと削除してください。  
初回起動時に `%LOCALAPPDATA%\Sad2Key\native\hidapi.dll` が展開されるので、完全に消す場合はこのフォルダも削除してください。  
No registry entries are created by this application.

## Credits

Sad2Key is created by `C2H4`.  
Switch controller HID handling was implemented with reference to `BetterJoy` (MIT License).  
`hidapi.dll` is from the `hidapi` project.

## Disclaimer / 免責事項

本ソフトウェアの使用によって生じたいかなる損害についても、作者は一切の責任を負いません。  
I assume no responsibility whatsoever for any damages incurred through the use of this software.
