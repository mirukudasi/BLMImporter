using UnityEditor;

namespace BLMImporter.Editor
{
    /// <summary>利用者へ出す確認・警告ダイアログをまとめる。</summary>
    internal static class BLMDialogs
    {
        public const string c_Title = "BLMImporter";

        // インポート処理中なら警告を出す。処理中だったときは true を返し、呼び出し元は新しいインポートを始めない
        public static bool WarnIfImportRunning()
        {
            var running = SequentialPackageImporter.IsRunning;
            if (running)
            {
                EditorUtility.DisplayDialog(c_Title, "インポート処理中のため、新しいインポートは開始できません。", "OK");
            }
            return running;
        }
    }
}
