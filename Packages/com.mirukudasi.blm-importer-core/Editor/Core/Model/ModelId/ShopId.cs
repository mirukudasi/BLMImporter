namespace BLMImporter.Editor.Core
{
    /// <summary>ショップのID（shops.subdomain が自然キー）。小文字に正規化して扱う。</summary>
    public sealed class ShopId : ModelIdBase<string>
    {
        public ShopId(string subdomain) : base(Normalize(subdomain))
        {
        }

        public override bool IsValid => !string.IsNullOrEmpty(r_Value);

        private static string Normalize(string subdomain)
        {
            if (string.IsNullOrEmpty(subdomain)) {
                return "";
            }
            return subdomain.Trim().ToLowerInvariant();
        }
    }
}
