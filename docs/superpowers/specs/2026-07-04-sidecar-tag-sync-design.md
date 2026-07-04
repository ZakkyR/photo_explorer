# サイドカーファイルによるタグ同期設計

**日付**: 2026-07-04  
**対象**: PhotoExplorer — 複数 PC 間でのタグ共有

---

## 背景と目的

現在、タグは `%AppData%\PhotoExplorer\photo_explorer.db`（ローカル SQLite）に絶対パスで保存されており、別 PC では参照できない。OneDrive など同期ストレージ上の写真フォルダを複数 PC で開いたとき、タグが自動的に同期される仕組みを追加する。

---

## アーキテクチャ

各写真フォルダに `.photoexplorer\tags.json` を配置する（サイドカー方式）。

```
OneDrive\Photos\2024\
├── img001.jpg
├── img002.jpg
└── .photoexplorer\
    └── tags.json   ← OneDrive が自動同期
```

- `.photoexplorer` フォルダには `FILE_ATTRIBUTE_HIDDEN` を付与しエクスプローラーで非表示にする
- ローカル SQLite DB は高速クエリ用キャッシュとして維持する（廃止しない）
- JSON が「同期の媒体」、SQLite が「実行時の作業領域」

**この方式の特性**:
- OneDrive フォルダ → `tags.json` も同期される（自動共有）
- ローカルフォルダ → `tags.json` はローカルに残るだけ（正常動作、壊れない）
- 特別な OneDrive 判定ロジック不要

---

## tags.json フォーマット

```json
{
  "version": 1,
  "entries": [
    { "file": "img001.jpg", "tag": "旅行",  "removed": false, "ts": "2026-07-04T10:00:00Z" },
    { "file": "img001.jpg", "tag": "家族",  "removed": false, "ts": "2026-07-04T11:00:00Z" },
    { "file": "img001.jpg", "tag": "旧タグ", "removed": true,  "ts": "2026-07-05T09:00:00Z" }
  ]
}
```

- `file`: フォルダ内のファイル名のみ（パスなし）
- `removed`: `false` = タグあり、`true` = 削除済み
- `ts`: ISO 8601 UTC タイムスタンプ

同じ `(file, tag)` ペアが複数存在する場合、**最新 `ts` のエントリを採用**する。

---

## マージロジック

`tags.json` 読み込み時、各エントリを SQLite に反映する:

1. エントリを `(file, tag)` でグループ化し、最新 `ts` のエントリのみ残す
2. `removed: false` → SQLite に該当タグが存在しなければ INSERT
3. `removed: true` → SQLite から該当タグを DELETE

**競合解決例**:

| PC-A 操作 | PC-B 操作 | 結果 |
|-----------|-----------|------|
| 「旅行」追加 | 「家族」追加 | 両方残る |
| 「旧タグ」削除 | — | 削除が伝播する |
| 同じタグを同時追加 | 同じタグを同時追加 | 重複なし（INSERT OR IGNORE） |
| 同じタグを追加/削除 | 同じタグを削除/追加 | 後の `ts` が勝つ |

---

## 同期タイミング

| タイミング | 処理 |
|-----------|------|
| フォルダを開いたとき | `tags.json` 読み込み → SQLite にマージ |
| タグを追加／削除したとき | SQLite 更新 → `tags.json` 書き出し |
| `FileSystemWatcher` が `tags.json` 変更を検知 | 自動マージ（別 PC 変更をリアルタイム反映） |

`tags.json` の書き出しは一時ファイル経由で行い、`File.Replace` でアトミックに置き換える（書き込み途中のファイルを OneDrive が掴まないようにするため）。

---

## OneDrive 競合ファイルの処理

OneDrive が競合を検知した場合、`tags (PC-A's conflicted copy 2026-07-04).json` のようなファイルを生成することがある。`FileSystemWatcher` でこのパターンのファイルを検知し、通常の `tags.json` とマージした上で競合ファイルを削除する。

検知パターン: ファイル名が `tags` で始まり `.json` で終わり、かつ `tags.json` 以外のファイル。

---

## エラー処理

| 状況 | 対応 |
|------|------|
| `tags.json` が存在しない | 新規作成（初回またはローカルフォルダ） |
| JSON パース失敗（壊れたファイル） | 無視してローカル SQLite のみで動作、ログ出力 |
| ファイル書き込み失敗 | ローカル SQLite は更新済み、次回書き出し時にリトライ |

---

## 既存データの移行

**初回起動時**（`%AppData%\PhotoExplorer\migration_v1.done` ファイルが存在しない場合に実行、完了後に作成）:

1. SQLite の全 `ImageTags` を読み込む
2. `FilePath` からフォルダパスとファイル名を分離する
3. 各フォルダの `.photoexplorer\tags.json` にエクスポートする（`removed: false`、`ts` = 現在時刻）
4. フォルダが存在しない場合（削除済みファイル）はスキップする

既存 SQLite データは移行後も削除しない（キャッシュとして引き続き機能する）。

---

## 実装対象コンポーネント

| コンポーネント | 変更内容 |
|--------------|---------|
| `ISidecarService` / `SidecarService` | 新規：JSON の読み書き・マージ・FileSystemWatcher 管理 |
| `ITagService` / `TagService` | 変更：タグ追加・削除時に `SidecarService` を呼び出す |
| `IImageService` / `ImageService` | 変更：フォルダ読み込み時に `SidecarService.MergeAsync` を呼び出す |
| `App.xaml.cs` | 変更：`SidecarService` をDI登録、初回マイグレーション実行 |

---

## スコープ外

- アルバム情報の同期（今後の課題）
- 非 OneDrive フォルダの手動エクスポート・インポート UI
