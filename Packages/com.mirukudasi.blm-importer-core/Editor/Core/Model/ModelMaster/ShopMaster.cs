namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ショップマスタ
    /// subdomain が自然キー
    /// </summary>
    public sealed class ShopMaster
    {
        public readonly ShopId r_Id;
        public readonly string r_Subdomain;
        public readonly string r_Name;
        public readonly Url r_ThumbnailUrl;

        public ShopMaster(string subdomain, string name, string thumbnailUrl)
        {
            r_Subdomain = subdomain ?? "";
            r_Id = new ShopId(r_Subdomain);
            r_Name = name ?? "";
            r_ThumbnailUrl = new Url(thumbnailUrl);
        }
    }
}
