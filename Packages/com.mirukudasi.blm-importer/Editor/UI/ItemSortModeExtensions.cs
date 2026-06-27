using BLMImporter.Editor.Core;

namespace BLMImporter.Editor
{
    /// <summary>並び順 enum の表示名を返す（UI側の表記管理）。</summary>
    internal static class ItemSortModeExtensions
    {
        public static string GetName(this ItemSortMode mode)
        {
            switch (mode)
            {
                case ItemSortMode.Name:
                    return "名前順";
                case ItemSortMode.ImportOrder:
                    return "インポート順";
                case ItemSortMode.Original:
                    return "DB登録順";
                default:
                    return mode.ToString();
            }
        }
    }
}
