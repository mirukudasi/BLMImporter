namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ユーザー作成リストのランタイム
    /// アイテムが属するかの判定機能を持つ
    /// </summary>
    public sealed class ItemListRuntime : IItemList
    {
        public readonly ItemListMaster r_Master;
        public ItemListId Id => r_Master.r_Id;
        public string Title => r_Master.r_Title;

        public ItemListRuntime(ItemListMaster master)
        {
            r_Master = master;
        }

        /// <summary>このリストに該当アイテムが属するか。</summary>
        public bool Contains(ItemMaster item)
        {
            return item != null && r_Master.r_ItemIds.Contains(item.r_Id);
        }
    }
}
