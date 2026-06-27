# BLMImporter.Editor.Core 呼び出しガイド

BOOTH Library Manager のライブラリ(data.db)を読み込み、unitypackage のインポートまでを行うエディタ用APIです。
GUIに依存しないため、任意のエディタ拡張から利用できます。

- 名前空間: `BLMImporter.Editor.Core`
- 前提: [BOOTH Library Manager](https://booth.pm/library_manager) がインストール済みで、データベース(data.db)が存在すること
- すべてメインスレッド(エディタ)での呼び出しを想定
- 失敗は例外で通知されます。ダイアログ表示などのUI処理は呼び出し側で行ってください

## クイックスタート

```csharp
using System.Linq;
using BLMImporter.Editor.Core;

// 1. ライブラリを読み込む
var snapshot = LibraryData.Load(LibraryData.DefaultDatabasePath);

// 2. 条件で絞り込む
var filter = new ItemFilter {
    Keyword = "衣装",
    m_IncludeAdult = false
};
var items = filter.Apply(snapshot.m_Items);

// 3. unitypackage を持つアイテムをインポートする
var options = new PackageImportOptions {
    m_Interactive = true        // インポート確認ダイアログを表示
};
var plan = PackageImporter.BuildPlan(items);
foreach (var request in plan.m_Packages) {
    PackageImporter.Import(request.m_PackagePath, options);
}
```

## LibraryData — ライブラリの読み込み

| メンバー | 説明 |
|---|---|
| `string DefaultDatabasePath` | 標準的な data.db の場所 (`%AppData%/pm.booth.library-manager/data.db`) |
| `LibrarySnapshot Load(string databasePath)` | DBを読み込み、ライブラリフォルダを走査してスナップショットを返す |

- DBが存在しない・形式が不正な場合は例外を投げます (`FileNotFoundException` / `InvalidDataException` など)
- 読み込み後の `LibrarySnapshot.m_LibraryPath` が空、またはフォルダが存在しない場合は、アプリ側でライブラリフォルダが未設定の状態です

## モデル

`Load` が返すデータはすべて読み取り専用のスナップショットです(再読み込みするまで実ファイルの変化は反映されません)。

### LibrarySnapshot
- `m_LibraryPath` — ライブラリフォルダのパス
- `m_Theme` / `m_Language` — アプリ設定(preferences)
- `m_Items` — 全アイテム (DB登録順。並べ替えは `ItemSorter`)
- `m_Lists` — ユーザーが作成したリスト(お気に入り集合)
- `m_Shops` — 全ショップ(shops)
- `m_AllTags` — マスタのタグ一覧(booth_tags)
- `m_Notifications` — アプリ内通知(notifications)
- `m_SmartLists` — 条件ベースの自動リスト(smart_lists)
- `m_UserItems` — ユーザーが手動追加したカスタムアイテム(user_item_info)

### Item
- 基本情報: `m_Id` / `m_Name` / `m_ShopName` / `m_ShopSubdomain` / `m_ShopThumbnailUrl` / `m_Adult` / `m_Description` / `m_ThumbnailUrl`
- カテゴリ: `m_SubCategoryId` / `m_SubCategoryName` / `m_ParentCategoryId` / `m_ParentCategoryName`
- 日時: `m_PublishedAt`(BOOTH公開) / `m_UpdatedAt`(BOOTHメタ更新) / `m_LibraryUpdatedAt`(ライブラリ側更新) / `m_RegisteredAt`(ライブラリ登録)
- 付帯情報: `m_Tags` / `m_OrderIds` / `m_Variations`
- ローカルファイル: `m_FolderPath` / `m_FolderExists` / `m_Files` (unitypackage が先頭に並ぶ)
- `UnityPackages` — `m_Files` のうち unitypackage のみ
- `PackageStatus` — 状態を表す enum

```csharp
public enum ItemPackageStatus
{
    NotDownloaded,   // ライブラリフォルダ未ダウンロード
    NoUnityPackage,  // ダウンロード済みだが unitypackage なし
    HasUnityPackage  // インポート可能
}
```

### ItemFile
- `m_FullPath` / `m_RelativePath` (アイテムフォルダ基準) / `m_IsUnityPackage`

### ItemVariation
- `m_VariationId` / `m_OrderId` / `m_VariationName` (注文単位のダウンロード対象)

### ItemList
- `m_Id` / `m_Title` / `m_Description` / `m_CreatedAt` / `m_UpdatedAt` / `m_ItemIds` (所属アイテムの商品ID集合)

### Shop
- `m_Subdomain` / `m_Name` / `m_ThumbnailUrl`

### Notification
- `m_Id` / `m_Title` / `m_Content` (JSON文字列のことが多い) / `m_Read` / `m_CreatedAt`

## ItemFilter — 絞り込み

条件を設定して `Apply` に渡すだけです。**空・null の条件は「すべて」として扱われます。**

| フィールド | 効果 |
|---|---|
| `m_Keyword` | アイテム名・ショップ名・タグ・商品IDへの部分一致 (大文字小文字を区別しない) |
| `m_SubCategoryName` | サブカテゴリ名の完全一致 |
| `m_List` | 指定リストに含まれるアイテムのみ |
| `m_IncludeAdult` | false で R-18 アイテムを除外 |
| `m_Tags` | 指定したタグで絞り込む (大文字小文字を区別しない) |
| `m_TagMatchMode` | `m_Tags` の一致方法。`All`=すべて含む(AND) / `Any`=いずれか含む(OR) |

```csharp
var favorites = snapshot.m_Lists.FirstOrDefault(list => list.m_Title == "お気に入り");
var filter = new ItemFilter { m_List = favorites };
var items = filter.Apply(snapshot.m_Items);
```

## PackageImporter — インポート

| メンバー | 説明 |
|---|---|
| `ItemImportPlan BuildPlan(IEnumerable<Item> items)` | アイテム群が持つ unitypackage を **すべて** 対象にした計画を組み立てる |
| `ItemImportPlan BuildPlan(IEnumerable<Item> items, ICollection<string> packagePaths)` | 指定フルパスの unitypackage **だけ** を対象にした計画を組み立てる |
| `void Import(string packagePath, PackageImportOptions options)` | unitypackage を1件インポートする。失敗時は例外 |

- `ItemImportPlan.m_Packages` — インポート対象 (`m_PackagePath`)
- `ItemImportPlan.m_ItemNamesWithoutPackage` — unitypackage が無く対象外になったアイテム名 (ユーザーへの通知用)
- `PackageImportOptions.m_Interactive` — Unity標準のインポート確認ダイアログを表示するか

```csharp
try {
    PackageImporter.Import(path, options);
} catch (Exception exception) {
    // ファイル欠落・パッケージ破損など。通知は呼び出し側の責務
    Debug.LogException(exception);
}
```

## ThumbnailCache — サムネイル取得

サムネイルをディスク(`Application.persistentDataPath/BLMThumbs`)とメモリにキャッシュし、未取得分はBOOTHから直列ダウンロードします。

| メンバー | 説明 |
|---|---|
| `Texture2D Get(string url)` | キャッシュ済みならテクスチャを返し、無ければダウンロード予約して null を返す |
| `void Update()` | ダウンロードを進める。`EditorApplication.update` などから毎フレーム呼ぶ |
| `event Action Repaint` | ダウンロード完了時に発火。ウィンドウの再描画に繋ぐ |
| `int RemainingCount` | 進行中・待機中のダウンロード件数 |
| `bool CanRequestFetch` | キャッシュ更新を実行できるか (前回実行から1日経過まで false) |
| `void RequestFetchNow()` | キャッシュ更新を宣言。以降 `Get` 時に古いサムネイルが再取得される |

```csharp
private ThumbnailCache thumbnails;

private void OnEnable()
{
    thumbnails = new ThumbnailCache();
    thumbnails.Repaint += Repaint;
    EditorApplication.update += thumbnails.Update;
}

private void OnDisable()
{
    thumbnails.Repaint -= Repaint;
    EditorApplication.update -= thumbnails.Update;
}

// OnGUI内: 表示中のアイテムだけ Get を呼ぶ (呼んだURLだけがダウンロードされる)
var texture = thumbnails.Get(item.m_ThumbnailUrl);
```

- サーバー負荷を避けるためダウンロードは同時1件・0.2秒間隔です。`Get` は画面に見えている分だけ呼んでください

## 低レベルAPI (通常は直接使わない)

- `MiniSqlite` — ネイティブライブラリ不要の読み取り専用SQLiteリーダー。`HasTable(name)` と `SelectAll(name)` で data.db の任意テーブルを読める

```csharp
var db = new MiniSqlite(LibraryData.DefaultDatabasePath);
foreach (var row in db.SelectAll("shops")) {
    Debug.Log(row.GetString("name"));
}
```
