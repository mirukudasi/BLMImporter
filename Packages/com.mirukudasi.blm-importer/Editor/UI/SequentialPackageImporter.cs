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
    /// unitypackageを1件ずつ直列にインポートする。
    /// AssetDatabase.ImportPackage は非同期で、ループで連続呼び出しすると
    /// インポートダイアログが互いに打ち消し合いスキップされるため、
    /// importPackage 系コールバックで前の1件が終わってから次を流す。
    /// インポートでスクリプトが入るとドメインリロードが起き static 状態が消えるため、
    /// 進行状態は SessionState に保存し、[InitializeOnLoad] でリロード後に再開する。
    /// </summary>
    [InitializeOnLoad]
    internal static class SequentialPackageImporter
    {
        // SessionState はアセンブリリロードを跨いで残り、エディタ終了で消える（途中再開に適切）
        private const string c_KeyRunning = "BLMImporter.SerialImport.Running";
        private const string c_KeyQueue = "BLMImporter.SerialImport.Queue";
        private const string c_KeyActive = "BLMImporter.SerialImport.Active";
        private const string c_KeyInteractive = "BLMImporter.SerialImport.Interactive";
        // 先頭 '.' のパッケージ用に作った一時コピー。インポート後に削除する
        private const string c_KeyTempFile = "BLMImporter.SerialImport.TempFile";
        // 進捗表示用。全対象パス／処理済みパス／処理中の元パス
        private const string c_KeyAll = "BLMImporter.SerialImport.All";
        private const string c_KeyDone = "BLMImporter.SerialImport.Done";
        private const string c_KeyActiveOriginal = "BLMImporter.SerialImport.ActiveOriginal";
        // キュー保存時のパス区切り。Windows/Mac のパスに現れない改行を使う
        private const char c_QueueSeparator = '\n';
        // ダイアログが閉じ処理も落ち着いた後、完了コールバックが来ない場合に次へ進めるまでの猶予
        private const double c_PostDialogGraceSeconds = 5.0;

        private static bool s_Subscribed = false;
        private static bool s_WaitingForImportCallback = false;
        // idle(ダイアログ閉・処理落ち着き)が続いたとき、完了扱いにして進める期限。0=未設定
        private static double s_GraceDeadline = 0.0;

        // 起動時・ドメインリロード後に呼ばれる。インポート進行中だったら購読し直して再開する。
        static SequentialPackageImporter()
        {
            if (SessionState.GetBool(c_KeyRunning, false)) {
                EnsureSubscribed();
                EditorApplication.delayCall += ResumeAfterReload;
                // リロードで閉じても進捗ウィンドウを開き直す
                EditorApplication.delayCall += ImportProgressWindow.OpenIfActive;
            }
        }

        public static bool IsRunning => SessionState.GetBool(c_KeyRunning, false);

        /// <summary>
        /// 実行中でなければ、前回の進捗（全対象・処理済み・処理中など）を消す。
        /// 新しいインポートダイアログを開く前に呼び、前回の「インポート済」表示が残らないようにする。
        /// </summary>
        public static void ClearProgressIfIdle()
        {
            if (IsRunning) {
                return;
            }
            SessionState.EraseString(c_KeyQueue);
            SessionState.EraseString(c_KeyAll);
            SessionState.EraseString(c_KeyDone);
            SessionState.EraseString(c_KeyActive);
            SessionState.EraseString(c_KeyActiveOriginal);
            SessionState.EraseString(c_KeyTempFile);
        }

        /// <summary>対象をキューに積み、直列インポートを開始する。</summary>
        public static void Run(IEnumerable<PackageImportRequest> requests, PackageImportOptions options)
        {
            if (IsRunning) {
                EditorUtility.DisplayDialog("BLMImporter", "インポート処理中のため、新しいインポートは開始できません。", "OK");
                return;
            }

            var paths = requests
                .Select(request => request.m_PackagePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();
            if (paths.Count == 0) {
                return;
            }

            // 念のため前回の進行中フラグを落としてから開始する（多重起動ガードの誤作動防止）
            s_WaitingForImportCallback = false;
            s_GraceDeadline = 0.0;
            SaveQueue(paths);
            SessionState.SetString(c_KeyAll, string.Join(c_QueueSeparator.ToString(), paths));
            SessionState.EraseString(c_KeyDone);
            SessionState.EraseString(c_KeyActive);
            SessionState.EraseString(c_KeyActiveOriginal);
            SessionState.EraseString(c_KeyTempFile);
            SessionState.SetBool(c_KeyInteractive, options.m_Interactive);
            SessionState.SetBool(c_KeyRunning, true);
            EnsureSubscribed();
            ScheduleImportNext();
        }

        /// <summary>
        /// 実行中なら対象を末尾のキューへ追加し、止まっていれば新規に開始する。
        /// ダウンロード完了分をライブで取り込みキューへ送り込むのに使う。
        /// </summary>
        public static void Enqueue(IEnumerable<PackageImportRequest> requests, PackageImportOptions options)
        {
            var paths = requests
                .Select(request => request.m_PackagePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();
            if (paths.Count == 0) {
                return;
            }

            // 既存の all/done を保ったまま追記する（完了後に遅れて届いた分も進捗一覧に残す）
            var all = LoadList(c_KeyAll);
            var queue = LoadQueue();
            foreach (var path in paths) {
                all.Add(path);
                queue.Add(path);
            }
            SessionState.SetString(c_KeyAll, string.Join(c_QueueSeparator.ToString(), all));
            SaveQueue(queue);

            if (!IsRunning) {
                // 直前の進捗を保ったまま再開する（Run と違い done をリセットしない）
                s_WaitingForImportCallback = false;
                s_GraceDeadline = 0.0;
                ClearActive();
                SessionState.EraseString(c_KeyTempFile);
                SessionState.SetBool(c_KeyInteractive, options.m_Interactive);
                SessionState.SetBool(c_KeyRunning, true);
                EnsureSubscribed();
                ScheduleImportNext();
            }
            else if (!s_WaitingForImportCallback) {
                // 実行中だがアイドル（処理中の1件が無い）なら次を起動する
                ScheduleImportNext();
            }
            RepaintImporterWindows();
        }

        // 次の1件のインポートを次フレームに回す。
        // importPackageCompleted コールバックの中から AssetDatabase.ImportPackage を再入呼び出しすると
        // 2件目以降が始まらないため、必ず delayCall を挟んでコールスタックを分離する。
        private static void ScheduleImportNext()
        {
            EditorApplication.delayCall += ImportNext;
        }

        // 次の1件をインポートする。失敗したものは飛ばして続け、空になったら後始末する。
        private static void ImportNext()
        {
            // 後始末後に残った delayCall が発火しても何もしない
            if (!IsRunning) {
                return;
            }
            // 既にインポート中なら二重起動しない（完了コールバックとリロード復帰が競合しても安全）
            if (s_WaitingForImportCallback) {
                return;
            }
            var queue = LoadQueue();
            while (queue.Count > 0) {
                var path = queue[0];
                queue.RemoveAt(0);
                // import 前に「残りキュー」を保存しておく。
                // import 中にドメインリロードが起きても、復帰時に残りから続けられる。
                SaveQueue(queue);
                try {
                    // 先頭 '.' のパッケージはドット無しの一時コピーをインポートする（Unityの隠し扱い・名前照合の回避）
                    var importPath = PackageImporter.PrepareImportablePath(path);
                    SetActiveImport(importPath, path);
                    s_WaitingForImportCallback = true;
                    s_GraceDeadline = 0.0;
                    PackageImporter.Import(importPath, CurrentOptions());
                    return;
                } catch (Exception exception) {
                    // 1件の失敗で全体を止めない。ダイアログで中断せず、ログだけ出して次の1件へ進む。
                    s_WaitingForImportCallback = false;
                    CleanupTempFile();
                    MarkProcessed(path);
                    ClearActive();
                    Debug.LogError("unitypackageのインポートに失敗したためスキップします: " + path + "\n" + exception);
                }
            }
            Finish();
        }

        private static void OnImportFinished(string packageName)
        {
            OnImportFinished(packageName, null);
        }

        private static void OnImportFinished(string packageName, string errorMessage = null)
        {
            if (!IsRunning) {
                return;
            }
            if (!IsActivePackageCallback(packageName)) {
                return;
            }
            if (errorMessage != null) {
                Debug.LogError("unitypackageのインポートに失敗しました: " + packageName + " / " + errorMessage);
            }
            s_WaitingForImportCallback = false;
            CleanupTempFile();
            MarkProcessed(ActiveOriginalPath());
            ClearActive();
            ScheduleImportNext();
        }

        // ドメインリロード後の再開。リロード直前に import 中だった1件はリロード完了をもって
        // 完了扱いにし、残りのキューを必ず続ける（ここで止めると running が固着するため、進めることを優先）。
        // 二重起動・取りこぼしは ImportNext の実行中ガードと SessionState のキューで防ぐ。
        private static void ResumeAfterReload()
        {
            if (!IsRunning) {
                return;
            }
            // エディタのコンパイル/更新が落ち着くまで待つ
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                EditorApplication.delayCall += ResumeAfterReload;
                return;
            }
            s_WaitingForImportCallback = false;
            CleanupTempFile();
            MarkProcessed(ActiveOriginalPath());
            ClearActive();
            ScheduleImportNext();
        }

        private static void WatchImportPackageCallback()
        {
            if (!IsRunning) {
                return;
            }
            if (!s_WaitingForImportCallback) {
                return;
            }

            // インポートダイアログ表示中はユーザー操作待ち。タイムアウトせず待つ。
            if (SessionState.GetBool(c_KeyInteractive, true) && IsImportPackageWindowOpen()) {
                s_GraceDeadline = 0.0;
                return;
            }
            // アセット取り込み/コンパイル中も完了まで待つ。タイムアウトしない。
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                s_GraceDeadline = 0.0;
                return;
            }
            // ダイアログが閉じ処理も落ち着いた状態。完了コールバックが来ないケース(既にインポート済み等)に備え、
            // 短い猶予(idleが継続したら)後に当該1件を完了扱いにして次へ進める。
            var now = EditorApplication.timeSinceStartup;
            if (s_GraceDeadline <= 0.0) {
                s_GraceDeadline = now + c_PostDialogGraceSeconds;
                return;
            }
            if (now < s_GraceDeadline) {
                return;
            }

            Debug.LogWarning("unitypackageのインポート完了コールバックを受信できなかったため、この1件を完了扱いにして次へ進みます: " + ActivePath());
            s_GraceDeadline = 0.0;
            s_WaitingForImportCallback = false;
            CleanupTempFile();
            MarkProcessed(ActiveOriginalPath());
            ClearActive();
            ScheduleImportNext();
        }

        private static bool IsImportPackageWindowOpen()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) {
                var typeName = window.GetType().Name;
                var title = window.titleContent != null ? window.titleContent.text : "";
                if (typeName.IndexOf("PackageImport", StringComparison.OrdinalIgnoreCase) >= 0
                    || title.IndexOf("Import Unity Package", StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }
            return false;
        }

        private static bool IsActivePackageCallback(string packageName)
        {
            var activePath = ActivePath();
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(activePath)) {
                return false;
            }

            var normalizedPackageName = packageName.Replace('\\', '/');
            var normalizedPackagePath = activePath.Replace('\\', '/');
            if (string.Equals(normalizedPackageName, normalizedPackagePath, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            var callbackFileName = Path.GetFileName(normalizedPackageName);
            var targetFileName = Path.GetFileName(normalizedPackagePath);
            if (string.Equals(callbackFileName, targetFileName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            var callbackNameWithoutExtension = Path.GetFileNameWithoutExtension(callbackFileName);
            var targetNameWithoutExtension = Path.GetFileNameWithoutExtension(targetFileName);
            return string.Equals(callbackFileName, targetNameWithoutExtension, StringComparison.OrdinalIgnoreCase)
                || string.Equals(callbackNameWithoutExtension, targetNameWithoutExtension, StringComparison.OrdinalIgnoreCase);
        }

        private static void Finish()
        {
            s_WaitingForImportCallback = false;
            s_GraceDeadline = 0.0;
            CleanupTempFile();
            DeleteLeftoverImportCache();
            SessionState.EraseString(c_KeyQueue);
            ClearActive();
            SessionState.EraseString(c_KeyTempFile);
            SessionState.SetBool(c_KeyRunning, false);
            // c_KeyAll / c_KeyDone は完了後の進捗表示に使うため残す（次の Run で作り直す）
            Unsubscribe();
            RepaintImporterWindows();
        }

        // ---- 購読・状態の保存/復元 ----

        private static void EnsureSubscribed()
        {
            if (s_Subscribed) {
                return;
            }
            AssetDatabase.importPackageCompleted += OnImportFinished;
            AssetDatabase.importPackageCancelled += OnImportFinished;
            AssetDatabase.importPackageFailed += OnImportFinished;
            EditorApplication.update += WatchImportPackageCallback;
            s_Subscribed = true;
        }

        private static void Unsubscribe()
        {
            AssetDatabase.importPackageCompleted -= OnImportFinished;
            AssetDatabase.importPackageCancelled -= OnImportFinished;
            AssetDatabase.importPackageFailed -= OnImportFinished;
            EditorApplication.update -= WatchImportPackageCallback;
            s_Subscribed = false;
        }

        private static PackageImportOptions CurrentOptions()
        {
            return new PackageImportOptions { m_Interactive = SessionState.GetBool(c_KeyInteractive, true) };
        }

        private static string ActivePath()
        {
            return SessionState.GetString(c_KeyActive, "");
        }

        // 処理中パス（コールバック照合用）を保存する。一時コピーを使う場合はその削除対象も覚える。
        // 進捗表示用に、一時コピー前の元パスも保持する。
        private static void SetActiveImport(string importPath, string originalPath)
        {
            SessionState.SetString(c_KeyActive, importPath);
            SessionState.SetString(c_KeyActiveOriginal, originalPath);
            var tempFile = "";
            if (importPath != originalPath) {
                tempFile = importPath;
            }
            SessionState.SetString(c_KeyTempFile, tempFile);
        }

        private static string ActiveOriginalPath()
        {
            return SessionState.GetString(c_KeyActiveOriginal, "");
        }

        // 処理中パス情報をクリアする（コールバック照合用・進捗用の両方）。
        private static void ClearActive()
        {
            SessionState.SetString(c_KeyActive, "");
            SessionState.SetString(c_KeyActiveOriginal, "");
        }

        // 処理済み（完了/失敗/スキップ）の元パスを記録する。進捗表示の「インポート済み」に使う。
        private static void MarkProcessed(string originalPath)
        {
            if (string.IsNullOrEmpty(originalPath)) {
                return;
            }
            var done = LoadList(c_KeyDone);
            if (!done.Contains(originalPath)) {
                done.Add(originalPath);
                SessionState.SetString(c_KeyDone, string.Join(c_QueueSeparator.ToString(), done));
            }
        }

        // 先頭 '.' パッケージ用に作った固定キャッシュがあれば削除する。削除に成功したときだけキーを消し、
        // 失敗時はキーを残して後続（次の CleanupTempFile / Finish の削除）で再試行できるようにする。
        // 残っても次回コピーで上書きされるため、最大1件しか残らない。
        private static void CleanupTempFile()
        {
            var tempFile = SessionState.GetString(c_KeyTempFile, "");
            if (string.IsNullOrEmpty(tempFile)) {
                return;
            }
            try {
                if (File.Exists(tempFile)) {
                    File.Delete(tempFile);
                }
                SessionState.EraseString(c_KeyTempFile);
            } catch (Exception exception) {
                Debug.LogWarning("一時インポートキャッシュの削除に失敗しました（次回のインポート時に上書きされます）: " + tempFile + "\n" + exception);
            }
        }

        // 後始末時に、追跡できていない固定キャッシュの残りも消す（クラッシュ等での取りこぼし対策）。
        private static void DeleteLeftoverImportCache()
        {
            try {
                if (File.Exists(PackageImporter.ImportCachePath)) {
                    File.Delete(PackageImporter.ImportCachePath);
                }
            } catch (Exception exception) {
                Debug.LogWarning("一時インポートキャッシュの削除に失敗しました（次回のインポート時に上書きされます）: " + PackageImporter.ImportCachePath + "\n" + exception);
            }
        }

        private static List<string> LoadQueue()
        {
            return LoadList(c_KeyQueue);
        }

        private static List<string> LoadList(string key)
        {
            var raw = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) {
                return new List<string>();
            }
            return raw.Split(c_QueueSeparator).Where(path => !string.IsNullOrEmpty(path)).ToList();
        }

        /// <summary>進捗ウィンドウ用に、対象パッケージごとの状態を返す。</summary>
        public static ImportProgress GetProgress()
        {
            var all = LoadList(c_KeyAll);
            var running = IsRunning;
            var queue = new HashSet<string>(LoadQueue());
            var done = new HashSet<string>(LoadList(c_KeyDone));
            var activeOriginal = ActiveOriginalPath();

            var entries = new List<ImportProgressEntry>(all.Count);
            var doneCount = 0;
            foreach (var path in all) {
                ImportEntryStatus status;
                if (running && activeOriginal.Length > 0 && path == activeOriginal) {
                    status = ImportEntryStatus.Importing;
                }
                else if (done.Contains(path)) {
                    status = ImportEntryStatus.Done;
                    doneCount += 1;
                }
                else if (running && queue.Contains(path)) {
                    status = ImportEntryStatus.Pending;
                }
                else {
                    // キューにも done にも無い＝ユーザーが除外した/中断で未取り込み
                    status = ImportEntryStatus.Excluded;
                }
                entries.Add(new ImportProgressEntry(path, status));
            }
            var active = running ? activeOriginal : "";
            return new ImportProgress(running, all.Count, doneCount, active, entries);
        }

        /// <summary>残りのインポートを中止する。実行中の1件は止められないが、以降は開始しない。</summary>
        public static void Cancel()
        {
            if (!IsRunning) {
                return;
            }
            SessionState.SetString(c_KeyQueue, "");
            Finish();
        }

        /// <summary>
        /// まだ取り込んでいない1件のインポート要否を切り替える（除外/再追加）。
        /// included=false でキューから外し、true で元の順序のままキューへ戻す。
        /// 処理中・完了済みは変更できない。
        /// </summary>
        public static void SetPackageIncluded(string originalPath, bool included)
        {
            if (!IsRunning || string.IsNullOrEmpty(originalPath)) {
                return;
            }
            if (ActiveOriginalPath() == originalPath) {
                return;
            }
            var all = LoadList(c_KeyAll);
            if (!all.Contains(originalPath)) {
                return;
            }
            var done = new HashSet<string>(LoadList(c_KeyDone));
            if (done.Contains(originalPath)) {
                return;
            }

            var queue = LoadQueue();
            var inQueue = queue.Contains(originalPath);
            if (included && !inQueue) {
                queue.Add(originalPath);
                // 元の対象順(all)に合わせて並べ直し、取り込み順を保つ
                queue = queue.OrderBy(path => all.IndexOf(path)).ToList();
                SaveQueue(queue);
            }
            else if (!included && inQueue) {
                queue.Remove(originalPath);
                SaveQueue(queue);
            }
            RepaintImporterWindows();
        }

        private static void SaveQueue(List<string> paths)
        {
            SessionState.SetString(c_KeyQueue, string.Join(c_QueueSeparator.ToString(), paths));
        }

        private static void RepaintImporterWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<BLMImporterWindow>()) {
                window.Repaint();
            }
            foreach (var window in Resources.FindObjectsOfTypeAll<ImportProgressWindow>()) {
                window.Repaint();
            }
        }
    }

    /// <summary>インポート対象1件の進捗状態。</summary>
    public enum ImportEntryStatus
    {
        Pending,
        Importing,
        Done,
        Excluded
    }

    /// <summary>インポート対象パッケージ1件の進捗エントリ。</summary>
    public sealed class ImportProgressEntry
    {
        public readonly string r_Path;
        public readonly ImportEntryStatus r_Status;

        public ImportProgressEntry(string path, ImportEntryStatus status)
        {
            r_Path = path ?? "";
            r_Status = status;
        }
    }

    /// <summary>直列インポートの進捗スナップショット。</summary>
    public sealed class ImportProgress
    {
        public readonly bool r_IsRunning;
        public readonly int r_Total;
        public readonly int r_DoneCount;
        // 処理中の元パス（実行中でなければ空）
        public readonly string r_ActivePath;
        public readonly IReadOnlyList<ImportProgressEntry> r_Entries;

        public ImportProgress(bool isRunning, int total, int doneCount, string activePath, IReadOnlyList<ImportProgressEntry> entries)
        {
            r_IsRunning = isRunning;
            r_Total = total;
            r_DoneCount = doneCount;
            r_ActivePath = activePath ?? "";
            r_Entries = entries;
        }
    }
}
