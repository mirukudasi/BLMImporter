using System;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// アプリ内の通知
    /// 用途不明
    /// </summary>
    public sealed class NotificationMaster
    {
        public readonly NotificationId r_Id;
        public readonly string r_Title;
        public readonly string r_Content;
        public readonly bool r_Read;
        public readonly DateTimeOffset? r_CreatedAt;

        public NotificationMaster(NotificationId id, string title, string content, bool read, string createdAt)
        {
            r_Id = id;
            r_Title = title ?? "";
            r_Content = content ?? "";
            r_Read = read;
            r_CreatedAt = ModelDate.Parse(createdAt);
        }
    }
}
