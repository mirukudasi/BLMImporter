using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// サムネイルを永続フォルダにキャッシュする
    /// 取得は可視行のみで呼ばれる前提で、各サムネイルの更新時刻が「取得実行時刻」より古い（またはキャッシュ無し）の場合のみ
    /// 取得実行時刻はキャッシュフォルダ内のメタファイルで管理する
    /// </summary>
    public sealed class ThumbnailCache : IDisposable
    {
        /// <summary>
        /// 1件分のサムネイルダウンロード
        /// URLと通信状態をまとめて持ち、Disposeで通信を破棄する
        /// </summary>
        private sealed class ThumbnailRequest : IDisposable
        {
            private readonly string r_Url;
            private readonly UnityWebRequest r_Request;

            public ThumbnailRequest(string url)
            {
                r_Url = url;
                r_Request = UnityWebRequestTexture.GetTexture(url);
                // pximg は Referer を要求する場合があるため付与する
                r_Request.SetRequestHeader("Referer", "https://booth.pm/");
                r_Request.SendWebRequest();
            }

            public string Url => r_Url;
            public bool IsDone => r_Request.isDone;
            public byte[] RawData => r_Request.downloadHandler.data;
            public bool Succeeded => r_Request.result == UnityWebRequest.Result.Success;
            public string FailureMessage {
                get {
                    if (Succeeded) {
                        return "";
                    }
                    var error = string.IsNullOrEmpty(r_Request.error) ? r_Request.result.ToString() : r_Request.error;
                    if (r_Request.responseCode > 0) {
                        error += " (HTTP " + r_Request.responseCode + ")";
                    }
                    return error;
                }
            }

            /// <summary>
            /// ダウンロード成功時のみテクスチャを返す
            /// 失敗時null
            /// </summary>
            public Texture2D LoadTexture()
            {
                if (!Succeeded) {
                    return null;
                }
                return DownloadHandlerTexture.GetContent(r_Request);
            }

            public void Dispose()
            {
                r_Request.Dispose();
            }
        }

        // サーバー負荷を避けるため直列ダウンロードとし、1件の開始間隔を最低この時間空ける
        private static readonly TimeSpan m_MinStartInterval = TimeSpan.FromSeconds(0.2);
        // キャッシュ更新の再実行を許可するまでの待ち時間
        private static readonly TimeSpan m_FetchCooldown = TimeSpan.FromDays(1);
        // 取得失敗後、同じURLを再試行するまでの待ち時間
        private static readonly TimeSpan m_FailureRetryDelay = TimeSpan.FromMinutes(30);
        private const string c_CacheFileExtension = ".img";

        private readonly Dictionary<string, Texture2D> r_Textures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, string> r_DiskPaths = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> r_FailedUntilUtc = new Dictionary<string, DateTime>();
        private readonly HashSet<string> r_InvalidDiskCacheUrls = new HashSet<string>();
        private readonly Queue<string> r_DownloadQueue = new Queue<string>();
        private readonly HashSet<string> r_QueuedOrActive = new HashSet<string>();
        private readonly string r_CacheDir;
        private readonly string r_MetaPath;
        // 直列ダウンロードのため同時に動くリクエストは常に1件
        private ThumbnailRequest m_Active = null;
        // 「キャッシュ取得を実行した時刻」。各サムネイルの更新時刻がこれより前なら再取得する
        private DateTime? m_FetchRequestedUtc = null;
        // 直近にダウンロードを開始した時刻。開始間隔の制御に使う
        private DateTime m_LastStartUtc = DateTime.MinValue;

        public event Action Repaint;

        public ThumbnailCache()
        {
            r_CacheDir = Path.Combine(Application.persistentDataPath, "BLMThumbs");
            r_MetaPath = Path.Combine(r_CacheDir, "fetch_requested.txt");
            Directory.CreateDirectory(r_CacheDir);
            m_FetchRequestedUtc = ReadMeta();
        }

        /// <summary>
        /// 通信中リクエストを破棄し、キューと保持テクスチャを片付ける。ウィンドウ破棄時に呼ぶ。
        /// </summary>
        public void Dispose()
        {
            if (m_Active != null) {
                m_Active.Dispose();
                m_Active = null;
            }
            r_DownloadQueue.Clear();
            r_QueuedOrActive.Clear();
            foreach (var texture in r_Textures.Values) {
                if (texture != null) {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            r_Textures.Clear();
        }

        /// <summary>
        /// 進行中・待機中のダウンロード件数
        /// </summary>
        public int RemainingCount {
            get {
                if (m_Active != null) {
                    return r_DownloadQueue.Count + 1;
                }
                return r_DownloadQueue.Count;
            }
        }

        /// <summary>
        /// キャッシュ更新を実行できるか。
        /// 前回実行から1日経過するまでは不可。
        /// </summary>
        public bool CanRequestFetch {
            get {
                if (!m_FetchRequestedUtc.HasValue) {
                    return true;
                }
                return DateTime.UtcNow - m_FetchRequestedUtc.Value >= m_FetchCooldown;
            }
        }

        /// <summary>
        /// キャッシュ取得を実行した時刻を現在時刻で記録する
        /// 以降、これより古いサムネイルが遅延再取得される
        /// </summary>
        public void RequestFetchNow()
        {
            var now = DateTime.UtcNow;
            m_FetchRequestedUtc = now;
            r_FailedUntilUtc.Clear();
            try {
                File.WriteAllText(r_MetaPath, now.ToString("o", CultureInfo.InvariantCulture));
            } catch (Exception e) {
                // メタファイル書き込み失敗は無視する
                Debug.LogWarning(e);
            }
        }

        private DateTime? ReadMeta()
        {
            try {
                if (!File.Exists(r_MetaPath)) {
                    return null;
                }
                var text = File.ReadAllText(r_MetaPath).Trim();
                var parsed = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value);
                if (!parsed) {
                    return null;
                }
                return value;
            } catch (Exception e) {
                Debug.LogWarning("サムネイルキャッシュ更新時刻の読み込みに失敗しました: " + r_MetaPath + "\n" + e);
                return null;
            }
        }

        /// <summary>
        /// キャッシュ済みのテクスチャを返す。各サムネイルの更新時刻が「取得実行時刻」より前、
        /// またはキャッシュ自体が無い場合は遅延ダウンロードをキューへ積む（可視行のみで呼ぶ前提）。
        /// 新版のダウンロード完了までは、あれば旧版を返す。
        /// </summary>
        /// <summary>Url 値型から取得する糖衣。内部は文字列キーで扱う。</summary>
        public Texture2D Get(Url url) => Get(url.r_Value);

        public Texture2D Get(string url)
        {
            if (string.IsNullOrEmpty(url)) {
                return null;
            }

            var diskPath = DiskPathFor(url);
            var fileExists = File.Exists(diskPath);
            if (NeedsRefresh(fileExists, diskPath)) {
                EnqueueDownload(url);
            }

            // UnloadUnusedAssets等で破棄された参照は捨てて読み直す
            if (r_Textures.TryGetValue(url, out var cached)) {
                if (cached != null) {
                    return cached;
                }
                r_Textures.Remove(url);
            }
            if (fileExists && !r_InvalidDiskCacheUrls.Contains(url)) {
                var texture = LoadFromDisk(url, diskPath);
                if (texture != null) {
                    return texture;
                }
                EnqueueDownload(url);
            } else if (fileExists) {
                EnqueueDownload(url);
            }
            return null;
        }

        /// <summary>
        /// キャッシュが無ければ常に取得
        /// ある場合は更新時刻が取得実行時刻より前のときだけ再取得する
        /// </summary>
        private bool NeedsRefresh(bool fileExists, string diskPath)
        {
            if (!fileExists) {
                return true;
            }
            if (!m_FetchRequestedUtc.HasValue) {
                return false;
            }
            var fileTime = File.GetLastWriteTimeUtc(diskPath);
            return fileTime < m_FetchRequestedUtc.Value;
        }

        private Texture2D LoadFromDisk(string url, string diskPath)
        {
            var texture = new Texture2D(2, 2);
            try {
                var bytes = File.ReadAllBytes(diskPath);
                if (texture.LoadImage(bytes)) {
                    // UnloadUnusedAssets で破棄されないようにする
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    r_Textures[url] = texture;
                    return texture;
                }
                UnityEngine.Object.DestroyImmediate(texture);
                MarkDiskCacheInvalid(url, "サムネイルキャッシュが壊れているため再取得します: " + diskPath);
                return null;
            } catch (Exception e) {
                if (texture != null) {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                MarkDiskCacheInvalid(url, "サムネイルキャッシュの読み込みに失敗したため再取得します: " + diskPath + "\n" + e);
                return null;
            }
        }

        private void MarkDiskCacheInvalid(string url, string message)
        {
            if (r_InvalidDiskCacheUrls.Add(url)) {
                Debug.LogWarning(message);
            }
        }

        /// <summary>
        /// BOOTHからのダウンロードを待機キューへ積む
        /// </summary>
        /// <param name="url"></param>
        private void EnqueueDownload(string url)
        {
            if (r_QueuedOrActive.Contains(url)) {
                return;
            }
            if (IsRetryBlocked(url)) {
                return;
            }
            r_QueuedOrActive.Add(url);
            r_DownloadQueue.Enqueue(url);
        }

        private bool IsRetryBlocked(string url)
        {
            if (!r_FailedUntilUtc.TryGetValue(url, out var retryAfterUtc)) {
                return false;
            }
            if (DateTime.UtcNow < retryAfterUtc) {
                return true;
            }
            r_FailedUntilUtc.Remove(url);
            return false;
        }

        /// <summary>
        /// EditorWindow の update から毎フレーム呼び、ダウンロードを進行させる
        /// </summary>
        public void Update()
        {
            var finished = m_Active != null && m_Active.IsDone;
            if (finished) {
                HandleCompleted();
            }
            PromoteQueue();
            if (finished && Repaint != null) {
                Repaint();
            }
        }

        /// <summary>
        /// 直列（同時1件）かつ前回開始から最低 MinStartInterval 空けて次の1件を開始する
        /// </summary>
        private void PromoteQueue()
        {
            if (m_Active != null || r_DownloadQueue.Count == 0) {
                return;
            }
            var elapsed = DateTime.UtcNow - m_LastStartUtc;
            if (elapsed < m_MinStartInterval) {
                return;
            }

            m_LastStartUtc = DateTime.UtcNow;
            var url = r_DownloadQueue.Dequeue();
            try {
                m_Active = new ThumbnailRequest(url);
            } catch (Exception e) {
                r_QueuedOrActive.Remove(url);
                MarkDownloadFailed(url, "サムネイル取得リクエストの開始に失敗しました\n" + e);
            }
        }

        private void HandleCompleted()
        {
            try {
                var texture = m_Active.LoadTexture();
                if (texture != null) {
                    // UnloadUnusedAssets で破棄されないようにする
                    texture.hideFlags = HideFlags.HideAndDontSave;
                    r_Textures[m_Active.Url] = texture;
                    r_FailedUntilUtc.Remove(m_Active.Url);
                    if (WriteDiskCache(m_Active.Url, m_Active.RawData)) {
                        r_InvalidDiskCacheUrls.Remove(m_Active.Url);
                    } else {
                        BlockRetryTemporarily(m_Active.Url, "サムネイルキャッシュの保存に失敗したため、再取得を一時的に抑制します: " + m_Active.Url);
                    }
                } else {
                    MarkDownloadFailed(m_Active);
                }
            } catch (Exception e) {
                MarkDownloadFailed(m_Active, "サムネイル画像の読み込みに失敗しました\n" + e);
            } finally {
                r_QueuedOrActive.Remove(m_Active.Url);
                m_Active.Dispose();
                m_Active = null;
            }
        }

        private void MarkDownloadFailed(ThumbnailRequest request, string detail = "")
        {
            var message = request.FailureMessage;
            if (string.IsNullOrEmpty(message)) {
                message = "画像データのデコードに失敗しました";
            }
            if (!string.IsNullOrEmpty(detail)) {
                message += "\n" + detail;
            }
            MarkDownloadFailed(request.Url, message);
        }

        private void MarkDownloadFailed(string url, string message) => BlockRetryTemporarily(url, "サムネイル取得に失敗しました。しばらく再試行を抑制します: " + url + "\n" + message);

        private void BlockRetryTemporarily(string url, string message)
        {
            var retryAfterUtc = DateTime.UtcNow + m_FailureRetryDelay;
            r_FailedUntilUtc[url] = retryAfterUtc;
            Debug.LogWarning(message);
        }

        private bool WriteDiskCache(string url, byte[] data)
        {
            if (data == null || data.Length == 0) {
                Debug.LogWarning("サムネイルキャッシュへ保存する画像データが空です: " + url);
                return false;
            }
            try {
                File.WriteAllBytes(DiskPathFor(url), data);
                return true;
            } catch (Exception e) {
                // キャッシュ書き込み失敗は無視する
                Debug.LogWarning(e);
                return false;
            }
        }

        /// <summary>
        /// URLのMD5をファイル名にする
        /// 毎回ハッシュ計算しないよう結果は使い回す
        /// </summary>
        private string DiskPathFor(string url)
        {
            if (r_DiskPaths.TryGetValue(url, out var known)) {
                return known;
            }

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(url));
            var builder = new StringBuilder();
            foreach (var b in hash) {
                builder.Append(b.ToString("x2"));
            }
            var diskPath = Path.Combine(r_CacheDir, builder.ToString() + c_CacheFileExtension);
            r_DiskPaths[url] = diskPath;
            return diskPath;
        }
    }
}
