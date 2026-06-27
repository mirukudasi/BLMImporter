namespace BLMImporter.Editor {
    internal static class StringExtensions {
        // 検索用にトリム＋小文字化した文字列を返す（null安全）
        public static string GetKeyword(this string val) => (val ?? "").Trim().ToLowerInvariant();
    }
}