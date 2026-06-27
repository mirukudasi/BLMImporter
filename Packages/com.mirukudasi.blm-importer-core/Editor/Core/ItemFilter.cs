using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 選択タグの検索方法
    /// All=すべて含む(AND) / Any=いずれか含む(OR)
    /// </summary>
    public enum TagMatchMode
    {
        AND,
        OR
    }

    /// <summary>
    /// アイテム一覧の絞り込み条件
    /// 空・nullの条件は「すべて」として扱う
    /// </summary>
    public sealed class ItemFilter
    {
        public string m_Keyword = "";
        public string m_SubCategoryName = "";

        // 通常リスト/スマートリストを問わない絞り込み対象リスト。nullで制限なし
        public IItemList m_List = null;
        public bool m_IncludeAdult = true;
        public TagMatchMode m_TagMatchMode = TagMatchMode.AND;

        // 選択タグで絞り込み
        // m_TagMatchMode で AND, OR を切り替える
        public readonly List<string> r_Tags = new List<string>();

        /// <summary>
        /// 条件に合致するアイテムだけを抽出する
        /// 判定は Master の値を見る
        /// </summary>
        public List<ItemRuntime> Apply(IEnumerable<ItemRuntime> items)
        {
            return items
                .Where(item => m_IncludeAdult || !item.r_Master.r_Adult)
                .Where(item => string.IsNullOrEmpty(m_SubCategoryName) || item.r_Master.r_SubCategoryName == m_SubCategoryName)
                .Where(item => m_List == null || m_List.Contains(item.r_Master))
                .Where(item => MatchesTags(item.r_Master.r_Tags) && MatchesKeyword(item.r_Master.r_Name, item.r_Master.r_ShopName, item.r_Master.r_Tags, item.r_Master.r_Id.r_Value))
                .ToList();
        }

        /// <summary>
        /// 選択タグとの一致を判定する
        /// タグ未指定なら常に通す
        /// </summary>
        private bool MatchesTags(IReadOnlyList<string> tags)
        {
            if (r_Tags.Count == 0) {
                return true;
            }
            bool HasTag(string required) => tags.Contains(required, StringComparer.OrdinalIgnoreCase);
            if (m_TagMatchMode == TagMatchMode.OR) {
                return r_Tags.Any(HasTag);
            }
            return r_Tags.All(HasTag);
        }

        /// <summary>
        /// アイテム名・ショップ名・タグ・商品IDのいずれかに部分一致すれば対象
        /// </summary>
        private bool MatchesKeyword(string name, string shopName, IReadOnlyList<string> tags, long id)
        {
            var keyword = m_Keyword.GetKeyword();
            if (keyword.Length == 0) {
                return true;
            }
            var candidates = tags
                .Append(name)
                .Append(shopName)
                .Append(id.ToString());
            return candidates.Any(text => text.ToLowerInvariant().Contains(keyword));
        }
    }
}
