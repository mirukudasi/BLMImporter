using System;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// スマートリストのランタイム
    /// 条件に合致するアイテムを判定する機能を持つ
    /// </summary>
    public sealed class SmartListRuntime : IItemList
    {
        public readonly SmartListMaster r_Master;
        public SmartListId Id => r_Master.r_Id;
        public string Title => r_Master.r_Title;

        public SmartListRuntime(SmartListMaster master)
        {
            r_Master = master;
        }

        /// <summary>
        /// このアイテムがスマートリストに含まれるか。
        /// criteria はいずれかに合致(OR)、tags はすべて含む(AND)を必須とする。未指定は制約なし。
        /// </summary>
        public bool Contains(ItemMaster item)
        {
            if (item == null) {
                return false;
            }
            var criteriaOk = r_Master.r_Criteria.Count == 0 || r_Master.r_Criteria.Any(criteria => MatchesCriteria(criteria, item));
            var tagsOk = r_Master.r_Tags.All(tag => item.r_Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            return criteriaOk && tagsOk;
        }

        // 条件1件に合致するか。無効(=0)のID条件・空の年齢制限は制約なしとして通す。
        private static bool MatchesCriteria(SmartListCriteriaMaster criteria, ItemMaster item)
        {
            var categoryOk = !criteria.r_CategoryId.IsValid || item.r_ParentCategoryId.Equals(criteria.r_CategoryId);
            var subCategoryOk = !criteria.r_SubCategoryId.IsValid || item.r_SubCategoryId.Equals(criteria.r_SubCategoryId);
            var ageOk = MatchesAgeRestriction(criteria.r_AgeRestriction, item.r_Adult);
            var textOk = MatchesText(criteria.r_Text, item);
            return categoryOk && subCategoryOk && ageOk && textOk;
        }

        // 条件テキストはアイテム名・ショップ名・説明・タグのいずれかへの部分一致で判定する。空なら無制限。
        private static bool MatchesText(string text, ItemMaster item)
        {
            var keyword = text.GetKeyword();
            if (keyword.Length == 0) {
                return true;
            }
            if (item.r_Name.ToLowerInvariant().Contains(keyword)) {
                return true;
            }
            if (item.r_ShopName.ToLowerInvariant().Contains(keyword)) {
                return true;
            }
            if (item.r_Description.ToLowerInvariant().Contains(keyword)) {
                return true;
            }
            return item.r_Tags.Any(tag => tag.ToLowerInvariant().Contains(keyword));
        }

        // age_restriction: "safe"=非アダルトのみ / "r18"系=アダルトのみ / それ以外=制限なし
        private static bool MatchesAgeRestriction(string ageRestriction, bool adult)
        {
            var age = ageRestriction.GetKeyword();
            if (age == "safe") {
                return !adult;
            }
            if (age.Contains("18") || age.Contains("adult")) {
                return adult;
            }
            return true;
        }
    }
}
