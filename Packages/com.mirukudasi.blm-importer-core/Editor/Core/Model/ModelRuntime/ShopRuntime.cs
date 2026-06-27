namespace BLMImporter.Editor.Core
{
    /// <summary>ショップのランタイム。現状ローカル派生状態は無く、Master の受け渡しを担う。</summary>
    public sealed class ShopRuntime
    {
        public readonly ShopMaster r_Master;

        public ShopRuntime(ShopMaster master)
        {
            r_Master = master;
        }

        public ShopId Id => r_Master.r_Id;
    }
}
