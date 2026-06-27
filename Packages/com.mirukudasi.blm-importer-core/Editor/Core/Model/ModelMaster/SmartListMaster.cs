using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// スマートリストの絞り込み条件
    /// 個別参照されないため独自IDは持たない
    /// </summary>
    public sealed class SmartListCriteriaMaster
    {
        // category_id は parent_categories、subcategory_id は sub_categories を指す
        public readonly ParentCategoryId r_CategoryId;
        public readonly SubCategoryId r_SubCategoryId;
        public readonly string r_Text;
        // 年齢制限フィルタ（"safe" など）
        public readonly string r_AgeRestriction;

        public SmartListCriteriaMaster(ParentCategoryId categoryId, SubCategoryId subCategoryId, string text, string ageRestriction)
        {
            r_CategoryId = categoryId;
            r_SubCategoryId = subCategoryId;
            r_Text = text ?? "";
            r_AgeRestriction = ageRestriction ?? "";
        }
    }

    /// <summary>
    /// 条件で自動的に構成されるスマートリスト
    /// </summary>
    public sealed class SmartListMaster
    {
        public readonly SmartListId r_Id;
        public readonly string r_Title;
        public readonly string r_Description;
        public readonly DateTimeOffset? r_CreatedAt;
        public readonly DateTimeOffset? r_UpdatedAt;
        public readonly IReadOnlyList<SmartListCriteriaMaster> r_Criteria;
        public readonly IReadOnlyList<string> r_Tags;

        public SmartListMaster(
            SmartListId id, string title, string description, string createdAt, string updatedAt,
            IEnumerable<SmartListCriteriaMaster> criteria, IEnumerable<string> tags)
        {
            r_Id = id;
            r_Title = title ?? "";
            r_Description = description ?? "";
            r_CreatedAt = ModelDate.Parse(createdAt);
            r_UpdatedAt = ModelDate.Parse(updatedAt);
            r_Criteria = (criteria ?? Enumerable.Empty<SmartListCriteriaMaster>()).ToArray();
            r_Tags = (tags ?? Enumerable.Empty<string>()).ToArray();
        }
    }
}
