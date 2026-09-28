using BLMImporter.Editor.Core;

namespace BLMImporter.Editor
{
    /// <summary>並び順 enum の表示名を返す（UI側の表記管理）。</summary>
    internal static class ItemSortModeExtensions
    {
        public static string GetName(this ItemSortMode mode)
        {
            return mode switch
            {
                ItemSortMode.Name => "名前順",
                ItemSortMode.ImportOrder => "インポート順",
                ItemSortMode.Original => "DB登録順",
                _ => mode.ToString()
            };
        }
    }
}
