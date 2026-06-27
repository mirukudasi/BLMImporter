using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// <see cref="ItemMaster"/> を参照し、ローカルファイル状態と軽い派生処理を付与するランタイムモデル。
    /// 同じアイテムでもライブラリフォルダの状況で変わる値（ファイル一覧・所持状況）はこちらが持つ。
    /// </summary>
    public sealed class ItemRuntime
    {
        public readonly ItemMaster r_Master;
        public readonly string r_FolderPath;
        public readonly bool r_FolderExists;
        public readonly IReadOnlyList<ItemFile> r_Files;
        // r_Files のうち unitypackage のものを一度だけ抽出してキャッシュ
        private readonly IReadOnlyList<ItemFile> r_UnityPackages;

        public ItemRuntime(ItemMaster master, string folderPath, bool folderExists, IEnumerable<ItemFile> files)
        {
            r_Master = master;
            r_FolderPath = folderPath ?? "";
            r_FolderExists = folderExists;
            r_Files = (files ?? Enumerable.Empty<ItemFile>()).ToArray();
            r_UnityPackages = r_Files.Where(file => file.r_IsUnityPackage).ToArray();
        }

        public ItemId Id => r_Master.r_Id;

        /// <summary>
        /// このアイテムが持つ unitypackage ファイルの一覧
        /// </summary>
        public IReadOnlyList<ItemFile> UnityPackages => r_UnityPackages;
        public int UnityPackageCount => r_UnityPackages.Count;
        /// <summary>
        /// インポート可能か
        /// </summary>
        public bool IsImportable => PackageStatus == ItemPackageStatus.HasUnityPackage;

        public ItemPackageStatus PackageStatus {
            get {
                if (!r_FolderExists) {
                    return ItemPackageStatus.NotDownloaded;
                }
                if (r_UnityPackages.Count > 0) {
                    return ItemPackageStatus.HasUnityPackage;
                }
                return ItemPackageStatus.NoUnityPackage;
            }
        }
    }

    /// <summary>
    /// アイテムのダウンロード・unitypackage所持状況
    /// </summary>
    public enum ItemPackageStatus
    {
        NotDownloaded,
        NoUnityPackage,
        HasUnityPackage
    }

    /// <summary>
    /// アイテム配下で見つかった実ファイル1件
    /// </summary>
    public sealed class ItemFile : IEquatable<ItemFile>
    {
        public readonly string r_FullPath;
        public readonly string r_RelativePath;
        public readonly bool r_IsUnityPackage;

        public ItemFile(string fullPath, string relativePath, bool isUnityPackage)
        {
            r_FullPath = fullPath ?? "";
            r_RelativePath = relativePath ?? "";
            r_IsUnityPackage = isUnityPackage;
        }

        // ファイルの同一性はフルパスで決まる。リロードやウィンドウ跨ぎで別インスタンスになっても
        // 同じ実体を指すものは等価として扱えるよう値等価にする（選択集合のキーに使うため）。
        public bool Equals(ItemFile other) => other != null && string.Equals(r_FullPath, other.r_FullPath, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as ItemFile);
        public override int GetHashCode() => r_FullPath.GetHashCode();
    }

    /// <summary>
    /// ItemRuntime からアイテム単位の派生値を取り出す拡張
    /// </summary>
    public static class ItemRuntimeExtensions
    {
        /// <summary>
        /// この item の unitypackage のうち packageFiles に含まれるものを返す
        /// packageFiles == null ですべて
        /// </summary>
        public static IEnumerable<ItemFile> ImportablePackageFiles(this ItemRuntime item, ICollection<ItemFile> packageFiles = null)
        {
            foreach (var file in item.UnityPackages) {
                if (packageFiles == null || packageFiles.Contains(file)) {
                    yield return file;
                }
            }
        }

        public static string ItemPageUrl(this ItemRuntime item) => "https://booth.pm/ja/items/" + item.r_Master.r_Id.r_Value;

        public static string ShopPageUrl(this ItemRuntime item) => "https://" + item.r_Master.r_ShopId.r_Value + ".booth.pm/";

        public static string OrderPageUrl(this OrderId orderId) => "https://accounts.booth.pm/orders/" + orderId.r_Value;

        // 指定注文に含まれる各バリエーションを {itemid, variationid} の配列にしてオーダーページURLへ付ける。
        // あわせてライブラリの絶対パスと、完了通知サーバの接続情報（ポート/トークン）を載せる。
        // 副作用を持たない純粋なURL組み立て。サーバ起動やロックは呼び出し側で行う。
        // ショップ名（subdomain）は後から変わりうるため含めない。
        public static string DownloadUrl(this ItemRuntime item, OrderId orderId, int completionPort, string completionToken) {
            var entries = item.r_Master.r_Variations
                .Where(variation => variation.r_OrderId.Equals(orderId))
                .Select(variation => "{itemid: " + item.r_Master.r_Id.r_Value + ", variationid: " + variation.r_Id.r_Value + "}");
            var payload = "[" + string.Join(", ", entries) + "]";
            var libraryPath = LibraryRuntimeSnapshot.Current.r_LibraryPath;
            return orderId.OrderPageUrl()
                + "?BLMImporterDLtargets=" + Uri.EscapeDataString(payload)
                + "&BLMImporterLibraryPath=" + Uri.EscapeDataString(libraryPath)
                + "&BLMImporterPort=" + completionPort
                + "&BLMImporterToken=" + Uri.EscapeDataString(completionToken);
        }
    }
}
