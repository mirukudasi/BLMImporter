using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace BLMImporter.Editor
{
    /// <summary>
    /// 文字列の一覧を区切り文字でつないで SessionState に保存・復元する。
    /// </summary>
    internal static class SessionStateList
    {
        // 保存された一覧を読み出す。空の要素は取り除く。
        public static List<string> Load(string key, char separator)
        {
            var raw = SessionState.GetString(key, "");
            var result = new List<string>();
            if (!string.IsNullOrEmpty(raw))
            {
                result = raw.Split(separator).Where(value => value.Length > 0).ToList();
            }
            return result;
        }

        // 空の一覧は消去する。読み取り側は未保存と空を区別しない
        public static void Save(string key, IEnumerable<string> values, char separator)
        {
            var list = values.ToList();
            if (list.Count > 0)
            {
                SessionState.SetString(key, string.Join(separator.ToString(), list));
            }
            else
            {
                SessionState.EraseString(key);
            }
        }
    }
}
