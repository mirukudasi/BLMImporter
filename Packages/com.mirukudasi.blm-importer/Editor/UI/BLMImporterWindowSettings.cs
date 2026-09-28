using BLMImporter.Editor.Core;
using UnityEditor;

namespace BLMImporter.Editor
{
    /// <summary>
    /// メインウィンドウの表示設定を EditorPrefs に保存・復元する。値が変わったときだけ書き込む
    /// </summary>
    internal sealed class BLMImporterWindowSettings
    {
        // 永続化する設定の EditorPrefs キー
        private const string c_PrefInteractive = "BLMImporter.InteractiveImport";
        private const string c_PrefPageSize = "BLMImporter.PageSize";
        private const string c_PrefShowAdult = "BLMImporter.ShowAdult";
        private const string c_PrefListWidth = "BLMImporter.ListWidth";
        private const string c_PrefTagMatchMode = "BLMImporter.TagMatchMode";
        private const string c_PrefSortMode = "BLMImporter.SortMode";
        private const string c_PrefSortDescending = "BLMImporter.SortDescending";

        private bool m_InteractiveImport = true;
        private int m_PageSize = 50;
        private bool m_ShowAdult = true;
        private float m_ListWidth = 380f;
        private TagMatchMode m_TagMatchMode = TagMatchMode.AND;
        private ItemSortMode m_SortMode = ItemSortMode.Name;
        private bool m_SortDescending = false;

        public void Load()
        {
            m_InteractiveImport = EditorPrefs.GetBool(c_PrefInteractive, true);
            m_PageSize = EditorPrefs.GetInt(c_PrefPageSize, 50);
            m_ShowAdult = EditorPrefs.GetBool(c_PrefShowAdult, true);
            m_ListWidth = EditorPrefs.GetFloat(c_PrefListWidth, 380f);
            m_TagMatchMode = (TagMatchMode)EditorPrefs.GetInt(c_PrefTagMatchMode, (int)TagMatchMode.AND);
            m_SortMode = (ItemSortMode)EditorPrefs.GetInt(c_PrefSortMode, (int)ItemSortMode.Name);
            m_SortDescending = EditorPrefs.GetBool(c_PrefSortDescending, false);
        }

        public bool InteractiveImport
        {
            get { return m_InteractiveImport; }
            set
            {
                if (value != m_InteractiveImport)
                {
                    m_InteractiveImport = value;
                    EditorPrefs.SetBool(c_PrefInteractive, value);
                }
            }
        }

        public int PageSize
        {
            get { return m_PageSize; }
            set
            {
                if (value != m_PageSize)
                {
                    m_PageSize = value;
                    EditorPrefs.SetInt(c_PrefPageSize, value);
                }
            }
        }

        public bool ShowAdult
        {
            get { return m_ShowAdult; }
            set
            {
                if (value != m_ShowAdult)
                {
                    m_ShowAdult = value;
                    EditorPrefs.SetBool(c_PrefShowAdult, value);
                }
            }
        }

        public TagMatchMode TagMatchMode
        {
            get { return m_TagMatchMode; }
            set
            {
                if (value != m_TagMatchMode)
                {
                    m_TagMatchMode = value;
                    EditorPrefs.SetInt(c_PrefTagMatchMode, (int)value);
                }
            }
        }

        // 一覧の並び順と方向（昇順/降順）
        public ItemSortMode SortMode
        {
            get { return m_SortMode; }
            set
            {
                if (value != m_SortMode)
                {
                    m_SortMode = value;
                    EditorPrefs.SetInt(c_PrefSortMode, (int)value);
                }
            }
        }

        public bool SortDescending
        {
            get { return m_SortDescending; }
            set
            {
                if (value != m_SortDescending)
                {
                    m_SortDescending = value;
                    EditorPrefs.SetBool(c_PrefSortDescending, value);
                }
            }
        }

        // アイテム一覧の幅。スプリッタのドラッグで変更でき、EditorPrefsへ保存する
        public float ListWidth
        {
            get { return m_ListWidth; }
            set { m_ListWidth = value; }
        }

        public void SaveListWidth()
        {
            EditorPrefs.SetFloat(c_PrefListWidth, m_ListWidth);
        }
    }
}
