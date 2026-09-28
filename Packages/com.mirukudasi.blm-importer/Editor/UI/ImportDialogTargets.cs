using System;
using System.Collections.Generic;
using BLMImporter.Editor.Core;
using UnityEditor;

namespace BLMImporter.Editor
{
    /// <summary>
    /// インポートダイアログの対象を、ドメインリロードを跨いで SessionState に保持する。
    /// </summary>
    internal static class ImportDialogTargets
    {
        // リロードを跨いで保持する対象。未DLアイテムID／DL済み未取り込みパス／取り込み前に除外したパス／対話インポート設定。
        private const string c_KeyPending = "BLMImporter.ImportDialog.Pending";
        private const string c_KeyReady = "BLMImporter.ImportDialog.Ready";
        private const string c_KeyExcluded = "BLMImporter.ImportDialog.Excluded";
        private const string c_KeyInteractive = "BLMImporter.ImportDialog.Interactive";
        private const char c_PathSeparator = '\n';

        public static bool HasPending()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(c_KeyPending, ""));
        }

        public static bool HasReady()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(c_KeyReady, ""));
        }

        public static List<long> LoadPending()
        {
            var raw = SessionState.GetString(c_KeyPending, "");
            var result = new List<long>();
            if (string.IsNullOrEmpty(raw))
            {
                return result;
            }
            foreach (var part in raw.Split(','))
            {
                if (long.TryParse(part, out var id))
                {
                    result.Add(id);
                }
            }
            return result;
        }

        public static void SavePending(List<long> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                SessionState.EraseString(c_KeyPending);
                return;
            }
            SessionState.SetString(c_KeyPending, string.Join(",", ids));
        }

        public static List<string> LoadReady()
        {
            return SessionStateList.Load(c_KeyReady, c_PathSeparator);
        }

        public static void SaveReady(List<string> paths)
        {
            SessionStateList.Save(c_KeyReady, paths, c_PathSeparator);
        }

        public static HashSet<string> LoadExcluded()
        {
            return new HashSet<string>(SessionStateList.Load(c_KeyExcluded, c_PathSeparator), StringComparer.Ordinal);
        }

        public static void SaveExcluded(HashSet<string> paths)
        {
            SessionStateList.Save(c_KeyExcluded, paths, c_PathSeparator);
        }

        public static bool LoadInteractive()
        {
            return SessionState.GetBool(c_KeyInteractive, true);
        }

        // 新しいダイアログの対象を設定する。取り込み前の除外は持ち越さない。
        public static void SetTargets(IEnumerable<long> pendingItemIds, IEnumerable<string> readyPaths, bool interactive)
        {
            SessionState.SetString(c_KeyPending, string.Join(",", pendingItemIds));
            SessionStateList.Save(c_KeyReady, readyPaths, c_PathSeparator);
            SessionState.EraseString(c_KeyExcluded);
            SessionState.SetBool(c_KeyInteractive, interactive);
        }

        public static void Clear()
        {
            SessionState.EraseString(c_KeyPending);
            SessionState.EraseString(c_KeyReady);
            SessionState.EraseString(c_KeyExcluded);
        }

        public static void ClearReadyAndExcluded()
        {
            SessionState.EraseString(c_KeyReady);
            SessionState.EraseString(c_KeyExcluded);
        }

        // インポート可能（unitypackageが現れた）になった未DLアイテムの unitypackage を「DL済み未取り込み」へ移す。
        // まだ未DL・ダウンロード済みでもunitypackageが無いものは待ちに残す。
        public static void PromoteDownloaded(LibraryRuntimeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            var pending = LoadPending();
            if (pending.Count == 0)
            {
                return;
            }
            var ready = LoadReady();
            var stillPending = new List<long>();
            foreach (var id in pending)
            {
                var item = snapshot.FindItem(new ItemId(id));
                if (item == null || !item.IsImportable)
                {
                    stillPending.Add(id);
                }
                else
                {
                    foreach (var file in item.UnityPackages)
                    {
                        if (!ready.Contains(file.r_FullPath))
                        {
                            ready.Add(file.r_FullPath);
                        }
                    }
                }
            }
            SavePending(stillPending);
            SaveReady(ready);
        }

        public static void SetExcluded(string path, bool isExcluded)
        {
            var set = LoadExcluded();
            if (isExcluded)
            {
                set.Add(path);
            }
            else
            {
                set.Remove(path);
            }
            SaveExcluded(set);
        }
    }
}
