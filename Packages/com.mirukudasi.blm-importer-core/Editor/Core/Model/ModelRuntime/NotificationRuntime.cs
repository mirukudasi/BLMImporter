namespace BLMImporter.Editor.Core
{
    /// <summary>通知のランタイム。現状ローカル派生状態は無く、Master の受け渡しを担う。</summary>
    public sealed class NotificationRuntime
    {
        public readonly NotificationMaster r_Master;

        public NotificationRuntime(NotificationMaster master)
        {
            r_Master = master;
        }

        public NotificationId Id => r_Master.r_Id;
    }
}
