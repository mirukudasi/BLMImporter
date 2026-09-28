using System;
using System.Collections.Generic;
using System.Linq;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// スマートリストの絞り込み条件
    /// 個別参照されないため独自IDは持たない
    /// </summary>
    public sealed class SmartListCriteriaMaster : ILibraryMasterRow
    {
        // category_id は parent_categories、subcategory_id は sub_categories を指す
        public readonly ParentCategoryId r_CategoryId;
        public readonly SubCategoryId r_SubCategoryId;
        public readonly string r_Text;
        // 年齢制限フィルタ（"safe" など）
        public readonly string r_AgeRestriction;
        public readonly SmartListId r_SmartListId;

        internal SmartListCriteriaMaster(MiniSqlite.Row row)
        {
            r_CategoryId = new ParentCategoryId(row.GetLong("category_id", 0));
            r_SubCategoryId = new SubCategoryId(row.GetLong("subcategory_id", 0));
            r_Text = row.GetString("text") ?? "";
            r_AgeRestriction = row.GetString("age_restriction") ?? "";
            r_SmartListId = new SmartListId(row.GetLong("smart_list_id", 0));
        }

        bool ILibraryMasterRow.IsValid => true;
    }

    /// <summary>
    /// 条件で自動的に構成されるスマートリスト
    /// </summary>
    public sealed class SmartListMaster : ILibraryMasterRow
    {
        public readonly SmartListId r_Id;
        public readonly string r_Title;
        public readonly string r_Description;
        public readonly DateTimeOffset? r_CreatedAt;
        public readonly DateTimeOffset? r_UpdatedAt;
        public readonly IReadOnlyList<SmartListCriteriaMaster> r_Criteria;
        public readonly IReadOnlyList<string> r_Tags;

        internal SmartListMaster(MiniSqlite.Row row, LibraryMaster library)
        {
            r_Id = new SmartListId(row.m_RowId);
            r_Title = row.GetString("title") ?? "";
            r_Description = row.GetString("description") ?? "";
            r_CreatedAt = ModelDate.Parse(row.GetString("created_at") ?? "");
            r_UpdatedAt = ModelDate.Parse(row.GetString("updated_at") ?? "");
            // smart_lists 本体に、条件(smart_list_criteria)とタグ(smart_list_tags)を紐付ける
            r_Criteria = library.r_CriteriaBySmartList[r_Id].ToArray();
            r_Tags = library.r_TagsBySmartList[r_Id].Select(tag => tag.r_Tag).ToArray();
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
