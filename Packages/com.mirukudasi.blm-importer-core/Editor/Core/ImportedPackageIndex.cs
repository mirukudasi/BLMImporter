using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// unitypackage に対して取るべき操作。プロジェクトに入っている版と作者側の時刻を比べて決める
    /// </summary>
    public enum PackageImportAction
    {
        // 入っている版より新しい、まだ入っていない、または調べ中
        Import,
        // 入っている版そのもの
        Open,
        // 入っている版より古い
        Reimport
    }

    /// <summary>
    /// unitypackage に対して、インポート・開く・再インポートのどれを取るべきかを判定する。
    ///
    /// 同じアイテム内で収録アセットのGUIDが1件でも重なる unitypackage 同士は、同じ系列の別の版とみなす。
    /// 直接重ならなくても、別の unitypackage を介してつながれば同じ系列になる。
    ///
    /// 系列ごとに「現在の版」を1つ選ぶ。GUIDから引いたプロジェクト側のファイルの大きさが
    /// 書庫に記録された大きさと一致する収録アセットの割合を一致率とし、一致率が最も高い版を現在の版とする。
    /// 一致率が同じなら作者側の更新時刻が新しい版を選ぶ。一致するものが1件も無い系列には現在の版が無い。
    ///
    /// 各 unitypackage は、作者側の更新時刻を現在の版と比べて、同じなら開く、古ければ再インポート、
    /// 新しいか現在の版が無ければインポートとする。
    /// 照合はGUIDで行うため、取り込み後にフォルダを移動・改名していても追従する。
    /// 中身は読まずファイルの大きさだけを比べるため軽い。
    ///
    /// unitypackage の解析は重いのでバックグラウンドスレッドで行い、
    /// 描画側は <see cref="GetActions"/> を呼ぶだけで待たされない。
    /// </summary>
    public static class ImportedPackageIndex
    {
        /// <summary>
        /// 1つの unitypackage について分かっていること
        /// </summary>
        private sealed class PackageEntry
        {
            // ファイルの更新時刻と大きさから作る指紋。差し替えの検知に使う
            public long m_Fingerprint = 0;
            // 指紋を最後に確かめたエディタ起動からの経過秒
            public double m_FingerprintCheckedAt = 0.0;
            // 書庫を解析した結果。解析中は null
            public PackageContents m_Contents = null;
            // プロジェクトと照合した結果。未照合、またはアセットが変わって捨てた後は null
            public ProjectMatch m_Match = null;
        }

        /// <summary>
        /// 書庫の解析結果。一度作ったら変わらない
        /// </summary>
        private sealed class PackageContents
        {
            public readonly UnityPackageAsset[] r_Assets;
            // 同じ系列かどうかを見分けるのに使う
            public readonly HashSet<string> r_Guids;
            // 収録アセットのうち最も新しい作者側の更新時刻。この版の新しさにあたる
            public readonly DateTime r_AuthorTimeUtc;

            public PackageContents(IEnumerable<UnityPackageAsset> assets)
            {
                r_Assets = assets.ToArray();
                r_Guids = new HashSet<string>(r_Assets.Select(asset => asset.r_Guid), StringComparer.OrdinalIgnoreCase);
                r_AuthorTimeUtc = r_Assets.Select(asset => asset.r_AuthorTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            }
        }

        /// <summary>
        /// プロジェクトとの照合結果。一度作ったら変わらない
        /// </summary>
        private sealed class ProjectMatch
        {
            // 収録アセットのうち、プロジェクト側のファイルと大きさが一致したものの割合
            public readonly double r_MatchRate;
            // 収録アセットが置かれている共通の親フォルダ
            public readonly string r_RootAssetPath;

            public ProjectMatch(double matchRate, string rootAssetPath)
            {
                r_MatchRate = matchRate;
                r_RootAssetPath = rootAssetPath;
            }
        }

        /// <summary>解析やプロジェクト状態の変化で判定が変わったときに発火する。描画の更新に使う。</summary>
        public static event Action StateChanged;

        private static readonly Dictionary<string, PackageEntry> s_Entries = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        // バックグラウンドで解析した結果をメインスレッドへ渡す受け渡し口
        private static readonly ConcurrentQueue<(string PackagePath, long Fingerprint, PackageContents Contents)> s_ParseResults = new ConcurrentQueue<(string PackagePath, long Fingerprint, PackageContents Contents)>();
        // 描画のたびにファイルを調べると重いため、同じファイルの指紋確認はこの間隔まで省く
        private const double c_FingerprintCheckIntervalSeconds = 5.0;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.update += TakeParseResults;
        }

        /// <summary>
        /// 同じアイテムが持つ unitypackage それぞれに取るべき操作を、パスをキーにして返す。
        /// 系列と現在の版はアイテム内の unitypackage を見比べて決まるため、アイテムの全パスをまとめて渡す。
        /// 1件でも解析が終わっていないうちは全件を <see cref="PackageImportAction.Import"/> にし、
        /// 揃った時点で <see cref="StateChanged"/> で知らせる。
        /// </summary>
        public static IReadOnlyDictionary<string, PackageImportAction> GetActions(IReadOnlyList<string> itemPackagePaths)
        {
            var entries = itemPackagePaths.Select(GetMatchedEntry).ToArray();
            var actions = new Dictionary<string, PackageImportAction>(StringComparer.OrdinalIgnoreCase);
            var hasUnparsed = entries.Any(entry => entry == null);
            if (hasUnparsed) {
                foreach (var packagePath in itemPackagePaths) {
                    actions[packagePath] = PackageImportAction.Import;
                }
            }
            else {
                foreach (var series in CollectSeries(entries)) {
                    var currentVersion = FindCurrentVersion(entries, series);
                    foreach (var index in series) {
                        actions[itemPackagePaths[index]] = DecideAction(entries[index], currentVersion);
                    }
                }
            }
            return actions;
        }

        /// <summary>
        /// 取り込み先として「開く」ときに使うフォルダを返す。
        /// 判定がまだの場合は空文字を返す。存在する収録アセットの共通の親フォルダを返す。
        /// </summary>
        public static string GetRootAssetPath(string packagePath) => GetMatchedEntry(packagePath)?.m_Match.r_RootAssetPath ?? "";

        /// <summary>
        /// プロジェクトのアセットが変わったときに、照合結果だけを捨てる。
        /// unitypackage の解析結果は残すので、次回は照合し直すだけで済む。
        /// </summary>
        internal static void InvalidateMatches()
        {
            foreach (var entry in s_Entries.Values) {
                entry.m_Match = null;
            }
            StateChanged?.Invoke();
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
                            var isNewNeighbor = !visited[other] && entries[current].m_Contents.r_Guids.Overlaps(entries[other].m_Contents.r_Guids);
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
            return series
                .Select(index => entries[index])
                .Where(entry => entry.m_Match.r_MatchRate > 0.0)
                .OrderByDescending(entry => entry.m_Match.r_MatchRate)
                .ThenByDescending(entry => entry.m_Contents.r_AuthorTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// 作者側の更新時刻を現在の版と比べて、この unitypackage に取るべき操作を決める。
        /// 系列に現在の版が無ければ、まだ取り込まれていないのでインポートとする。
        /// </summary>
        private static PackageImportAction DecideAction(PackageEntry entry, PackageEntry currentVersion)
        {
            if (currentVersion != null) {
                return entry.m_Contents.r_AuthorTimeUtc.CompareTo(currentVersion.m_Contents.r_AuthorTimeUtc) switch {
                    > 0 => PackageImportAction.Import,
                    < 0 => PackageImportAction.Reimport,
                    _ => PackageImportAction.Open
                };
            }
            return PackageImportAction.Import;
        }

        /// <summary>
        /// 解析と照合が済んだ項目を返す。まだ解析が終わっていなければ null を返す。
        /// </summary>
        private static PackageEntry GetMatchedEntry(string packagePath)
        {
            var entry = GetEntry(packagePath);
            var isParsed = entry.m_Contents != null;
            if (isParsed) {
                if (entry.m_Match == null) {
                    entry.m_Match = MatchWithProject(entry.m_Contents);
                }
                return entry;
            }
            return null;
        }

        /// <summary>
        /// 記録済みの項目を返す。無ければ作り、解析を始める。
        /// ファイルが差し替わっていた場合も解析し直す。
        /// </summary>
        private static PackageEntry GetEntry(string packagePath)
        {
            var now = EditorApplication.timeSinceStartup;
            var isKnown = s_Entries.TryGetValue(packagePath, out var found);
            var isRecentlyChecked = isKnown && now - found.m_FingerprintCheckedAt < c_FingerprintCheckIntervalSeconds;
            if (isRecentlyChecked) {
                return found;
            }
            var fingerprint = ReadFingerprint(packagePath);
            if (isKnown) {
                found.m_FingerprintCheckedAt = now;
            }
            var isUnchanged = isKnown && found.m_Fingerprint == fingerprint;
            if (isUnchanged) {
                return found;
            }
            return RegisterEntry(packagePath, fingerprint, now);
        }

        // 初回か差し替え時に項目を作り直し、解析を始める
        private static PackageEntry RegisterEntry(string packagePath, long fingerprint, double now)
        {
            var entry = new PackageEntry {
                m_Fingerprint = fingerprint,
                m_FingerprintCheckedAt = now
            };
            s_Entries[packagePath] = entry;
            StartParse(packagePath, fingerprint);
            return entry;
        }

        // ファイルの更新時刻と大きさをまとめた指紋。読めない場合は 0
        private static long ReadFingerprint(string packagePath)
        {
            try {
                var info = new FileInfo(packagePath);
                if (info.Exists) {
                    return info.LastWriteTimeUtc.Ticks ^ info.Length;
                }
                return 0;
            } catch (Exception) {
                return 0;
            }
        }

        // 書庫の読み取りは重いのでバックグラウンドで行い、結果はメインスレッドで受け取る。
        // 読み取りを始めた時点の指紋を付けて、差し替え後に古い結果を使わないようにする
        private static void StartParse(string packagePath, long fingerprint)
        {
            Task.Run(() => s_ParseResults.Enqueue((packagePath, fingerprint, new PackageContents(UnityPackageArchive.ReadAssets(packagePath)))));
        }

        // 解析結果をメインスレッドで受け取る。Unity の API を使う照合はここから先で行う
        private static void TakeParseResults()
        {
            var received = false;
            while (!s_ParseResults.IsEmpty) {
                if (s_ParseResults.TryDequeue(out var result)) {
                    var isCurrentFile = s_Entries.TryGetValue(result.PackagePath, out var entry) && entry.m_Fingerprint == result.Fingerprint;
                    if (isCurrentFile) {
                        entry.m_Contents = result.Contents;
                        entry.m_Match = null;
                        received = true;
                    }
                }
            }
            if (received) {
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 収録アセットを現在のプロジェクトと突き合わせ、
        /// プロジェクト側のファイルと大きさが一致する割合と、存在する収録アセットの共通の親フォルダを求める。
        /// </summary>
        private static ProjectMatch MatchWithProject(PackageContents contents)
        {
            var folders = new List<string>();
            var matchedCount = 0;
            foreach (var asset in contents.r_Assets) {
                var assetPath = AssetDatabase.GUIDToAssetPath(asset.r_Guid);
                var hasAssetPath = !string.IsNullOrEmpty(assetPath);
                if (hasAssetPath) {
                    var file = new FileInfo(assetPath);
                    if (file.Exists) {
                        folders.Add(FolderOf(assetPath));
                    }
                    var isSizeMatched = file.Exists && file.Length == asset.r_Size;
                    if (isSizeMatched) {
                        matchedCount += 1;
                    }
                }
            }
            var matchRate = 0.0;
            if (contents.r_Assets.Length > 0) {
                matchRate = (double)matchedCount / contents.r_Assets.Length;
            }
            return new ProjectMatch(matchRate, CommonRootFolder(folders));
        }

        private static string FolderOf(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            return directory.Replace('\\', '/');
        }

        /// <summary>
        /// 複数のフォルダパスに共通する先頭部分を求める。
        /// 共通部分が無い場合は Assets フォルダを返す。一覧が空なら空文字を返す。
        /// </summary>
        private static string CommonRootFolder(List<string> folders)
        {
            if (folders.Count == 0) {
                return "";
            }
            var common = folders[0].Split('/');
            var commonLength = common.Length;
            foreach (var folder in folders.Skip(1)) {
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
            ImportedPackageIndex.InvalidateMatches();
        }
    }
}
