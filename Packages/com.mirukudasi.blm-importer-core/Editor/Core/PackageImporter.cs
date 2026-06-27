using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// インポート方法の設定
    /// </summary>
    public sealed class PackageImportOptions
    {
        // インポート確認ダイアログを表示する
        public bool m_Interactive = true;
    }

    /// <summary>
    /// インポート対象の unitypackage 1件
    /// </summary>
    public sealed class PackageImportRequest
    {
        public string m_PackagePath = "";
    }

    /// <summary>
    /// 複数アイテムから集めたインポート対象の一覧
    /// </summary>
    public sealed class ItemImportPlan
    {
        public readonly List<PackageImportRequest> r_Packages = new List<PackageImportRequest>();
        // unitypackage を持たないため対象外になったアイテム名
        public readonly List<string> r_ItemNamesWithoutPackage = new List<string>();
    }

    /// <summary>
    /// BOOTHアイテムの unitypackage をUnityプロジェクトへインポートするサービス
    /// エディタウィンドウに依存しないため、他のエディタ拡張からも利用できる
    /// </summary>
    public static class PackageImporter
    {
        /// <summary>
        /// packageFiles が null なら全件、指定があればそのファイルだけを対象に
        /// </summary>
        public static ItemImportPlan BuildPlan(IEnumerable<ItemRuntime> items, ICollection<ItemFile> packageFiles = null)
        {
            var plan = new ItemImportPlan();
            foreach (var item in items) {
                AddItemPackages(plan, item, packageFiles);
            }
            return plan;
        }

        /// <summary>
        /// インポート対象を計画へ加える
        /// 全件モードで unitypackage が無いアイテムは対象外として名前を控える
        /// </summary>
        private static void AddItemPackages(ItemImportPlan plan, ItemRuntime item, ICollection<ItemFile> packageFiles)
        {
            if (packageFiles == null && item.UnityPackageCount == 0) {
                plan.r_ItemNamesWithoutPackage.Add(item.r_Master.r_Name);
            }
            foreach (var file in item.ImportablePackageFiles(packageFiles)) {
                plan.r_Packages.Add(new PackageImportRequest { m_PackagePath = file.r_FullPath });
            }
        }

        /// <summary>
        /// unitypackage を1件インポート
        /// 失敗時は例外を投げる
        /// </summary>
        public static void Import(string packagePath, PackageImportOptions options)
        {
            if (!File.Exists(packagePath)) {
                throw new FileNotFoundException("ファイルが見つかりません: " + packagePath, packagePath);
            }
            AssetDatabase.ImportPackage(packagePath, options.m_Interactive);
        }

        /// <summary>
        /// 先頭が '.' のパッケージを安全にインポートするための固定キャッシュ先。
        /// 直列インポートのため常に1件ずつしか使わず、毎回ここへ上書きコピーする。
        /// </summary>
        public static string ImportCachePath => Path.Combine(Path.GetTempPath(), "BLMImporter-ImportCache.unitypackage");

        /// <summary>
        /// インポートに使うパスを返す。
        /// 先頭が '.' のファイルは Unity が隠しファイル扱いして正しく取り込めず、完了コールバック名も食い違うため、
        /// 固定名の一時コピー（<see cref="ImportCachePath"/>）を作ってそのパスを返す。通常ファイルはそのまま返す。
        /// 一時コピーを作った場合（戻り値 != packagePath）は、呼び出し側がインポート後に削除する。
        /// 残留しても次回のコピーで上書きされるため、最大1件しか残らない。
        /// </summary>
        public static string PrepareImportablePath(string packagePath)
        {
            var fileName = Path.GetFileName(packagePath);
            if (string.IsNullOrEmpty(fileName) || fileName[0] != '.') {
                return packagePath;
            }
            var cachePath = ImportCachePath;
            // 直前の残留があっても上書きする
            File.Copy(packagePath, cachePath, true);
            return cachePath;
        }
    }
}
