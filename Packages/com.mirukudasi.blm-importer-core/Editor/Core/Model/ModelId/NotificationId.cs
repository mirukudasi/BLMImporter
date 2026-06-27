namespace BLMImporter.Editor.Core
{
    /// <summary>アプリ内通知のID（notifications.id）。</summary>
    public sealed class NotificationId : ModelIdBase<long>
    {
        public NotificationId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
