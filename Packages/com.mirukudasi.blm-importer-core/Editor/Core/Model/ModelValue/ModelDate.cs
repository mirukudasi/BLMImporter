using System;
using System.Globalization;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// BのISO8601日時文字列を <see cref="DateTimeOffset"/> へ正規化する
    /// </summary>
    public static class ModelDate
    {
        /// <summary>
        /// 解釈できない・空の場合は null を返す
        /// </summary>
        public static DateTimeOffset? Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) {
                return null;
            }
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)) {
                return result;
            }
            return null;
        }
    }
}
