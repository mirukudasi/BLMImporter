# Changelog

このパッケージの主な変更点を記録します。

## [1.1.0] - 2026-09-29

このリリースには、公開 API の互換性がない変更が含まれます。Core を直接使っている拡張機能は、下の「Changed」「Removed」を確認してください。

### Added

- unitypackage に対して取るべき操作を判定する `ImportedPackageIndex` を追加。
  - `GetActions` に同じアイテムの unitypackage のパスをまとめて渡すと、それぞれの操作(`PackageImportAction`)を返す。プロジェクトに入っている版と作者側の更新時刻が同じなら `Open`、古ければ `Reimport`、新しい・まだ入っていない・調べ中なら `Import`。
  - 入っている版は、収録アセットの GUID から引いたプロジェクト側ファイルの大きさが、書庫内の大きさと一致する割合で推定する。同じアイテム内で GUID が重なる unitypackage 同士は、同じ系列の別の版として見比べる。
  - 照合は GUID で行うため、インポート後にフォルダを移動・改名していても追従する。中身は読まずファイルの大きさだけを比べるため軽い。書庫の読み取りはバックグラウンドで行う。
  - `GetRootAssetPath` で「開く」ときの取り込み先フォルダを、`StateChanged` で判定の変化を受け取れる。
- 読み込みの入口として `LibraryRuntimeSnapshot.Load(databasePath)` と `LibraryRuntimeSnapshot.DefaultDatabasePath` を追加。
- `ItemVariationMaster.r_ItemId` と `SmartListCriteriaMaster.r_SmartListId` を追加。

### Changed

- data.db の読み込みを整理した。各テーブルを1つずつの Master に変換して持つ形になり、1行の読み込みに失敗してもその行だけ飛ばして読み込みを続けるようになった。読み込まれるデータの内容は変わらない。
- Master、`LibraryRuntimeSnapshot`、各 Runtime、`ItemFile`、`Url`、`ItemId` 以外の ID の生成は Core の内部に限った(コンストラクタを internal にした)。拡張機能はこれらを受け取って読むだけになる。
- `ItemFile` は、フルパスとアイテムフォルダから、相対パスと unitypackage かどうかを自分で求めるようにした。

### Removed

- `LibraryData` を廃止。`LibraryData.LoadRuntime(path)` は `LibraryRuntimeSnapshot.Load(path)` に、`LibraryData.DefaultDatabasePath` は `LibraryRuntimeSnapshot.DefaultDatabasePath` に置き換える。
- 内部用の型を公開 API から外した: `MiniSqlite`、`Cache<T>`、`ModelDate`、`UnityPackageArchive`、`UnityPackageAsset`。ダウンロード用ローカルサーバーの内部状態(`BLMDownloadServer` の `Port` / `Token` / `EnsureStarted` / `MarkDownloadStarted`)と `ItemId.Undefined` も公開 API から外した。

## [1.0.1] - 2026-07-19

### Changed

- ダウンロードURLを Booth オーダーページ直接(クエリパラメータ付き)から、BLMDownloader の中継ページ(`mirukudasi.github.io/BLMDownloader/Action/`)経由に変更。Booth のページにクエリパラメータを載せない。拡張機能 Booth Downloader 0.3.0 以降が必要。

## [1.0.0] - 2026-06-27

### Added

- 初回リリース。GUI 非依存の BLM ライブラリ読み込み・絞り込み・unitypackage インポート API。
