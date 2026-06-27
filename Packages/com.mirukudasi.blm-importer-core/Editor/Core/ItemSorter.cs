using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アイテム一覧の並び基準
    /// </summary>
    public enum ItemSortMode
    {
        Name,
        ImportOrder,
        Original
    }

    /// <summary>
    /// アイテム一覧を指定の並び順に並べ替える
    /// </summary>
    public static class ItemSorter
    {
        /// <summary>
        /// mode の基準で並べ替える。descending=true で降順（逆順）にする
        /// 並び基準は Master の値を見る
        /// </summary>
        public static List<ItemRuntime> Sort(IEnumerable<ItemRuntime> items, ItemSortMode mode, bool descending)
        {
            switch (mode) {
                case ItemSortMode.Original:
                    var ordered = items.ToList();
                    if (descending) {
                        ordered.Reverse();
                    }
                    return ordered;
                case ItemSortMode.ImportOrder:
                    // 登録(インポート)日時順。未設定(null)は既定の比較で先頭側に寄る。
                    if (descending) {
                        return items.OrderByDescending(item => item.r_Master.r_RegisteredAt).ToList();
                    }
                    return items.OrderBy(item => item.r_Master.r_RegisteredAt).ToList();
                default:
                    if (descending) {
                        return items.OrderByDescending(item => item.r_Master.r_Name, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                    return items.OrderBy(item => item.r_Master.r_Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }
}
