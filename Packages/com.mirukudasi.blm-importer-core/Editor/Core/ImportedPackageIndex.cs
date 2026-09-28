using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// unitypackage が、このプロジェクトに入っている版と比べてどういう関係にあるかの状態
    /// </summary>
    public enum PackageImportState
    {
        // 同じアイテムの unitypackage をまだ調べ終えていない
        Checking,
        // この unitypackage の系列はまだプロジェクトに取り込まれていない
        NotImported,
        // プロジェクトに入っている版より新しい版
        Newer,
        // プロジェクトに入っている版そのもの
        Current,
        // プロジェクトに入っている版より古い版
        Older
    }

    /// <summary>
    /// unitypackage が、このプロジェクトに入っている版と比べて新しいか古いかを判定する。
    ///
    /// 同じアイテム内で収録アセットのGUIDが1件でも重なる unitypackage 同士は、同じ系列の別の版とみなす。
    /// 直接重ならなくても、別の unitypackage を介してつながれば同じ系列になる。
    ///
    /// 系列ごとに「現在の版」を1つ選ぶ。GUIDから引いたプロジェクト側のファイルの大きさが
    /// 書庫に記録された大きさと一致する収録アセットの割合を一致率とし、一致率が最も高い版を現在の版とする。
    /// 一致率が同じなら作者側の更新時刻が新しい版を選ぶ。一致するものが1件も無い系列には現在の版が無い。
    ///
    /// 各 unitypackage の状態は、作者側の更新時刻を現在の版と比べて、新しい版・現在の版・古い版のいずれかに決める。
    /// 照合はGUIDで行うため、取り込み後にフォルダを移動・改名していても追従する。
    /// 中身は読まずファイルの大きさだけを比べるため軽い。
    ///
    /// unitypackage の解析は重いのでバックグラウンドスレッドで行い、
    /// 描画側は <see cref="GetStates"/> を呼ぶだけで待たされない。
    /// </summary>
    public static class ImportedPackageIndex
    {
        /// <summary>
        /// 1つの unitypackage についての解析結果と照合結果
        /// </summary>
        private sealed class PackageEntry
        {
            // ファイルの更新時刻と大きさから作る指紋。差し替えを検知して解析し直すのに使う
            public long m_Fingerprint = 0;
            // 指紋を最後に確かめたエディタ起動からの経過秒。描画のたびにファイルを調べないための目印
            public double m_FingerprintCheckedAt = 0.0;
            // 解析済みなら収録アセットの一覧。解析中は null
            public UnityPackageAsset[] m_Assets = null;
            // 収録アセットのGUIDの集まり。同じ系列かどうかを見分けるのに使う
            public HashSet<string> m_Guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 収録アセットのうち最も新しい作者側の更新時刻。この unitypackage の版の新しさにあたる
            public DateTime m_AuthorTimeUtc = DateTime.MinValue;
            // 現在のプロジェクト状態に対して照合済みか
            public bool m_Resolved = false;
            // 収録アセットのうち、プロジェクト側のファイルと大きさが一致したものの割合
            public double m_MatchRate = 0.0;
            // 収録アセットが置かれている共通の親フォルダ
            public string m_RootAssetPath = "";
        }

        /// <summary>
        /// アイテム1つ分の判定結果の控え。判定の前提が変わっていなければ描画のたびに作り直さずに済む
        /// </summary>
        private sealed class ItemStateCache
        {
            // 控えを作ったときの判定の前提の番号
            public int m_Generation = -1;
            public Dictionary<string, PackageImportState> m_States = new Dictionary<string, PackageImportState>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>解析やプロジェクト状態の変化で判定が変わったときに発火する。描画の更新に使う。</summary>
        public static event Action StateChanged;

        private static readonly Dictionary<string, PackageEntry> s_Entries = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        // 解析待ちのファイルパスと、解析が終わった収録アセット一覧の受け渡し口
        private static readonly ConcurrentQueue<string> s_ParseQueue = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<KeyValuePair<string, UnityPackageAsset[]>> s_ParseResults = new ConcurrentQueue<KeyValuePair<string, UnityPackageAsset[]>>();
        // アイテムごとの判定結果の控え。キーはアイテム内の unitypackage のパスを改行でつないだもの
        private static readonly Dictionary<string, ItemStateCache> s_ItemStateCaches = new Dictionary<string, ItemStateCache>(StringComparer.OrdinalIgnoreCase);
        // 解析スレッドが動いているか。0=停止中 1=稼働中
        private static int s_WorkerState = 0;
        // 判定の前提が変わるたびに増やす番号。控えが古くなったかを見分けるのに使う
        private static int s_Generation = 0;
        // 描画のたびにファイルを調べると重いため、同じファイルの指紋確認はこの間隔まで省く
        private const double c_FingerprintCheckIntervalSeconds = 5.0;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += TakeParseResults;
        }

        /// <summary>
        /// 同じアイテムが持つ unitypackage それぞれの状態を、パスをキーにして返す。
        /// 系列と現在の版はアイテム内の unitypackage を見比べて決まるため、アイテムの全パスをまとめて渡す。
        /// 1件でも解析が終わっていないうちは全件を <see cref="PackageImportState.Checking"/> にし、
        /// 揃った時点で <see cref="StateChanged"/> で知らせる。
        /// </summary>
        public static IReadOnlyDictionary<string, PackageImportState> GetStates(IReadOnlyList<string> itemPackagePaths)
        {
            // ファイルの差し替えの検知とプロジェクトとの照合はここで済ませる。その過程で判定の前提の番号が進むことがある
            var entries = new PackageEntry[itemPackagePaths.Count];
            for (var i = 0; i < itemPackagePaths.Count; i += 1) {
                entries[i] = GetResolvedEntry(itemPackagePaths[i]);
            }

            var cacheKey = string.Join("\n", itemPackagePaths);
            var isCacheFresh = s_ItemStateCaches.TryGetValue(cacheKey, out var cache) && cache.m_Generation == s_Generation;
            if (isCacheFresh) {
                return cache.m_States;
            }

            var states = new Dictionary<string, PackageImportState>(StringComparer.OrdinalIgnoreCase);
            var hasUnparsed = entries.Any(entry => entry == null);
            if (hasUnparsed) {
                foreach (var packagePath in itemPackagePaths) {
                    states[packagePath] = PackageImportState.Checking;
                }
            }
            else {
                foreach (var series in CollectSeries(entries)) {
                    var currentVersion = FindCurrentVersion(entries, series);
                    foreach (var index in series) {
                        states[itemPackagePaths[index]] = DecideState(entries[index], currentVersion);
                    }
                }
            }
            s_ItemStateCaches[cacheKey] = new ItemStateCache {
                m_Generation = s_Generation,
                m_States = states
            };
            return states;
        }

        /// <summary>
        /// 取り込み先として「開く」ときに使うフォルダを返す。
        /// 判定がまだの場合は空文字を返す。存在する収録アセットの共通の親フォルダを返す。
        /// </summary>
        public static string GetRootAssetPath(string packagePath)
        {
            var entry = GetResolvedEntry(packagePath);
            if (entry == null) {
                return "";
            }
            return entry.m_RootAssetPath;
        }

        /// <summary>
        /// プロジェクトのアセットが変わったときに、照合結果だけを捨てる。
        /// unitypackage の解析結果は残すので、次回は照合し直すだけで済む。
        /// </summary>
        public static void InvalidateResolved()
        {
            foreach (var entry in s_Entries.Values) {
                entry.m_Resolved = false;
            }
            NotifyStateChanged();
        }

        /// <summary>
        /// 収録アセットのGUIDが重なる unitypackage 同士を、同じ系列としてまとめる。
        /// 別の unitypackage を介してつながるものも同じ系列に入れる。
        /// 各系列は <paramref name="entries"/> の中での位置の一覧で、小さい順に並ぶ。
        /// </summary>
        private static List<List<int>> CollectSeries(PackageEntry[] entries)
        {
            var seriesList = new List<List<int>>();
            var visited = new bool[entries.Length];
            for (var start = 0; start < entries.Length; start += 1) {
                if (!visited[start]) {
                    var members = new List<int>();
                    var queue = new Queue<int>();
                    visited[start] = true;
                    queue.Enqueue(start);
                    while (queue.Count > 0) {
                        var current = queue.Dequeue();
                        members.Add(current);
                        for (var other = 0; other < entries.Length; other += 1) {
                            var isNewNeighbor = !visited[other] && entries[current].m_Guids.Overlaps(entries[other].m_Guids);
                            if (isNewNeighbor) {
                                visited[other] = true;
                                queue.Enqueue(other);
                            }
                        }
                    }
                    members.Sort();
                    seriesList.Add(members);
                }
            }
            return seriesList;
        }

        /// <summary>
        /// 系列の中から、プロジェクトに入っていると推定される版を選ぶ。
        /// 大きさの一致率が最も高い版を選び、同率なら作者側の更新時刻が新しい版、それも同じなら先に並ぶ版を選ぶ。
        /// 一致するものが1件も無ければ null を返す。
        /// </summary>
        private static PackageEntry FindCurrentVersion(PackageEntry[] entries, List<int> series)
        {
            PackageEntry currentVersion = null;
            foreach (var index in series) {
                var candidate = entries[index];
                var isCandidate = candidate.m_MatchRate > 0.0;
                var hasHigherRate = currentVersion == null || candidate.m_MatchRate > currentVersion.m_MatchRate;
                var isSameRate = currentVersion != null && candidate.m_MatchRate == currentVersion.m_MatchRate;
                var isNewerAtSameRate = isSameRate && candidate.m_AuthorTimeUtc > currentVersion.m_AuthorTimeUtc;
                var isBetter = hasHigherRate || isNewerAtSameRate;
                if (isCandidate && isBetter) {
                    currentVersion = candidate;
                }
            }
            return currentVersion;
        }

        /// <summary>
        /// 作者側の更新時刻を現在の版と比べて、この unitypackage の状態を決める。
        /// 系列に現在の版が無ければ、まだ取り込まれていないとみなす。
        /// </summary>
        private static PackageImportState DecideState(PackageEntry entry, PackageEntry currentVersion)
        {
            if (currentVersion != null) {
                if (entry.m_AuthorTimeUtc > currentVersion.m_AuthorTimeUtc) {
                    return PackageImportState.Newer;
                }
                if (entry.m_AuthorTimeUtc < currentVersion.m_AuthorTimeUtc) {
                    return PackageImportState.Older;
                }
                return PackageImportState.Current;
            }
            return PackageImportState.NotImported;
        }

        /// <summary>
        /// 解析と照合が済んだ項目を返す。まだ解析が終わっていなければ null を返す。
        /// </summary>
        private static PackageEntry GetResolvedEntry(string packagePath)
        {
            var entry = GetEntry(packagePath);
            if (entry.m_Assets == null) {
                return null;
            }
            if (!entry.m_Resolved) {
                Resolve(entry);
            }
            return entry;
        }

        /// <summary>
        /// 記録済みの項目を返す。無ければ作り、解析を予約する。
        /// ファイルが差し替わっていた場合も解析し直す。
        /// </summary>
        private static PackageEntry GetEntry(string packagePath)
        {
            var now = EditorApplication.timeSinceStartup;
            if (s_Entries.TryGetValue(packagePath, out var found)) {
                var isFresh = now - found.m_FingerprintCheckedAt < c_FingerprintCheckIntervalSeconds;
                if (isFresh) {
                    return found;
                }
                found.m_FingerprintCheckedAt = now;
                if (found.m_Fingerprint == ReadFingerprint(packagePath)) {
                    return found;
                }
            }
            var entry = new PackageEntry {
                m_Fingerprint = ReadFingerprint(packagePath),
                m_FingerprintCheckedAt = now
            };
            // ここへ来るのは初回か、ファイルが差し替わったときだけなので指紋の読み直しは負担にならない
            s_Entries[packagePath] = entry;
            // 解析し直しになるため、これまでの判定結果の控えは使えなくなる
            s_Generation += 1;
            s_ParseQueue.Enqueue(packagePath);
            StartWorker();
            return entry;
        }

        // ファイルの更新時刻と大きさをまとめた指紋。読めない場合は 0
        private static long ReadFingerprint(string packagePath)
        {
            try {
                var info = new FileInfo(packagePath);
                if (!info.Exists) {
                    return 0;
                }
                return info.LastWriteTimeUtc.Ticks ^ info.Length;
            } catch (Exception) {
                return 0;
            }
        }

        // 解析スレッドは常に1本だけ動かし、待ち行列を順に片付ける
        private static void StartWorker()
        {
            var alreadyRunning = Interlocked.CompareExchange(ref s_WorkerState, 1, 0) == 1;
            if (alreadyRunning) {
                return;
            }
            Task.Run((Action)ParsePendingPackages);
        }

        private static void ParsePendingPackages()
        {
            while (s_ParseQueue.TryDequeue(out var packagePath)) {
                var assets = UnityPackageArchive.ReadAssets(packagePath);
                var result = new UnityPackageAsset[assets.Count];
                for (var i = 0; i < assets.Count; i += 1) {
                    result[i] = assets[i];
                }
                s_ParseResults.Enqueue(new KeyValuePair<string, UnityPackageAsset[]>(packagePath, result));
            }
            Interlocked.Exchange(ref s_WorkerState, 0);
            // 停止を決めた直後に追加された分を取りこぼさないよう、残っていれば起動し直す
            if (!s_ParseQueue.IsEmpty) {
                StartWorker();
            }
        }

        // 解析結果をメインスレッドで受け取る。Unity の API を使う照合はここから先で行う
        private static void TakeParseResults()
        {
            var received = false;
            while (s_ParseResults.TryDequeue(out var result)) {
                if (s_Entries.TryGetValue(result.Key, out var entry)) {
                    entry.m_Assets = result.Value;
                    entry.m_Guids = new HashSet<string>(result.Value.Select(asset => asset.r_Guid), StringComparer.OrdinalIgnoreCase);
                    entry.m_AuthorTimeUtc = LatestAuthorTime(result.Value);
                    entry.m_Resolved = false;
                    received = true;
                }
            }
            if (received) {
                NotifyStateChanged();
            }
        }

        private static DateTime LatestAuthorTime(UnityPackageAsset[] assets)
        {
            var latest = DateTime.MinValue;
            foreach (var asset in assets) {
                if (asset.r_AuthorTimeUtc > latest) {
                    latest = asset.r_AuthorTimeUtc;
                }
            }
            return latest;
        }

        /// <summary>
        /// 収録アセットを現在のプロジェクトと突き合わせ、
        /// プロジェクト側のファイルと大きさが一致する割合と、存在する収録アセットの共通の親フォルダを求める。
        /// </summary>
        private static void Resolve(PackageEntry entry)
        {
            var folders = new List<string>();
            var matchedCount = 0;
            foreach (var asset in entry.m_Assets) {
                var assetPath = AssetDatabase.GUIDToAssetPath(asset.r_Guid);
                var exists = !string.IsNullOrEmpty(assetPath) && File.Exists(assetPath);
                if (exists) {
                    folders.Add(FolderOf(assetPath));
                    if (new FileInfo(assetPath).Length == asset.r_Size) {
                        matchedCount += 1;
                    }
                }
            }
            var matchRate = 0.0;
            if (entry.m_Assets.Length > 0) {
                matchRate = (double)matchedCount / entry.m_Assets.Length;
            }
            entry.m_Resolved = true;
            entry.m_MatchRate = matchRate;
            entry.m_RootAssetPath = CommonRootFolder(folders);
        }

        private static string FolderOf(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath) ?? "";
            return directory.Replace('\\', '/');
        }

        /// <summary>
        /// 複数のフォルダパスに共通する先頭部分を求める。
        /// 共通部分が無い場合は Assets フォルダを返す。
        /// </summary>
        private static string CommonRootFolder(List<string> folders)
        {
            if (folders.Count == 0) {
                return "";
            }
            var common = folders[0].Split('/');
            var commonLength = common.Length;
            foreach (var folder in folders) {
                var segments = folder.Split('/');
                var limit = Math.Min(commonLength, segments.Length);
                var matched = 0;
                while (matched < limit && string.Equals(common[matched], segments[matched], StringComparison.Ordinal)) {
                    matched += 1;
                }
                commonLength = matched;
            }
            if (commonLength > 0) {
                return string.Join("/", common, 0, commonLength);
            }
            return "Assets";
        }

        private static void NotifyStateChanged()
        {
            // 照合結果や解析結果が変わったので、判定結果の控えを使えなくする
            s_Generation += 1;
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// アセットの追加・削除・移動があったら、取り込み済み判定を照合し直させる。
    /// </summary>
    internal sealed class ImportedPackageIndexWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            ImportedPackageIndex.InvalidateResolved();
        }
    }
}
