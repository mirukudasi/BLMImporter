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
    /// BOOTH Library Manager の data.db を読み込み、購入済みアイテムを一覧から選んで
    /// Unityプロジェクトへインポートするためのエディタウィンドウ。
    /// データ処理は Core 側（LibraryData / ItemFilter / PackageImporter）が担い、
    /// このクラスは描画と入力の処理に専念する。
    /// </summary>
    public partial class BLMImporterWindow : EditorWindow
    {
        private LibraryRuntimeSnapshot snapshot = null;
        private string loadError = "";

        // ドメインリロード後はフィールド初期化子が再実行されないため OnEnable で生成する
        private ThumbnailCache thumbnails = null;
        private ImportSelection selection = null;
        private BLMImporterWindowSettings settings = null;
        // 検索窓で絞り込み中のタグと、その一致方法（AND/OR）
        private List<string> selectedTags = null;
        // 表示名は ItemSortMode.GetName() が単一管理。ポップアップ用ラベルはenumから生成する
        private static readonly string[] SortLabels = Enum.GetValues(typeof(ItemSortMode))
            .Cast<ItemSortMode>()
            .Select(mode => mode.GetName())
            .ToArray();

        private string searchText = "";
        private int categoryFilterIndex = 0;
        // カテゴリ選択の先頭に置く、絞り込まない選択肢の表示名
        private const string c_AllLabel = "すべて";
        private string[] categoryOptions = new[] { c_AllLabel };
        private int listFilterIndex = 0;

        // ページング
        private int currentPage = 0;
        private string lastFilterKey = "";
        private static readonly int[] PageSizeChoices = { 20, 50, 100, 200 };
        private static readonly GUIContent[] PageSizeLabels = PageSizeChoices.Select(size => new GUIContent(size.ToString())).ToArray();

        private ItemRuntime focusedItem = null;
        private Vector2 listScroll = Vector2.zero;
        private Vector2 detailScroll = Vector2.zero;
        private Vector2 tagScroll = Vector2.zero;
        private Vector2 packageScroll = Vector2.zero;
        private Vector2 suggestScroll = Vector2.zero;
        private Vector2 chipScroll = Vector2.zero;
        // 一覧スクロールビューの表示領域（コンテンツ座標）。サムネイルの遅延ロード判定に使う
        private Rect listViewport = new Rect(0f, 0f, 0f, 0f);

        private const float c_RowHeight = 58f;
        private const float c_PackageRowHeight = 34f;
        // 行のクリック判定から外す、左端のチェックボックスと右端のボタンの幅
        private const float c_RowCheckboxHitWidth = 30f;
        private const float c_PackageCheckboxHitWidth = 28f;
        private const float c_PackageButtonHitWidth = 96f;
        private bool draggingSplitter = false;
        private const float c_ListWidthMin = 260f;
        private const float c_ListWidthMax = 640f;
        private const float c_SplitterWidth = 5f;
        // タグ・unitypackageリストを収める固定枠の高さ（枠内でスクロールする）
        private const float c_TagsHeight = 84f;
        private const float c_PackageListHeight = 180f;
        // タグストリップ（候補・絞り込み）のチップ帯の高さ。横スクロールバーが要るときだけ下に伸ばす
        private const float c_ChipBand = 22f;
        // タグのチップ同士の間隔
        private const float c_ChipSpacing = 4f;
        // 絞り込みタグ行の右側操作群（すべて解除＋一致AND/OR＋ラベル＋余白）の確保幅。DrawTagMatchMode の配置と揃える
        private const float c_ChipRightReserve = 210f;

        // GUIスタイルはOnGUI中にしか作れないため遅延生成する
        private BLMWindowStyles styles = null;

        [MenuItem("Tools/BLMImporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<BLMImporterWindow>("BLMImporter");
            window.minSize = new Vector2(820, 480);
        }

        protected virtual void OnEnable()
        {
            thumbnails = new ThumbnailCache();
            selection = new ImportSelection();
            selectedTags = new List<string>();
            categoryOptions = new[] { c_AllLabel };
            settings = new BLMImporterWindowSettings();
            settings.Load();
            thumbnails.Repaint += Repaint;
            ImportedPackageIndex.StateChanged += Repaint;
            SequentialPackageImporter.StateChanged += Repaint;
            EditorApplication.update += OnEditorUpdate;
            wantsMouseMove = true;
            Reload();
        }

        protected virtual void OnDisable()
        {
            if (thumbnails != null)
            {
                thumbnails.Repaint -= Repaint;
                thumbnails.Dispose();
            }
            ImportedPackageIndex.StateChanged -= Repaint;
            SequentialPackageImporter.StateChanged -= Repaint;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (thumbnails == null)
            {
                return;
            }
            thumbnails.Update();
        }

        protected virtual void OnGUI()
        {
            if (styles == null)
            {
                styles = new BLMWindowStyles();
            }
            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
            }

            DrawHeaderBar();
            if (snapshot == null)
            {
                return;
            }
            var visibleItems = DrawFilterBar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawItemList(visibleItems);
                DrawSplitter();
                DrawDetailPane();
            }

            DrawFooter();
        }

        private void Reload()
        {
            // 再読み込み後も同じアイテムを開き直せるよう、表示中アイテムのIDを控える
            var focusedItemId = focusedItem?.Id;

            loadError = "";
            try
            {
                // 共有キャッシュを破棄して最新の実データを読み直す（プレビューはダミーを返すため影響なし）
                LibraryRuntimeSnapshot.ClearCache();
                snapshot = LoadSnapshot();
                BuildCategoryOptions();
                // 選択状態は消さず、新しいスナップショットに存在するものだけ残す（値等価で再マッチ）
                selection.PruneTo(snapshot);
                focusedItem = snapshot.FindItem(focusedItemId);
                loadError = ResolveLoadError(snapshot);
            }
            catch (Exception exception)
            {
                snapshot = null;
                focusedItem = null;
                loadError = $"読み込みに失敗しました: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        // ---- プレビュー版（スクリーンショット用）で差し替える拡張ポイント ----

        /// <summary>ライブラリのスナップショットを読み込む。実ウィンドウは共有 Current を使う。</summary>
        protected virtual LibraryRuntimeSnapshot LoadSnapshot()
        {
            return LibraryRuntimeSnapshot.Current;
        }

        /// <summary>読み込んだスナップショットの問題点を文章で返す（問題なければ空文字）。</summary>
        protected virtual string ResolveLoadError(LibraryRuntimeSnapshot loaded)
        {
            if (string.IsNullOrEmpty(loaded.r_LibraryPath) || !Directory.Exists(loaded.r_LibraryPath))
            {
                return $"ライブラリフォルダが見つかりません: {loaded.r_LibraryPath}";
            }
            return "";
        }

        /// <summary>サムネイルURLに対応するテクスチャを返す（未取得ならnull）。</summary>
        protected virtual Texture2D GetThumbnail(Url url)
        {
            return thumbnails.Get(url);
        }

        private void BuildCategoryOptions()
        {
            var categories = snapshot.SubCategoryNames();
            categories.Insert(0, c_AllLabel);
            categoryOptions = categories.ToArray();
            categoryFilterIndex = 0;
        }

        // ---- 共通描画ヘルパ ----

        private void DrawSeparator()
        {
            EditorGUILayout.Space(6);
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, styles.Separator);
            EditorGUILayout.Space(4);
        }

        // 横スクロールバーが出るときだけ、その高さぶんの余白を返す
        private static float ScrollbarAllowance(bool needsScroll)
        {
            if (needsScroll)
            {
                return ScrollbarHeight();
            }
            return 0f;
        }

        // スタイルの高さが未設定のときに使う既定値へ置き換える
        private static float PositiveOr(float value, float fallback)
        {
            if (value > 0f)
            {
                return value;
            }
            return fallback;
        }

        // 行の上端と高さを基準に、指定の大きさの矩形を行の縦中央へ置く
        private static Rect CenterVertically(float x, float top, float rowHeight, float width, float height)
        {
            return new Rect(x, top + (rowHeight - height) * 0.5f, width, height);
        }

        // リンクのように見えるボタンを描き、押されたら指定の処理を行う。マウスを重ねるとリンク用の矢印になる
        private static void LinkButton(string text, GUIStyle style, Action onClick, params GUILayoutOption[] options)
        {
            if (GUILayout.Button(text, style, options))
            {
                onClick();
            }
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        }

        // 詳細ペインの区切り線と見出し
        private void DrawSectionHeader(string title)
        {
            DrawSeparator();
            GUILayout.Label(title, styles.SectionHeader);
            EditorGUILayout.Space(2);
        }
    }
}
