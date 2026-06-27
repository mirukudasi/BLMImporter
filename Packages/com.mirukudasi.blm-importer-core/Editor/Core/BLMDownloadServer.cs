using System;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor.Core
{
    /// <summary>
    /// 拡張からのダウンロード完了通知を受ける 127.0.0.1 限定のローカル HTTP サーバ。
    /// POST /complete（ヘッダ X-BLM-Token またはクエリ token でセッショントークン照合）を受けると
    /// 完了シグナルを立て、インポートダイアログがポーリングを待たず即時に再読込できるようにする。
    /// 失敗してもダイアログ側の5秒ポーリングで完了検知できるため、ベストエフォートでよい。
    /// </summary>
    [InitializeOnLoad]
    public static class BLMDownloadServer
    {
        public const int c_Port = 48750;
        private const string c_KeyActive = "BLMImporter.DownloadServer.Active";
        private const string c_KeyToken = "BLMImporter.DownloadServer.Token";

        private static HttpListener s_Listener = null;
        private static Thread s_Thread = null;
        // バックグラウンドスレッドから参照するため SessionState ではなくフィールドに持つ
        private static volatile string s_Token = "";
        private static int s_CompletionSignal = 0;

        // ダウンロードボタン押下〜完了通知（またはキャンセル）までの「ダウンロード中」状態。
        // この間は他のダウンロード操作と5秒ポーリングをロックし、完了通知での即時反映に任せる。
        private static volatile bool s_Downloading = false;

        static BLMDownloadServer()
        {
            // ドメインリロード後、ダウンロード待ち中だった場合は起動し直す
            if (SessionState.GetBool(c_KeyActive, false)) {
                EnsureStarted();
            }
        }

        public static int Port => c_Port;
        public static string Token => s_Token;

        /// <summary>サーバを起動（未起動なら）。トークンはセッション内で固定し、リロードを跨いで再利用する。</summary>
        public static void EnsureStarted()
        {
            SessionState.SetBool(c_KeyActive, true);
            var token = SessionState.GetString(c_KeyToken, "");
            if (string.IsNullOrEmpty(token)) {
                token = Guid.NewGuid().ToString("N");
                SessionState.SetString(c_KeyToken, token);
            }
            s_Token = token;

            if (s_Listener != null) {
                return;
            }
            try {
                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + c_Port + "/");
                listener.Start();
                s_Listener = listener;
                s_Thread = new Thread(Loop) { 
                    IsBackground = true, 
                    Name = "BLMDownloadServer" 
                };
                s_Thread.Start();
            }
            catch (Exception exception) {
                Debug.LogWarning("ダウンロード完了サーバの起動に失敗しました（5秒ポーリングで継続します）: " + exception.Message);
                s_Listener = null;
            }
        }

        // メインスレッドで呼ぶ。前回以降に完了通知があれば true（呼ぶと内部カウンタはリセット）。
        public static bool ConsumeCompletionSignal()
        {
            return Interlocked.Exchange(ref s_CompletionSignal, 0) > 0;
        }

        /// <summary>ダウンロード開始時に呼ぶ。完了通知かキャンセルまで IsDownloading=true になる。</summary>
        public static void MarkDownloadStarted()
        {
            s_Downloading = true;
        }

        /// <summary>完了通知・キャンセルでロックを解除する。</summary>
        public static void MarkDownloadFinished()
        {
            s_Downloading = false;
        }

        /// <summary>ダウンロード中か（完了通知またはキャンセルで解除）。</summary>
        public static bool IsDownloading => s_Downloading;

        /// <summary>
        /// ダウンロード開始時に呼ぶ。完了通知サーバを起動し「ダウンロード中」ロックを立て、
        /// URLに載せる接続情報（ポート/トークン）を返す。URL組み立て自体は呼び出し側で行う。
        /// </summary>
        public static (int port, string token) BeginDownload()
        {
            EnsureStarted();
            MarkDownloadStarted();
            return (c_Port, s_Token);
        }

        private static void Loop()
        {
            var listener = s_Listener;
            while (listener != null && listener.IsListening) {
                HttpListenerContext context;
                try {
                    context = listener.GetContext();
                }
                catch {
                    break; // Stop/Dispose で抜ける
                }
                try {
                    Handle(context);
                }
                catch {
                    // 1リクエストの失敗は無視
                }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-BLM-Token");
            response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");

            if (request.HttpMethod == "OPTIONS") {
                WriteJson(response, 204, "");
                return;
            }
            var path = request.Url.AbsolutePath;
            if (path == "/ping") {
                WriteJson(response, 200, "{\"ok\":true}");
                return;
            }
            if (path == "/complete" && request.HttpMethod == "POST") {
                var token = request.Headers["X-BLM-Token"];
                if (string.IsNullOrEmpty(token)) {
                    token = request.QueryString["token"];
                }
                var expected = s_Token;
                if (string.IsNullOrEmpty(expected) || token != expected) {
                    WriteJson(response, 403, "{\"ok\":false,\"error\":\"token\"}");
                    return;
                }
                Interlocked.Increment(ref s_CompletionSignal);
                s_Downloading = false; // 完了通知でロック解除
                WriteJson(response, 200, "{\"ok\":true}");
                return;
            }
            WriteJson(response, 404, "{\"ok\":false}");
        }

        private static void WriteJson(HttpListenerResponse response, int status, string body)
        {
            response.StatusCode = status;
            if (!string.IsNullOrEmpty(body)) {
                response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body);
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            response.Close();
        }
    }
}
