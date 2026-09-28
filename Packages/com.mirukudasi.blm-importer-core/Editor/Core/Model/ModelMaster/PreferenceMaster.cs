using System.Text;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// BOOTH Library Manager の設定
    /// ライブラリフォルダの場所・テーマ・言語を持つ
    /// </summary>
    internal sealed class PreferenceMaster : ILibraryMasterRow
    {
        public readonly string r_LibraryPath = "";
        public readonly string r_Theme = "";
        public readonly string r_Language = "";

        internal PreferenceMaster()
        {
        }

        internal PreferenceMaster(MiniSqlite.Row row)
        {
            r_LibraryPath = ReadLibraryPathValue(row);
            r_Theme = row.GetString("theme") ?? "";
            r_Language = row.GetString("language") ?? "";
        }

        bool ILibraryMasterRow.IsValid => true;

        private static string ReadLibraryPathValue(MiniSqlite.Row row)
        {
            var found = row.Columns.TryGetValue("item_directory_path", out var value);
            if (!found || value == null) {
                return "";
            }
            // BLOB(UTF-16LE) で格納されている。文字列で来た場合はそのまま使う。
            if (value is byte[] blob) {
                return Encoding.Unicode.GetString(blob);
            }
            return value.ToString();
        }
    }
}
