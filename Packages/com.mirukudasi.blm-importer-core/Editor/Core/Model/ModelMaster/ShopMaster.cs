namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// ショップマスタ
    /// subdomain が自然キー
    /// </summary>
    public sealed class ShopMaster : ILibraryMasterRow
    {
        public readonly ShopId r_Id;
        public readonly string r_Subdomain;
        public readonly string r_Name;
        public readonly Url r_ThumbnailUrl;

        internal ShopMaster(MiniSqlite.Row row)
        {
            r_Subdomain = row.GetString("subdomain") ?? "";
            r_Id = new ShopId(r_Subdomain);
            r_Name = row.GetString("name") ?? r_Subdomain;
            r_ThumbnailUrl = new Url(row.GetString("thumbnail_url") ?? "");
        }

        bool ILibraryMasterRow.IsValid => !string.IsNullOrEmpty(r_Subdomain);
    }
}
