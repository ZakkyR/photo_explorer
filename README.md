# PhotoExplorer

Windows 向け画像整理デスクトップアプリ。複数フォルダの画像をタグで管理し、素早くプレビュー・他アプリへ転送できます。

## スクリーンショット

> *(準備中)*

## 特徴

| 機能 | 説明 |
|------|------|
| **フォルダ管理** | 複数の画像フォルダを登録・削除。表示名を付けて管理しやすく |
| **アルバム** | 複数フォルダを一つのビューにまとめて表示 |
| **タグ付け** | JPEG / PNG は IPTC キーワードとしてファイルに直接書き込み。その他形式は SQLite に保存 |
| **タグフィルタリング** | 複数タグの OR 条件で絞り込み |
| **サムネイルグリッド** | サイズをスライダーで 50〜500px に調整可能。ディスクキャッシュで高速表示 |
| **プレビュー** | ダブルクリックでプレビューウィンドウを表示。← → キーで画像切替 |
| **ドラッグ＆ドロップ** | 画像を選択してエクスプローラーや Affinity 等のアプリへ直接ドロップ |
| **複数選択** | Ctrl + クリックで複数枚選択、一括タグ編集 |
| **自動更新** | フォルダへの画像追加・削除を自動検知してグリッドに反映 |

## 対応形式

`.jpg` `.jpeg` `.png` `.webp` `.bmp` `.tiff` `.tif` `.raw` `.cr2` `.nef` `.arw` `.dng`

## 動作環境

- Windows 10 / 11 (x64)
- インストール不要（単一 EXE）

## ダウンロード

[Releases](../../releases) ページから `PhotoExplorer.App.exe` をダウンロードして実行してください。

## データ保存先

| 種類 | パス |
|------|------|
| DB（アルバム・フォルダ・タグ） | `%APPDATA%\PhotoExplorer\photo_explorer.db` |
| ウィンドウ設定 | `%APPDATA%\PhotoExplorer\settings.json` |
| サムネイルキャッシュ | `%LOCALAPPDATA%\PhotoExplorer\thumbnails\` |

アンインストール時はこれらのフォルダを削除してください。

## ソースからビルド

**.NET 8 SDK** が必要です。

```powershell
git clone https://github.com/ZakkyR/photo_explorer.git
cd photo_explorer\src
dotnet build PhotoExplorer.App\PhotoExplorer.App.csproj -c Release
```

単一 EXE を生成する場合:

```powershell
dotnet publish PhotoExplorer.App\PhotoExplorer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\publish
```

## 技術スタック

- **WPF** (.NET 8, net8.0-windows)
- **MVVM** — CommunityToolkit.Mvvm
- **DB** — EF Core 8 + SQLite
- **画像処理** — SixLabors.ImageSharp 3.x
- **メタデータ読み取り** — MetadataExtractor
- **DI** — Microsoft.Extensions.DependencyInjection

## ライセンス

MIT
