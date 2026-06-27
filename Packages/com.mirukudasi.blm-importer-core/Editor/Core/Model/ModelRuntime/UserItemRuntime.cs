namespace BLMImporter.Editor.Core
{
    /// <summary>カスタムアイテムのランタイム。現状ローカル派生状態は無く、Master の受け渡しを担う。</summary>
    public sealed class UserItemRuntime
    {
        public readonly UserItemMaster r_Master;

        public UserItemRuntime(UserItemMaster master)
        {
            r_Master = master;
        }

        public UserItemId Id => r_Master.r_Id;
    }
}
