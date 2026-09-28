using System;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    /// <summary>複数のウィンドウで共通して使う描画と操作をまとめる。</summary>
    internal static class BLMGuiDraw
    {
        // 枠付きでサムネイルを描画する。未取得時はプレースホルダ背景を出す
        public static void Thumbnail(Rect rect, Texture2D texture, BLMWindowStyles styles)
        {
            EditorGUI.DrawRect(rect, styles.ThumbFrame);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(inner, styles.ThumbBack);
            if (texture != null)
            {
                GUI.DrawTexture(inner, texture, ScaleMode.ScaleToFit);
            }
        }

        // ショップ名とカテゴリ名を区切り記号でつないだ表示文字列を返す
        public static string ShopAndCategory(ItemMaster master)
        {
            return $"{master.r_ShopName}  •  {master.r_SubCategoryName}";
        }

        // 指定した位置に赤いダウンロードボタンを描画する
        public static void DownloadButton(Rect rect, Action onClick)
        {
            using (new GuiBackgroundColorScope(BLMWindowStyles.DownloadButtonColor))
            {
                // ダウンロード中は他のダウンロードをロックする
                using (new EditorGUI.DisabledScope(BLMDownloadServer.IsDownloading))
                {
                    if (GUI.Button(rect, "ダウンロード"))
                    {
                        onClick();
                    }
                }
            }
        }

        // 自動レイアウトで赤いダウンロードボタンを描画する
        public static void DownloadButtonLayout(Action onClick, params GUILayoutOption[] options)
        {
            using (new GuiBackgroundColorScope(BLMWindowStyles.DownloadButtonColor))
            {
                // ダウンロード中は他のダウンロードをロックする
                using (new EditorGUI.DisabledScope(BLMDownloadServer.IsDownloading))
                {
                    if (GUILayout.Button("ダウンロード", options))
                    {
                        onClick();
                    }
                }
            }
        }

        // 完了通知の受け口を用意してから、ブラウザでダウンロード用のページを開く
        public static void OpenDownloadPage(ItemRuntime item, OrderId orderId)
        {
            var server = BLMDownloadServer.BeginDownload();
            Application.OpenURL(item.DownloadUrl(orderId, server.port, server.token));
        }
    }
}
