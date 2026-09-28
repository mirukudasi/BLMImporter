using System;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アプリ内の通知
    /// 用途不明
    /// </summary>
    public sealed class NotificationMaster : ILibraryMasterRow
    {
        public readonly NotificationId r_Id;
        public readonly string r_Title;
        public readonly string r_Content;
        public readonly bool r_Read;
        public readonly DateTimeOffset? r_CreatedAt;

        internal NotificationMaster(MiniSqlite.Row row)
        {
            r_Id = new NotificationId(row.m_RowId);
            r_Title = row.GetString("title") ?? "";
            r_Content = row.GetString("content") ?? "";
            r_Read = row.GetLong("read", 0) != 0;
            r_CreatedAt = ModelDate.Parse(row.GetString("created_at") ?? "");
        }

        bool ILibraryMasterRow.IsValid => true;
    }
}
