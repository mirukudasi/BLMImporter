using System.Collections.Generic;
using System.Linq;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    public partial class BLMImporterWindow
    {
        // ---- フッター（一括インポート） ----

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var toggleSlot = GUILayoutUtility.GetRect(210f, 24f, GUILayout.Width(210f));
                var toggleRect = CenterVertically(toggleSlot.x, toggleSlot.y, toggleSlot.height, 210f, 18f);
                settings.InteractiveImport = EditorGUI.ToggleLeft(toggleRect, "個別のインポートダイアログを表示", settings.InteractiveImport);
                GUILayout.FlexibleSpace();

                var hasSelection = selection.PackageCount > 0 || selection.PendingCount > 0;
                using (new EditorGUI.DisabledScope(!hasSelection || SequentialPackageImporter.IsRunning))
                {
                    if (GUILayout.Button("選択した unitypackage をインポート", GUILayout.Width(220), GUILayout.Height(24)))
                    {
                        OpenImportDialogForSelection();
                    }
                }
            }
        }

        // チェックの入ったアイテムをインポートする。
        // ダウンロード済みと未ダウンロードをまとめてインポートダイアログへ渡す。
        // ダイアログ側で完了を待ち、「インポート開始」で取り込む。
        private void OpenImportDialogForSelection()
        {
            if (BLMDialogs.WarnIfImportRunning())
            {
                return;
            }

            var plan = selection.BuildPlan(snapshot.r_Items.Values);
            var readyPaths = plan.r_Packages.Select(package => package.m_PackagePath).ToList();
            var pendingIds = selection.PendingItemIds.Select(itemId => itemId.r_Value).ToList();
            if (readyPaths.Count == 0 && pendingIds.Count == 0)
            {
                EditorUtility.DisplayDialog(BLMDialogs.c_Title, "インポート対象が選択されていません。", "OK");
                return;
            }

            ImportProgressWindow.Open(pendingIds, readyPaths, settings.InteractiveImport);
            // 取り込みへ渡したので本体側の選択は解除する
            selection.Clear();
        }

        private PackageImportOptions BuildImportOptions()
        {
            return new PackageImportOptions
            {
                m_Interactive = settings.InteractiveImport
            };
        }

        // 1件のインポート要求を直列インポータに渡す（インポートダイアログのスキップを防ぐ）
        private void ImportPackage(string packagePath)
        {
            if (BLMDialogs.WarnIfImportRunning())
            {
                return;
            }

            ImportProgressWindow.RunImmediately(new List<string> { packagePath }, BuildImportOptions());
        }
    }
}
