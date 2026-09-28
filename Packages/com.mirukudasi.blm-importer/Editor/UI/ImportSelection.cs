using System.Collections.Generic;
using System.Linq;
using BLMImporter.Editor.Core;

namespace BLMImporter.Editor
{
    /// <summary>
    /// アイテム一覧と詳細ペインで選んだインポート対象を保持する。
    /// ダウンロード済みのアイテムは unitypackage 単位、未ダウンロードのアイテムはアイテム単位で選択を持つ。
    /// </summary>
    internal sealed class ImportSelection
    {
        // インポート対象として選択中の unitypackage（ItemFile）。選択状態の唯一の正本。
        // アイテム一覧のチェックは、そのアイテム配下の選択数から導出する
        private readonly HashSet<ItemFile> r_Packages = new HashSet<ItemFile>();
        // 未ダウンロードのまま選択されたアイテムID（ダウンロード待ち対象）
        private readonly HashSet<ItemId> r_PendingItemIds = new HashSet<ItemId>();

        public int PackageCount => r_Packages.Count;

        public int PendingCount => r_PendingItemIds.Count;

        public IEnumerable<ItemId> PendingItemIds => r_PendingItemIds;

        public bool Contains(ItemFile file)
        {
            return r_Packages.Contains(file);
        }

        public bool ContainsPending(ItemId itemId)
        {
            return r_PendingItemIds.Contains(itemId);
        }

        public void SetPackage(ItemFile file, bool selected)
        {
            if (selected)
            {
                r_Packages.Add(file);
            }
            else
            {
                r_Packages.Remove(file);
            }
        }

        public void SetPackages(List<ItemFile> packages, bool selected)
        {
            foreach (var file in packages)
            {
                SetPackage(file, selected);
            }
        }

        public void SetPending(ItemId itemId, bool selected)
        {
            if (selected)
            {
                r_PendingItemIds.Add(itemId);
            }
            else
            {
                r_PendingItemIds.Remove(itemId);
            }
        }

        // アイテム配下で選択中の unitypackage 数
        public int SelectedPackageCount(ItemRuntime item)
        {
            return item.ImportablePackageFiles(r_Packages).Count();
        }

        // 全選択チェックボックスに使う対象数と選択数を数える
        public (int Total, int Selected) CountForSelectAll(List<ItemRuntime> items)
        {
            var total = 0;
            var selected = 0;
            foreach (var item in items)
            {
                if (item.IsImportable)
                {
                    total += item.UnityPackageCount;
                    selected += SelectedPackageCount(item);
                }
                else
                {
                    total += 1;
                    if (r_PendingItemIds.Contains(item.Id))
                    {
                        selected += 1;
                    }
                }
            }
            return (total, selected);
        }

        // アイテムが選択されているか（ダウンロード済は1つ以上のpackage、未ダウンロードはアイテム単位）
        public bool IsItemSelected(ItemRuntime item)
        {
            if (item.IsImportable)
            {
                return SelectedPackageCount(item) > 0;
            }
            return r_PendingItemIds.Contains(item.Id);
        }

        // フィルタ後の全アイテムを選択／解除する（ダウンロード済はpackage、未ダウンロードはアイテム単位）
        public void SetAll(List<ItemRuntime> items, bool selected)
        {
            foreach (var item in items)
            {
                if (item.IsImportable)
                {
                    SetPackages(item.UnityPackages.ToList(), selected);
                }
                else
                {
                    SetPending(item.Id, selected);
                }
            }
        }

        // ダブルクリックでの選択トグル。未ダウンロードはアイテム単位、それ以外は配下全パッケージ。
        public void Toggle(ItemRuntime item)
        {
            if (!item.IsImportable)
            {
                SetPending(item.Id, !ContainsPending(item.Id));
            }
            else
            {
                var packages = item.UnityPackages.ToList();
                var allSelected = packages.Count > 0 && SelectedPackageCount(item) == packages.Count;
                SetPackages(packages, !allSelected);
            }
        }

        // 再読み込み後、選択中の unitypackage / 未DL選択アイテムのうち、新スナップショットに残っているものだけ保持する
        public void PruneTo(LibraryRuntimeSnapshot snapshot)
        {
            var existingFiles = new HashSet<ItemFile>(snapshot.r_Items.Values.SelectMany(item => item.UnityPackages));
            r_Packages.IntersectWith(existingFiles);
            r_PendingItemIds.IntersectWith(snapshot.r_Items.Keys);
        }

        // インポートへ渡した選択（unitypackage・未DL）を解除する
        public void Clear()
        {
            r_Packages.Clear();
            r_PendingItemIds.Clear();
        }

        public ItemImportPlan BuildPlan(IEnumerable<ItemRuntime> items)
        {
            return PackageImporter.BuildPlan(items, r_Packages);
        }
    }
}
