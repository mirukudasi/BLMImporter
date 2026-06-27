using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    /// <summary>
    /// 選択アイテムのインポートダイアログ。元のダウンロード待ちダイアログをベースに、
    /// 未ダウンロードの完了待ち（折りたたみ）と、ダウンロード済みの一括インポート・進捗表示を1つにまとめる。
    /// 5秒ごとにライブラリを再読み込みし、ダウンロード完了したアイテムを「ダウンロード済」へ移す。
    /// インポート対象はアイテムごとにまとめ、unitypackage単位で状態表示・除外/再追加できる。
    /// </summary>
    internal sealed class ImportProgressWindow : EditorWindow
    {
        private const double c_ReloadInterval = 5.0;
        private const float c_WaitingRowHeight = 56f;
        private const float c_WaitingThumb = 46f;
        private const float c_GroupHeight = 44f;
        private const float c_GroupThumb = 36f;
        private const float c_PackageRowHeight = 22f;

        // リロードを跨いで保持する対象。未DLアイテムID／DL済み未取り込みパス／取り込み前に除外したパス／対話インポート設定。
        private const string c_KeyPending = "BLMImporter.ImportDialog.Pending";
        private const string c_KeyReady = "BLMImporter.ImportDialog.Ready";
        private const string c_KeyExcluded = "BLMImporter.ImportDialog.Excluded";
        private const string c_KeyInteractive = "BLMImporter.ImportDialog.Interactive";

        private LibraryRuntimeSnapshot m_Snapshot = null;
        private string m_LoadError = "";
        private ThumbnailCache m_Thumbnails = null;
        private BLMWindowStyles m_Styles = null;
        private double m_NextReloadTime = 0.0;
        private double m_NextRepaintTime = 0.0;
        private bool m_PendingExpanded = true;
        private Vector2 m_WaitingScroll = Vector2.zero;
        private Vector2 m_ImportScroll = Vector2.zero;
        // パッケージのフルパス -> 所属アイテム。スナップショットから作る。
        private Dictionary<string, ItemRuntime> m_ItemByPath = null;

        // 未DLアイテムIDとDL済みパスを設定して開く。インポートは「インポート開始」押下で行う。
        public static void Open(IEnumerable<long> pendingItemIds, IEnumerable<string> readyPaths, bool interactive)
        {
            // 新規ダイアログでは前回の進捗（インポート済み表示）を持ち越さない
            SequentialPackageImporter.ClearProgressIfIdle();
            SessionState.SetString(c_KeyPending, string.Join(",", pendingItemIds));
            SessionState.SetString(c_KeyReady, string.Join("\n", readyPaths));
            SessionState.EraseString(c_KeyExcluded);
            SessionState.SetBool(c_KeyInteractive, interactive);
            ShowWindow();
        }

        // ダウンロード待ち・DL済み未取り込み・除外の対象をクリアする（詳細/単体インポート向け）。
        public static void ClearTargets()
        {
            SessionState.EraseString(c_KeyPending);
            SessionState.EraseString(c_KeyReady);
            SessionState.EraseString(c_KeyExcluded);
        }

        // 取り込み中・待ち対象がある場合のみ開く（リロード後の開き直しなど）。
        public static void OpenIfActive()
        {
            if (SequentialPackageImporter.IsRunning || HasPending() || HasReady())
            {
                ShowWindow();
            }
        }

        private static void ShowWindow()
        {
            var window = GetWindow<ImportProgressWindow>("BLMImporter インポート");
            window.minSize = new Vector2(560, 480);
            window.m_ItemByPath = null;
            window.ReloadSnapshot();
        }

        private void OnEnable()
        {
            m_Thumbnails = new ThumbnailCache();
            m_Thumbnails.Repaint += Repaint;
            EditorApplication.update += OnEditorUpdate;
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            if (m_Thumbnails != null)
            {
                m_Thumbnails.Repaint -= Repaint;
                m_Thumbnails.Dispose();
            }
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (m_Thumbnails == null)
            {
                return;
            }
            m_Thumbnails.Update();
            var now = EditorApplication.timeSinceStartup;
            // 拡張からのダウンロード完了通知があれば、5秒ポーリングを待たず即時に再読込する
            if (BLMDownloadServer.ConsumeCompletionSignal() && HasPending())
            {
                ReloadSnapshot();
                Repaint();
            }
            // ダウンロード中は5秒ポーリングを止め、完了通知での即時再読込に任せる（スナップ未取得時のみ取得）
            var pollForDownloads = HasPending() && !BLMDownloadServer.IsDownloading;
            if ((m_Snapshot == null || pollForDownloads) && now >= m_NextReloadTime)
            {
                ReloadSnapshot();
                Repaint();
            }
            // 取り込み中・ダウンロード中はロック表示や完了反映のため定期再描画する
            if ((SequentialPackageImporter.IsRunning || BLMDownloadServer.IsDownloading) && now >= m_NextRepaintTime)
            {
                m_NextRepaintTime = now + 0.5;
                Repaint();
            }
        }

        private void ReloadSnapshot()
        {
            try
            {
                // 共有キャッシュを破棄して最新の実データを読み直し、DL完了を検知する
                LibraryRuntimeSnapshot.ClearCache();
                m_Snapshot = LibraryRuntimeSnapshot.Current;
                m_LoadError = "";
            }
            catch (Exception exception)
            {
                m_LoadError = "ライブラリの再読み込みに失敗しました: " + exception.Message;
                Debug.LogException(exception);
            }
            m_ItemByPath = null;
            MoveDownloadedToReady();
            m_NextReloadTime = EditorApplication.timeSinceStartup + c_ReloadInterval;
        }

        // インポート可能（unitypackageが現れた）になった未DLアイテムの unitypackage を「DL済み未取り込み」へ移す。
        // まだ未DL・ダウンロード済みでもunitypackageが無いものは待ちに残す。
        private void MoveDownloadedToReady()
        {
            if (m_Snapshot == null)
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
                var item = m_Snapshot.FindItem(new ItemId(id));
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

        private void OnGUI()
        {
            if (m_Styles == null)
            {
                m_Styles = new BLMWindowStyles();
            }
            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }
            if (!string.IsNullOrEmpty(m_LoadError))
            {
                EditorGUILayout.HelpBox(m_LoadError, MessageType.Error);
            }

            var progress = SequentialPackageImporter.GetProgress();
            var pending = LoadPending();
            var ready = LoadReady();
            var excluded = LoadExcluded();

            DrawHeaderBar(progress, ready, excluded, pending);
            DrawPendingSection(pending);
            DrawImportSection(progress, ready, excluded);
        }

        private void DrawHeaderBar(ImportProgress progress, List<string> ready, HashSet<string> excluded, List<long> pending)
        {
            var importCount = ready.Count(path => !excluded.Contains(path));
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(HeaderTitle(progress, ready, pending), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                // ダウンロード中だけ表示。押すとロック解除（他DLと5秒ポーリングが再開）
                if (BLMDownloadServer.IsDownloading)
                {
                    if (GUILayout.Button("ダウンロード中止", GUILayout.Width(120), GUILayout.Height(24)))
                    {
                        BLMDownloadServer.MarkDownloadFinished();
                    }
                }

                using (new EditorGUI.DisabledScope(importCount == 0))
                {
                    if (GUILayout.Button("インポート開始（" + importCount + "）", GUILayout.Width(150), GUILayout.Height(24)))
                    {
                        StartImport(ready, excluded);
                    }
                }

                var active = progress.r_IsRunning || pending.Count > 0 || ready.Count > 0;
                if (active)
                {
                    if (GUILayout.Button("キャンセル", GUILayout.Width(90), GUILayout.Height(24)))
                    {
                        CancelAll(progress);
                    }
                }
                else
                {
                    if (GUILayout.Button("閉じる", GUILayout.Width(90), GUILayout.Height(24)))
                    {
                        Close();
                    }
                }
            }

            if (progress.r_Total > 0)
            {
                var ratio = (float)progress.r_DoneCount / progress.r_Total;
                var barRect = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(barRect, ratio, progress.r_DoneCount + " / " + progress.r_Total);
            }
            EditorGUILayout.Space(2);
        }

        private static string HeaderTitle(ImportProgress progress, List<string> ready, List<long> pending)
        {
            if (progress.r_IsRunning)
            {
                return "インポート中...";
            }
            if (pending.Count > 0)
            {
                return "ダウンロード待ち...";
            }
            if (progress.r_Total > 0 && ready.Count == 0)
            {
                return progress.r_DoneCount < progress.r_Total ? "インポート終了（未取り込みあり）" : "インポート完了";
            }
            return "インポート";
        }

        private void CancelAll(ImportProgress progress)
        {
            var proceed = EditorUtility.DisplayDialog(
                "BLMImporter",
                "インポートとダウンロード待ちを中止しますか？",
                "中止する", "戻る");
            if (!proceed)
            {
                return;
            }
            if (progress.r_IsRunning)
            {
                SequentialPackageImporter.Cancel();
            }
            BLMDownloadServer.MarkDownloadFinished();
            ClearTargets();
            Close();
        }

        // DL済み未取り込みのうち除外していない分をまとめて取り込みへ送り込み、対象から外す。
        private void StartImport(List<string> ready, HashSet<string> excluded)
        {
            var toImport = ready.Where(path => !excluded.Contains(path)).ToList();
            if (toImport.Count == 0)
            {
                return;
            }
            var requests = toImport.Select(path => new PackageImportRequest { m_PackagePath = path });
            var interactive = SessionState.GetBool(c_KeyInteractive, true);
            SequentialPackageImporter.Enqueue(requests, new PackageImportOptions { m_Interactive = interactive });
            SessionState.EraseString(c_KeyReady);
            SessionState.EraseString(c_KeyExcluded);
        }

        // ---- 未ダウンロード（ダウンロード待ち）。見出しは常に表示し、空のときは一覧を出さない。 ----

        private void DrawPendingSection(List<long> pending)
        {
            m_PendingExpanded = EditorGUILayout.Foldout(m_PendingExpanded, "未ダウンロード（" + pending.Count + "）", true);
            if (!m_PendingExpanded || pending.Count == 0)
            {
                return;
            }
            var height = Mathf.Min(pending.Count * c_WaitingRowHeight + 8f, 180f);
            m_WaitingScroll = EditorGUILayout.BeginScrollView(m_WaitingScroll, EditorStyles.helpBox, GUILayout.Height(height));
            foreach (var id in pending)
            {
                var item = m_Snapshot != null ? m_Snapshot.FindItem(new ItemId(id)) : null;
                DrawWaitingRow(item, id);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(2);
        }

        private void DrawWaitingRow(ItemRuntime item, long id)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, c_WaitingRowHeight, GUILayout.ExpandWidth(true));
            var thumbRect = new Rect(rowRect.x + 6f, rowRect.y + (c_WaitingRowHeight - c_WaitingThumb) * 0.5f, c_WaitingThumb, c_WaitingThumb);
            DrawThumbnail(thumbRect, item != null ? m_Thumbnails.Get(item.r_Master.r_ThumbnailUrl) : null);

            const float c_RightWidth = 150f;
            var rightX = rowRect.xMax - 6f - c_RightWidth;
            if (item != null && item.r_Master.r_OrderIds.Count > 0)
            {
                var orderId = item.r_Master.r_OrderIds[0];
                var buttonRect = new Rect(rightX, rowRect.y + (c_WaitingRowHeight - 22f) * 0.5f, c_RightWidth, 22f);
                var previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                // ダウンロード中は他のダウンロードをロックする
                using (new EditorGUI.DisabledScope(BLMDownloadServer.IsDownloading))
                {
                    // 拡張の自動ダウンロード用URL（オーダーページ＋targets＋ライブラリパス）を開く
                    if (GUI.Button(buttonRect, "ダウンロード"))
                    {
                        var server = BLMDownloadServer.BeginDownload();
                        Application.OpenURL(item.DownloadUrl(orderId, server.port, server.token));
                    }
                }
                GUI.backgroundColor = previousBackground;
            }
            else
            {
                var labelRect = new Rect(rightX, rowRect.y + (c_WaitingRowHeight - 16f) * 0.5f, c_RightWidth, 16f);
                GUI.Label(labelRect, "注文情報なし", m_Styles.RowSub);
            }

            var textX = thumbRect.xMax + 8f;
            var name = item != null ? item.r_Master.r_Name : ("アイテム " + id);
            var sub = item != null ? item.r_Master.r_ShopName + "  •  " + item.r_Master.r_SubCategoryName : null;
            DrawRowText(new Rect(textX, rowRect.y, Mathf.Max(0f, rightX - 8f - textX), c_WaitingRowHeight), name, sub);
        }

        // 太字タイトル＋任意のサブ行を、与えた矩形の縦中央に積む
        private void DrawRowText(Rect area, string title, string sub)
        {
            const float c_TitleHeight = 17f;
            const float c_SubHeight = 14f;
            var hasSub = !string.IsNullOrEmpty(sub);
            var totalHeight = hasSub ? c_TitleHeight + c_SubHeight : c_TitleHeight;
            var y = area.y + (area.height - totalHeight) * 0.5f;
            GUI.Label(new Rect(area.x, y, area.width, c_TitleHeight), title, EditorStyles.boldLabel);
            if (hasSub)
            {
                GUI.Label(new Rect(area.x, y + c_TitleHeight, area.width, c_SubHeight), sub, m_Styles.RowSub);
            }
        }

        // ---- インポート対象。アイテムごとにまとめ、unitypackage単位で状態・除外/再追加を表示。 ----

        private void DrawImportSection(ImportProgress progress, List<string> ready, HashSet<string> excluded)
        {
            var statusByPath = new Dictionary<string, ImportEntryStatus>(StringComparer.Ordinal);
            foreach (var entry in progress.r_Entries)
            {
                statusByPath[entry.r_Path] = entry.r_Status;
            }
            var targets = new HashSet<string>(statusByPath.Keys, StringComparer.Ordinal);
            foreach (var path in ready)
            {
                targets.Add(path);
            }

            EditorGUILayout.Space(2);
            GUILayout.Label("インポート対象（unitypackage " + targets.Count + " 個）", EditorStyles.boldLabel);
            m_ImportScroll = EditorGUILayout.BeginScrollView(m_ImportScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            if (targets.Count == 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("（なし）", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var group in GroupByItem(targets))
                {
                    DrawGroup(group.Key, group.Value, statusByPath, excluded, progress.r_ActivePath);
                    DrawItemSeparator();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // アイテムグループの境界を示す横線
        private void DrawItemSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, m_Styles.Separator);
        }

        private void DrawGroup(ItemRuntime item, List<string> paths, Dictionary<string, ImportEntryStatus> statusByPath, HashSet<string> excluded, string activePath)
        {
            var done = paths.Count(path => statusByPath.TryGetValue(path, out var status) && status == ImportEntryStatus.Done);
            DrawGroupHeader(item, done, paths.Count);
            foreach (var path in paths)
            {
                DrawPackageRow(path, statusByPath, excluded, activePath);
            }
        }

        private void DrawGroupHeader(ItemRuntime item, int done, int total)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, c_GroupHeight, GUILayout.ExpandWidth(true));
            var thumbRect = new Rect(rowRect.x + 4f, rowRect.y + (c_GroupHeight - c_GroupThumb) * 0.5f, c_GroupThumb, c_GroupThumb);
            DrawThumbnail(thumbRect, item != null ? m_Thumbnails.Get(item.r_Master.r_ThumbnailUrl) : null);

            const float c_CountWidth = 84f;
            var countX = rowRect.xMax - 6f - c_CountWidth;
            GUI.Label(new Rect(countX, rowRect.y + (c_GroupHeight - 16f) * 0.5f, c_CountWidth, 16f), "済 " + done + " / " + total, m_Styles.RowSub);

            var textX = thumbRect.xMax + 6f;
            var name = item != null ? item.r_Master.r_Name : "（不明なパッケージ）";
            var sub = item != null ? item.r_Master.r_ShopName + "  •  " + item.r_Master.r_SubCategoryName : null;
            DrawRowText(new Rect(textX, rowRect.y, Mathf.Max(0f, countX - 6f - textX), c_GroupHeight), name, sub);
        }

        private void DrawPackageRow(string path, Dictionary<string, ImportEntryStatus> statusByPath, HashSet<string> excluded, string activePath)
        {
            var inImporter = statusByPath.TryGetValue(path, out var importerStatus);
            var state = ResolveRowState(path, inImporter, importerStatus, excluded, activePath);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(c_PackageRowHeight)))
            {
                GUILayout.Space(16);
                using (new GuiColorScope(state.Color))
                {
                    GUILayout.Label("●", GUILayout.Width(16));
                }
                GUILayout.Label(Path.GetFileName(path), EditorStyles.label);
                GUILayout.FlexibleSpace();
                using (new GuiColorScope(state.Color))
                {
                    GUILayout.Label(state.Label, GUILayout.Width(84));
                }
                DrawRowToggle(path, inImporter, importerStatus, excluded);
            }
        }

        // 行の状態（ラベルと色）を決める。取り込みキューにあるものは取り込み側の状態、無いものはDL済み未取り込み扱い。
        private static (string Label, Color Color) ResolveRowState(string path, bool inImporter, ImportEntryStatus importerStatus, HashSet<string> excluded, string activePath)
        {
            var green = new Color(0.45f, 0.80f, 0.50f);
            var amber = new Color(0.95f, 0.75f, 0.2f);
            var gray = new Color(0.66f, 0.66f, 0.66f);
            var red = new Color(0.93f, 0.32f, 0.32f);
            var blue = new Color(0.5f, 0.7f, 0.95f);
            if (inImporter)
            {
                if (importerStatus == ImportEntryStatus.Done)
                {
                    return ("インポート済", green);
                }
                if (importerStatus == ImportEntryStatus.Excluded)
                {
                    return ("除外", red);
                }
                var importing = importerStatus == ImportEntryStatus.Importing || string.Equals(path, activePath, StringComparison.Ordinal);
                if (importing)
                {
                    return ("インポート中", amber);
                }
                return ("待機中", gray);
            }
            if (excluded.Contains(path))
            {
                return ("除外", red);
            }
            return ("未取り込み", blue);
        }

        private void DrawRowToggle(string path, bool inImporter, ImportEntryStatus importerStatus, HashSet<string> excluded)
        {
            if (inImporter)
            {
                if (importerStatus == ImportEntryStatus.Pending)
                {
                    using (new GuiColorScope(new Color(0.93f, 0.32f, 0.32f)))
                    {
                        if (GUILayout.Button("除外", EditorStyles.miniButton, GUILayout.Width(56)))
                        {
                            SequentialPackageImporter.SetPackageIncluded(path, false);
                        }
                    }
                }
                else if (importerStatus == ImportEntryStatus.Excluded)
                {
                    if (GUILayout.Button("追加", EditorStyles.miniButton, GUILayout.Width(56)))
                    {
                        SequentialPackageImporter.SetPackageIncluded(path, true);
                    }
                }
                else
                {
                    GUILayout.Space(60);
                }
            }
            else if (excluded.Contains(path))
            {
                if (GUILayout.Button("追加", EditorStyles.miniButton, GUILayout.Width(56)))
                {
                    SetReadyExcluded(path, false);
                }
            }
            else
            {
                using (new GuiColorScope(new Color(0.93f, 0.32f, 0.32f)))
                {
                    if (GUILayout.Button("除外", EditorStyles.miniButton, GUILayout.Width(56)))
                    {
                        SetReadyExcluded(path, true);
                    }
                }
            }
        }

        private void SetReadyExcluded(string path, bool isExcluded)
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

        // パスをアイテム単位にまとめる。順序はパス昇順で安定させる。
        private List<KeyValuePair<ItemRuntime, List<string>>> GroupByItem(HashSet<string> paths)
        {
            var map = ItemByPath();
            var groups = new List<KeyValuePair<ItemRuntime, List<string>>>();
            var indexByItem = new Dictionary<ItemId, int>();
            var unknown = new List<string>();
            var sorted = paths.ToList();
            sorted.Sort(StringComparer.Ordinal);
            foreach (var path in sorted)
            {
                map.TryGetValue(path, out var item);
                if (item == null)
                {
                    unknown.Add(path);
                }
                else
                {
                    if (!indexByItem.TryGetValue(item.Id, out var index))
                    {
                        index = groups.Count;
                        indexByItem[item.Id] = index;
                        groups.Add(new KeyValuePair<ItemRuntime, List<string>>(item, new List<string>()));
                    }
                    groups[index].Value.Add(path);
                }
            }
            if (unknown.Count > 0)
            {
                groups.Add(new KeyValuePair<ItemRuntime, List<string>>(null, unknown));
            }
            return groups;
        }

        private Dictionary<string, ItemRuntime> ItemByPath()
        {
            if (m_ItemByPath != null)
            {
                return m_ItemByPath;
            }
            var map = new Dictionary<string, ItemRuntime>(StringComparer.Ordinal);
            if (m_Snapshot != null)
            {
                foreach (var item in m_Snapshot.r_Items.Values)
                {
                    foreach (var file in item.UnityPackages)
                    {
                        map[file.r_FullPath] = item;
                    }
                }
            }
            m_ItemByPath = map;
            return m_ItemByPath;
        }

        private void DrawThumbnail(Rect rect, Texture2D texture)
        {
            EditorGUI.DrawRect(rect, m_Styles.ThumbFrame);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(inner, m_Styles.ThumbBack);
            if (texture != null)
            {
                GUI.DrawTexture(inner, texture, ScaleMode.ScaleToFit);
            }
        }

        // ---- SessionState 保持（リロードを跨ぐ） ----

        private static bool HasPending()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(c_KeyPending, ""));
        }

        private static bool HasReady()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(c_KeyReady, ""));
        }

        private static List<long> LoadPending()
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

        private static void SavePending(List<long> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                SessionState.EraseString(c_KeyPending);
                return;
            }
            SessionState.SetString(c_KeyPending, string.Join(",", ids));
        }

        private static List<string> LoadReady()
        {
            return SplitPaths(SessionState.GetString(c_KeyReady, ""));
        }

        private static void SaveReady(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                SessionState.EraseString(c_KeyReady);
                return;
            }
            SessionState.SetString(c_KeyReady, string.Join("\n", paths));
        }

        private static HashSet<string> LoadExcluded()
        {
            return new HashSet<string>(SplitPaths(SessionState.GetString(c_KeyExcluded, "")), StringComparer.Ordinal);
        }

        private static void SaveExcluded(HashSet<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                SessionState.EraseString(c_KeyExcluded);
                return;
            }
            SessionState.SetString(c_KeyExcluded, string.Join("\n", paths));
        }

        private static List<string> SplitPaths(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return new List<string>();
            }
            return raw.Split('\n').Where(path => path.Length > 0).ToList();
        }
    }
}
