using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 一覧の絞り込みに使える「リスト」の共通インターフェース
    /// 通常リスト(ItemListRuntime)と条件ベースのスマートリスト(SmartListRuntime)を、
    /// フィルタからは「タイトルを持ち、アイテムが属するか判定できるリスト」として同じに扱う
    /// </summary>
    public interface IItemList
    {
        string Title { get; }
        bool Contains(ItemMaster item);
    }

    public static class IItemListExtensions
    {
        /// <summary>
        /// 候補アイテムのうち、このリストに属するものだけを返す
        /// リストは自分で全アイテムを持たないため、対象集合（通常は snapshot のアイテム）を受け取る
        /// </summary>
        public static IEnumerable<ItemRuntime> GetItemRuntimes(this IItemList list, IEnumerable<ItemRuntime> items)
        {
            return items.Where(item => list.Contains(item.r_Master));
        }

        /// <summary>
        /// 現在の実データ(LibraryRuntimeSnapshot.Current)から、このリストに属するアイテムを返す
        /// 未読込なら空。プレビューはダミーを Current に載せないため、ここは常に実データを見る
        /// </summary>
        public static IEnumerable<ItemRuntime> GetItemRuntimes(this IItemList list)
        {
            var current = LibraryRuntimeSnapshot.Current;
            if (current == null) {
                return Enumerable.Empty<ItemRuntime>();
            }
            return list.GetItemRuntimes(current.r_Items.Values);
        }
    }
}
