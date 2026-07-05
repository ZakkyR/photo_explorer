# 設計仕様: JSON 手動同期 UI・マイグレーションステータス表示

**日付:** 2026-07-05
**対象バージョン:** v1.4.0

---

## 概要

OneDrive を経由したタグ同期において、PC 間でデータがずれた場合に手動で修復できる UI を追加する。
また、起動時に自動実行されるマイグレーション（DB → サイドカー初回書き込み）の進捗をステータスバーに表示する。

---

## 機能1: フォルダ右クリックメニューへの JSON 同期項目追加

### UI

`SidebarView.xaml` のフォルダ右クリックメニューに2項目を追加する。

```
名前を変更
──────────
JSON に書き出す     ← DB の内容を tags.json に上書き保存
JSON から取り込む   ← tags.json の内容を強制的に DB に反映
──────────
削除
```

### 動作

| 操作 | 処理 |
|---|---|
| **JSON に書き出す** | 対象フォルダの全 `ImageTags` を DB から読み取り、`tags.json` に上書き保存。`merged_at` キャッシュを更新。 |
| **JSON から取り込む** | `merged_at` キャッシュをリセットし、`MergeIntoDbAsync` を強制実行。DB に最新の `tags.json` 内容を反映。完了後フォルダを再読み込み。 |

### フィードバック

操作完了後、ステータスバーに結果を3秒間表示してからクリアする。

- 書き出し成功: `JSON に書き出しました`
- 取り込み成功: `JSON から取り込みました`
- エラー: `同期に失敗しました`

---

## 機能2: ステータスバーへのステータステキスト追加

### UI

既存の `StatusBar` に状態テキスト項目を追加する。

```
[42 枚] | [フォルダパス]  マイグレーション中...  | サイズ: ─●───
```

空文字のときは表示領域が詰まらないよう `Visibility` で制御する。

---

## アーキテクチャ: AppStatus シングルトン

マイグレーション（`App.xaml.cs`）とコマンド操作（`MainViewModel`）の両方からステータスを書き込めるよう、共有シングルトンを DI に登録する。

```
App.xaml.cs (migration)  ──┐
                            ├──→  AppStatus (INotifyPropertyChanged, DI singleton)
MainViewModel (commands) ──┘        ↓
                                MainWindow.xaml が DataContext を切り替えずに直接バインド
```

`AppStatus` は `StatusMessage` プロパティ（`string`）を持ち、`INotifyPropertyChanged` を実装する。

### ステータス更新タイミング

| イベント | メッセージ |
|---|---|
| マイグレーション開始 | `マイグレーション中...` |
| マイグレーション完了 | `マイグレーション完了` (3秒後クリア) |
| JSON 書き出し開始 | `JSON に書き出し中...` |
| JSON 書き出し完了 | `JSON に書き出しました` (3秒後クリア) |
| JSON 取り込み開始 | `JSON から取り込み中...` |
| JSON 取り込み完了 | `JSON から取り込みました` (3秒後クリア) |
| エラー | `同期に失敗しました` (3秒後クリア) |

---

## 変更ファイル一覧

| ファイル | 変更内容 |
|---|---|
| `AppStatus.cs` (新規) | `StatusMessage` プロパティを持つ DI 登録用シングルトン |
| `ISidecarService.cs` | `ExportToSidecarAsync` と `ForceImportFromSidecarAsync` を追加 |
| `SidecarService.cs` | 上記2メソッドを実装 |
| `MainViewModel.cs` | `ExportToSidecarCommand`・`ImportFromSidecarCommand` を追加。`AppStatus` を注入。3秒後クリアのタイマー処理 |
| `SidebarView.xaml` | 右クリックメニューに2項目を追加 |
| `SidebarView.xaml.cs` | メニュークリックハンドラを追加 |
| `MainWindow.xaml` | StatusBar に `AppStatus.StatusMessage` バインドを追加（`MainWindow.xaml.cs` でコードビハインドから `App.Services` 経由で取得し `Tag` プロパティ経由でバインド） |
| `App.xaml.cs` | `AppStatus` を DI 登録。移行タスクで `StatusMessage` を更新 |

---

## 対象外（スコープ外）

- JSON ファイルの内容をアプリ内で直接編集する機能
- 複数フォルダの一括同期 UI
- 同期履歴ログ
