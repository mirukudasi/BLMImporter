using System;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    /// <summary>GUI.color を一時的に変更し、Dispose時に元へ戻す。</summary>
    internal sealed class GuiColorScope : IDisposable
    {
        private readonly Color previous;

        public GuiColorScope(Color color)
        {
            previous = GUI.color;
            GUI.color = color;
        }

        public void Dispose()
        {
            GUI.color = previous;
        }
    }

    /// <summary>ウィンドウ描画で使うGUIスタイル・配色をまとめて生成する。</summary>
    internal sealed class BLMWindowStyles
    {
        public readonly GUIStyle RowTitle;
        public readonly GUIStyle RowSub;
        public readonly GUIStyle DetailMeta;
        public readonly GUIStyle SectionHeader;
        public readonly GUIStyle Badge;
        public readonly GUIStyle TagChip;
        public readonly GUIStyle Description;
        public readonly GUIStyle NameLink;
        public readonly GUIStyle MetaKey;
        public readonly GUIStyle Link;
        public readonly GUIStyle PackageName;

        public readonly Color Zebra;
        public readonly Color Hover;
        public readonly Color Focus;
        public readonly Color Accent;
        public readonly Color Separator;
        public readonly Color ThumbFrame;
        public readonly Color ThumbBack;

        public BLMWindowStyles()
        {
            var pro = EditorGUIUtility.isProSkin;

            // 組み込みスタイルをコピーすると normal.textColor=(0,0,0,0) のスキン既定色補正が効かず
            // 黒で描画されるため、文字色はすべて明示的に指定する
            var textColor = new Color(0.10f, 0.10f, 0.10f);
            var subColor = new Color(0.35f, 0.35f, 0.35f);
            var linkColor = new Color(0.10f, 0.30f, 0.90f);
            if (pro)
            {
                textColor = new Color(0.82f, 0.82f, 0.82f);
                subColor = new Color(0.62f, 0.62f, 0.62f);
                linkColor = new Color(0.40f, 0.70f, 1.00f);
            }

            RowTitle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip, stretchWidth = true };
            RowSub = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, stretchWidth = true };
            DetailMeta = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            SectionHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            Description = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11 };
            MetaKey = new GUIStyle(EditorStyles.miniLabel) { fixedWidth = 56 };
            PackageName = new GUIStyle(EditorStyles.label) { fontSize = 11, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, stretchWidth = true };
            ApplyTextColor(RowTitle, textColor);
            ApplyTextColor(DetailMeta, textColor);
            ApplyTextColor(SectionHeader, textColor);
            ApplyTextColor(Description, textColor);
            ApplyTextColor(PackageName, textColor);
            ApplyTextColor(RowSub, subColor);
            ApplyTextColor(MetaKey, subColor);

            NameLink = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, wordWrap = true };
            ApplyTextColor(NameLink, linkColor);
            Link = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            ApplyTextColor(Link, linkColor);

            Badge = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1)
            };
            Badge.normal.background = MakeSolidTexture(new Color(0.80f, 0.20f, 0.22f));
            Badge.normal.textColor = Color.white;

            var assetLabel = GUI.skin.FindStyle("AssetLabel");
            if (assetLabel != null)
            {
                TagChip = new GUIStyle(assetLabel);
            }
            else
            {
                TagChip = new GUIStyle(EditorStyles.miniButton);
            }

            if (pro)
            {
                Zebra = new Color(1f, 1f, 1f, 0.022f);
                Hover = new Color(1f, 1f, 1f, 0.05f);
                Separator = new Color(0f, 0f, 0f, 0.30f);
                ThumbBack = new Color(1f, 1f, 1f, 0.06f);
            }
            else
            {
                Zebra = new Color(0f, 0f, 0f, 0.025f);
                Hover = new Color(0f, 0f, 0f, 0.04f);
                Separator = new Color(0f, 0f, 0f, 0.12f);
                ThumbBack = new Color(0f, 0f, 0f, 0.08f);
            }
            Focus = new Color(0.24f, 0.49f, 0.90f, 0.18f);
            Accent = new Color(0.26f, 0.55f, 0.96f, 1f);
            ThumbFrame = new Color(0f, 0f, 0f, 0.35f);
        }

        /// <summary>アイテム状態に応じた表示色を返す。</summary>
        public Color StatusColor(ItemPackageStatus status)
        {
            if (status == ItemPackageStatus.NotDownloaded)
            {
                return new Color(0.95f, 0.65f, 0.30f);
            }
            if (status == ItemPackageStatus.HasUnityPackage)
            {
                return new Color(0.45f, 0.80f, 0.50f);
            }
            return new Color(0.65f, 0.65f, 0.65f);
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // 全状態の文字色を同色にする（スキン側の状態別色を無効化する）
        private static void ApplyTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
        }
    }
}
