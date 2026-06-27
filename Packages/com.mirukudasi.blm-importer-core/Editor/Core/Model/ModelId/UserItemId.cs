namespace BLMImporter.Editor.Core
{
    /// <summary>ユーザー追加カスタムアイテムのID（user_item_info.id）。</summary>
    public sealed class UserItemId : ModelIdBase<long>
    {
        public UserItemId(long value) : base(value)
        {
        }

        public override bool IsValid => r_Value > 0;
    }
}
