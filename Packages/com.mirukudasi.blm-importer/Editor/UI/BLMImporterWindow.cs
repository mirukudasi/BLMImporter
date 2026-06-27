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
    public class BLMImporterWindow : EditorWindow
    {
        private bool interactiveImport = true;

        private LibraryRuntimeSnapshot snapshot = null;
        private string loadError = "";

        // ドメインリロード後はフィールド初期化子が再実行されないため OnEnable で生成する
        private ThumbnailCache thumbnails = null;
        // インポート対象として選択中の unitypackage（ItemFile）。選択状態の唯一の正本。
        // アイテム一覧のチェックは、そのアイテム配下の選択数から導出する
        private HashSet<ItemFile> selectedPackageFiles = null;
        // 未ダウンロードのまま選択されたアイテムID（ダウンロード待ち対象）
        private HashSet<ItemId> selectedPendingItemIds = null;
        // 検索窓で絞り込み中のタグと、その一致方法（AND/OR）
        private List<string> selectedTags = null;
        private TagMatchMode tagMatchMode = TagMatchMode.AND;
        // 一覧の並び順と方向（昇順/降順）
        private ItemSortMode sortMode = ItemSortMode.Name;
        private bool sortDescending = false;
        // 表示名は ItemSortMode.GetName() が単一管理。ポップアップ用ラベルはenumから生成する
        private static readonly string[] SortLabels = Enum.GetValues(typeof(ItemSortMode))
            .Cast<ItemSortMode>()
            .Select(mode => mode.GetName())
            .ToArray();

        private string searchText = "";
        private int categoryFilterIndex = 0;
        private string[] categoryOptions = new[] { "すべて" };
        private int listFilterIndex = 0;
        private bool showAdult = true;

        // 永続化する設定の EditorPrefs キー
        private const string c_PrefInteractive = "BLMImporter.InteractiveImport";
        private const string c_PrefPageSize = "BLMImporter.PageSize";
        private const string c_PrefShowAdult = "BLMImporter.ShowAdult";
        private const string c_PrefListWidth = "BLMImporter.ListWidth";
        private const string c_PrefTagMatchMode = "BLMImporter.TagMatchMode";
        private const string c_PrefSortMode = "BLMImporter.SortMode";
        private const string c_PrefSortDescending = "BLMImporter.SortDescending";

        // ページング
        private int currentPage = 0;
        private int pageSize = 50;
        private string lastFilterKey = "";
        private static readonly int[] PageSizeChoices = { 20, 50, 100, 200 };
        private static readonly GUIContent[] PageSizeLabels = {
            new GUIContent("20"), new GUIContent("50"), new GUIContent("100"), new GUIContent("200")
        };

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
        // アイテム一覧の幅。スプリッタのドラッグで変更でき、EditorPrefsへ保存する
        private float listWidth = 380f;
        private bool draggingSplitter = false;
        private const float c_ListWidthMin = 260f;
        private const float c_ListWidthMax = 640f;
        private const float c_SplitterWidth = 5f;
        // タグ・unitypackageリストを収める固定枠の高さ（枠内でスクロールする）
        private const float c_TagsHeight = 84f;
        private const float c_PackageListHeight = 180f;
        // タグストリップ（候補・絞り込み）のチップ帯の高さ。横スクロールバーが要るときだけ下に伸ばす
        private const float c_ChipBand = 22f;
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
            selectedPackageFiles = new HashSet<ItemFile>();
            selectedPendingItemIds = new HashSet<ItemId>();
            selectedTags = new List<string>();
            categoryOptions = new[] { "すべて" };
            interactiveImport = EditorPrefs.GetBool(c_PrefInteractive, true);
            pageSize = EditorPrefs.GetInt(c_PrefPageSize, 50);
            showAdult = EditorPrefs.GetBool(c_PrefShowAdult, true);
            listWidth = EditorPrefs.GetFloat(c_PrefListWidth, 380f);
            tagMatchMode = (TagMatchMode)EditorPrefs.GetInt(c_PrefTagMatchMode, (int)TagMatchMode.AND);
            sortMode = (ItemSortMode)EditorPrefs.GetInt(c_PrefSortMode, (int)ItemSortMode.Name);
            sortDescending = EditorPrefs.GetBool(c_PrefSortDescending, false);
            thumbnails.Repaint += Repaint;
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

            EditorGUILayout.BeginHorizontal();
            DrawItemList(visibleItems);
            DrawSplitter();
            DrawDetailPane();
            EditorGUILayout.EndHorizontal();

            DrawFooter();
        }

        // ---- 設定バー ----

        private void DrawHeaderBar()
        {
            EditorGUILayout.Space(6);

            var downloading = thumbnails.RemainingCount > 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("BLMImporter", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (downloading)
                {
                    GUILayout.Label("キャッシュ更新中: 残り " + thumbnails.RemainingCount + " 件", EditorStyles.miniLabel);
                }

                var updateContent = new GUIContent("キャッシュ更新");
                if (!thumbnails.CanRequestFetch)
                {
                    updateContent.tooltip = "1日おいて試してください";
                }
                using (new EditorGUI.DisabledScope(!thumbnails.CanRequestFetch || downloading))
                {
                    if (GUILayout.Button(updateContent, GUILayout.Width(110)))
                    {
                        thumbnails.RequestFetchNow();
                    }
                }
                if (GUILayout.Button("アプリを開く", GUILayout.Width(100)))
                {
                    Application.OpenURL("booth-library-manager://");
                }
                if (GUILayout.Button("再読み込み", GUILayout.Width(90)))
                {
                    Reload();
                }
            }

            if (!string.IsNullOrEmpty(loadError))
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Error);
            }
        }

        private void Reload()
        {
            // 再読み込み後も同じアイテムを開き直せるよう、表示中アイテムのIDを控える
            ItemId focusedItemId = null;
            if (focusedItem != null)
            {
                focusedItemId = focusedItem.r_Master.r_Id;
            }

            loadError = "";
            try
            {
                // 共有キャッシュを破棄して最新の実データを読み直す（プレビューはダミーを返すため影響なし）
                LibraryRuntimeSnapshot.ClearCache();
                snapshot = LoadSnapshot();
                BuildCategoryOptions();
                // 選択状態は消さず、新しいスナップショットに存在するものだけ残す（値等価で再マッチ）
                PruneSelectionToSnapshot();
                focusedItem = snapshot.FindItem(focusedItemId);
                loadError = ResolveLoadError(snapshot);
            }
            catch (Exception exception)
            {
                snapshot = null;
                focusedItem = null;
                loadError = "読み込みに失敗しました: " + exception.Message;
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
                return "ライブラリフォルダが見つかりません: " + loaded.r_LibraryPath;
            }
            return "";
        }

        /// <summary>サムネイルURLに対応するテクスチャを返す（未取得ならnull）。</summary>
        protected virtual Texture2D GetThumbnail(Url url)
        {
            return thumbnails.Get(url);
        }

        // 再読み込み後、選択中の unitypackage / 未DL選択アイテムのうち、新スナップショットに残っているものだけ保持する
        private void PruneSelectionToSnapshot()
        {
            var existingFiles = new HashSet<ItemFile>(snapshot.r_Items.Values.SelectMany(item => item.UnityPackages));
            selectedPackageFiles.IntersectWith(existingFiles);
            selectedPendingItemIds.IntersectWith(snapshot.r_Items.Keys);
        }

        private void BuildCategoryOptions()
        {
            var categories = snapshot.SubCategoryNames();
            categories.Insert(0, "すべて");
            categoryOptions = categories.ToArray();
            categoryFilterIndex = 0;
        }

        // ---- フィルタバー ----

        // フィルタ条件のUIを描き、現在の条件で絞り込んだアイテム一覧を返す
        private List<ItemRuntime> DrawFilterBar()
        {
            var visibleItems = new List<ItemRuntime>();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                searchText = GUILayout.TextField(searchText, EditorStyles.toolbarSearchField, GUILayout.Width(220));
                DrawSearchCancelButton();
                categoryFilterIndex = EditorGUILayout.Popup(categoryFilterIndex, categoryOptions, EditorStyles.toolbarPopup, GUILayout.Width(160));

                var listTitles = BuildListTitles();
                listFilterIndex = EditorGUILayout.Popup(listFilterIndex, listTitles, EditorStyles.toolbarPopup, GUILayout.Width(140));

                EditorGUI.BeginChangeCheck();
                sortMode = (ItemSortMode)EditorGUILayout.Popup((int)sortMode, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck()) {
                    EditorPrefs.SetInt(c_PrefSortMode, (int)sortMode);
                }
                DrawSortDirectionToggle();

                EditorGUI.BeginChangeCheck();
                showAdult = GUILayout.Toggle(showAdult, "R-18表示", EditorStyles.toolbarButton, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) {
                    EditorPrefs.SetBool(c_PrefShowAdult, showAdult);
                }
                GUILayout.FlexibleSpace();

                visibleItems = ItemSorter.Sort(BuildFilter().Apply(snapshot.r_Items.Values), sortMode, sortDescending);
                var selectionLabel = "選択 " + selectedPackageFiles.Count + " package";
                if (selectedPendingItemIds.Count > 0)
                {
                    selectionLabel += " / 未DL " + selectedPendingItemIds.Count;
                }
                GUILayout.Label(visibleItems.Count + " / " + snapshot.r_Items.Count + " 件   " + selectionLabel, EditorStyles.miniLabel);
            }

            // 検索窓の直下に、選択中タグ（×付き）→ タグ候補 の順で表示する
            DrawSelectedTagChips();
            DrawTagSuggestions();
            return visibleItems;
        }

        // 検索フィールド内（右端）に常時出るクリア用×ボタン。クリックで検索語を消去する。
        private void DrawSearchCancelButton()
        {
            var cancelStyle = FindSearchCancelStyle();
            if (cancelStyle == null)
            {
                // スタイル未提供の環境ではツールバーボタンで代替する
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    searchText = "";
                    GUI.FocusControl(null);
                }
                return;
            }
            // キャンセルボタンスタイルは左マージンが負で、フィールド右端に重なって表示される
            if (GUILayout.Button(GUIContent.none, cancelStyle))
            {
                searchText = "";
                GUI.FocusControl(null);
            }
        }

        // Unity バージョン差（"Seach" 表記のtypo含む）を吸収して検索キャンセルボタンのスタイルを取得する
        private static GUIStyle FindSearchCancelStyle()
        {
            foreach (var name in new[] { "ToolbarSearchCancelButton", "ToolbarSeachCancelButton" })
            {
                var style = GUI.skin.FindStyle(name);
                if (style != null)
                {
                    return style;
                }
            }
            return null;
        }

        // 選択中タグを×付きチップで表示する。チップのクリックで絞り込みを解除する。
        // 全要素を行の縦中央に揃える（手動rect）。チップは左、一致モードと「すべて解除」は右に置く。
        private void DrawSelectedTagChips()
        {
            if (selectedTags.Count == 0)
            {
                return;
            }
            // 幅は currentViewWidth から算出（Layout/Repaintで安定）。見出し(88)と右側操作群(c_ChipRightReserve)を引く
            var chipsWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 88f - c_ChipRightReserve - 8f);
            var needsScroll = ChipsContentWidth(selectedTags, true) > chipsWidth;
            var rowRect = GUILayoutUtility.GetRect(0f, c_ChipBand + (needsScroll ? ScrollbarHeight() : 0f), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }
            // 見出し・操作群・チップはチップ帯（上端 c_ChipBand）の縦中央に揃える。スクロールバーは帯の下に出る
            var bandCenterY = rowRect.y + c_ChipBand * 0.5f;

            var clearWidth = 72f;
            var clearRect = new Rect(rowRect.xMax - 8f - clearWidth, bandCenterY - 9f, clearWidth, 18f);
            if (GUI.Button(clearRect, "すべて解除", EditorStyles.miniButton))
            {
                selectedTags.Clear();
            }
            DrawTagMatchMode(clearRect.x - 8f, bandCenterY);

            DrawMiddleLabel(new Rect(rowRect.x + 8f, rowRect.y, 76f, c_ChipBand), "絞り込みタグ", styles.MetaKey);

            var chipsArea = new Rect(rowRect.x + 88f, rowRect.y, chipsWidth, rowRect.height);
            var removeTarget = DrawChipStrip(chipsArea, selectedTags, true, ref chipScroll);
            if (removeTarget != null)
            {
                selectedTags.Remove(removeTarget);
            }
        }

        // チップ群を横スクロールで描画する。はみ出すときだけ下端に横スクロールバーが出る。
        // withClose=true でチップに「✕」を付ける（選択中タグ=解除）。クリックされたタグを返す（無ければnull）。
        private string DrawChipStrip(Rect area, IReadOnlyList<string> tags, bool withClose, ref Vector2 scroll)
        {
            const float c_ChipSpacing = 4f;
            var contentWidth = ChipsContentWidth(tags, withClose);
            var needsScroll = contentWidth > area.width;
            // スクロールバーが出るぶん、チップを並べる帯はその上側に確保する
            var band = area.height - (needsScroll ? ScrollbarHeight() : 0f);
            var viewRect = new Rect(0f, 0f, contentWidth, band);
            var clicked = (string)null;
            scroll = GUI.BeginScrollView(area, scroll, viewRect, false, false);
            var x = 0f;
            foreach (var tag in tags)
            {
                var content = new GUIContent(withClose ? tag + "  ✕" : tag, withClose ? "クリックで解除" : "クリックで追加");
                var size = styles.TagChip.CalcSize(content);
                var chipRect = new Rect(x, (band - size.y) * 0.5f, size.x, size.y);
                EditorGUIUtility.AddCursorRect(chipRect, MouseCursor.Link);
                if (GUI.Button(chipRect, content, styles.TagChip))
                {
                    clicked = tag;
                }
                x += size.x + c_ChipSpacing;
            }
            GUI.EndScrollView();
            return clicked;
        }

        // チップ群を1列に並べたときの合計幅（スクロール要否・配置に使う）
        private float ChipsContentWidth(IReadOnlyList<string> tags, bool withClose)
        {
            var total = 0f;
            foreach (var tag in tags)
            {
                total += styles.TagChip.CalcSize(new GUIContent(withClose ? tag + "  ✕" : tag)).x + 4f;
            }
            return total;
        }

        private static float ScrollbarHeight()
        {
            var height = GUI.skin.horizontalScrollbar.fixedHeight;
            return height > 0f ? height : 15f;
        }

        // タグの一致方法（AND/OR）と「一致」ラベルを右端 rightX から左へ縦中央で置く。
        private void DrawTagMatchMode(float rightX, float centerY)
        {
            var isAll = tagMatchMode == TagMatchMode.AND;
            var y = centerY - 9f;
            var segWidth = 42f;
            var orRect = new Rect(rightX - segWidth, y, segWidth, 18f);
            var andRect = new Rect(orRect.x - segWidth, y, segWidth, 18f);
            var pickAll = GUI.Toggle(andRect, isAll, new GUIContent("AND", "すべてのタグを含む"), EditorStyles.miniButtonLeft);
            var pickAny = GUI.Toggle(orRect, !isAll, new GUIContent("OR", "いずれかのタグを含む"), EditorStyles.miniButtonRight);

            var next = tagMatchMode;
            if (pickAll && !isAll)
            {
                next = TagMatchMode.AND;
            }
            if (pickAny && isAll)
            {
                next = TagMatchMode.OR;
            }
            if (next != tagMatchMode)
            {
                tagMatchMode = next;
                EditorPrefs.SetInt(c_PrefTagMatchMode, (int)tagMatchMode);
            }
            var labelWidth = 32f;
            var labelRect = new Rect(andRect.x - labelWidth, centerY - 9f, labelWidth, 18f);
            DrawMiddleLabel(labelRect, "一致", styles.RowSub);
        }

        // 与えた矩形の縦中央に、指定スタイルでラベルを描く（スタイルの整列・固定幅は一時的に無効化）
        private static void DrawMiddleLabel(Rect rect, string text, GUIStyle style)
        {
            var previousAlignment = style.alignment;
            var previousFixedWidth = style.fixedWidth;
            style.alignment = TextAnchor.MiddleLeft;
            style.fixedWidth = 0f;
            GUI.Label(rect, text, style);
            style.alignment = previousAlignment;
            style.fixedWidth = previousFixedWidth;
        }

        // 検索文字に部分一致するタグの候補を1行（横スクロール）で表示する。クリックで絞り込みへ追加する。
        // 検索窓はクリアしない（連続で複数タグを足せる。クリアは検索窓の×ボタンで行う）。
        private void DrawTagSuggestions()
        {
            var suggestions = BuildTagSuggestions();
            if (suggestions.Count == 0)
            {
                return;
            }
            EditorGUILayout.Space(2);
            // 幅は currentViewWidth から算出（Layout/Repaintで安定）。同じ幅で「スクロール要否＝高さ」と「エリア幅」を決める
            var chipsWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 56f - 16f);
            var needsScroll = ChipsContentWidth(suggestions, false) > chipsWidth;
            var rowRect = GUILayoutUtility.GetRect(0f, c_ChipBand + (needsScroll ? ScrollbarHeight() : 0f), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }
            DrawMiddleLabel(new Rect(rowRect.x + 8f, rowRect.y, 44f, c_ChipBand), "候補", styles.MetaKey);
            var chipsArea = new Rect(rowRect.x + 56f, rowRect.y, chipsWidth, rowRect.height);
            var clicked = DrawChipStrip(chipsArea, suggestions, false, ref suggestScroll);
            EditorGUILayout.Space(2);
            if (clicked != null)
            {
                AddSelectedTag(clicked);
            }
        }

        // 検索文字を含み、まだ選択していないタグを出現回数順に集める
        private List<string> BuildTagSuggestions()
        {
            return snapshot.SuggestTags(searchText, selectedTags);
        }

        private void AddSelectedTag(string tag)
        {
            if (!selectedTags.Contains(tag))
            {
                selectedTags.Add(tag);
            }
        }

        private string[] BuildListTitles()
        {
            var titles = new List<string> { "リスト: すべて" };
            titles.AddRange(snapshot.r_Lists.Values.Select(list => "★ " + list.r_Master.r_Title));
            titles.AddRange(snapshot.r_SmartLists.Values.Select(smartList => "🔍 " + smartList.r_Master.r_Title));
            return titles.ToArray();
        }

        // フィルタ用のリスト一覧。通常リスト→スマートリストの順（BuildListTitles の並びと揃える）
        private List<IItemList> AllFilterLists()
        {
            return snapshot.r_Lists.Values.Cast<IItemList>()
                .Concat(snapshot.r_SmartLists.Values.Cast<IItemList>())
                .ToList();
        }

        // 昇順／降順を切り替えるボタン（▲=昇順 / ▼=降順）
        private void DrawSortDirectionToggle()
        {
            var arrow = "▲";
            var tooltip = "昇順（クリックで降順）";
            if (sortDescending)
            {
                arrow = "▼";
                tooltip = "降順（クリックで昇順）";
            }
            if (GUILayout.Button(new GUIContent(arrow, tooltip), EditorStyles.toolbarButton, GUILayout.Width(26)))
            {
                sortDescending = !sortDescending;
                EditorPrefs.SetBool(c_PrefSortDescending, sortDescending);
            }
        }

        // UIの選択状態をCore側のフィルタ条件へ詰め替える
        private ItemFilter BuildFilter()
        {
            var filter = new ItemFilter {
                m_Keyword = searchText,
                m_IncludeAdult = showAdult
            };
            if (categoryFilterIndex > 0 && categoryFilterIndex < categoryOptions.Length) {
                filter.m_SubCategoryName = categoryOptions[categoryFilterIndex];
            }
            // index 0 = すべて、1.. = 通常リスト→スマートリストの順。BuildListTitles と同じ並びにする
            var lists = AllFilterLists();
            var listIndex = listFilterIndex - 1;
            if (listIndex >= 0 && listIndex < lists.Count) {
                filter.m_List = lists[listIndex];
            }
            filter.r_Tags.AddRange(selectedTags);
            filter.m_TagMatchMode = tagMatchMode;
            return filter;
        }

        // ---- 共通描画ヘルパ ----

        // 枠付きでサムネイルを描画する。未取得時はプレースホルダ背景を出す
        private void DrawThumbnail(Rect rect, Texture2D texture)
        {
            EditorGUI.DrawRect(rect, styles.ThumbFrame);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            EditorGUI.DrawRect(inner, styles.ThumbBack);
            if (texture != null)
            {
                GUI.DrawTexture(inner, texture, ScaleMode.ScaleToFit);
            }
        }

        private void DrawSeparator()
        {
            EditorGUILayout.Space(6);
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, styles.Separator);
            EditorGUILayout.Space(4);
        }

        // ---- アイテム一覧 ----

        private void DrawItemList(List<ItemRuntime> filtered)
        {
            var columnRect = EditorGUILayout.BeginVertical(GUILayout.Width(listWidth));
            // 表示領域の高さはRepaint時のみ確定するため、その時だけ控える
            if (Event.current.type == EventType.Repaint)
            {
                listViewport = columnRect;
            }

            var filterKey = searchText + "|" + categoryFilterIndex + "|" + listFilterIndex + "|" + showAdult + "|" + pageSize + "|" + tagMatchMode + "|" + sortMode + "|" + sortDescending + "|" + string.Join(",", selectedTags);
            if (filterKey != lastFilterKey)
            {
                currentPage = 0;
                listScroll = Vector2.zero;
                lastFilterKey = filterKey;
            }
            var pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)pageSize));
            currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
            var pageItems = filtered.Skip(currentPage * pageSize).Take(pageSize).ToList();

            DrawSelectAllHeader(filtered);

            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (var i = 0; i < pageItems.Count; i += 1)
            {
                DrawItemRow(pageItems[i], i);
            }
            if (filtered.Count == 0)
            {
                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField("該当するアイテムがありません。", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndScrollView();

            DrawPagination(pageCount);
            EditorGUILayout.EndVertical();
        }

        // 一覧上部の「全選択」チェックボックス。ダウンロード済はunitypackage、未ダウンロードはアイテム単位で対象にする
        private void DrawSelectAllHeader(List<ItemRuntime> filtered)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(6);
                var total = 0;
                var selected = 0;
                foreach (var item in filtered)
                {
                    if (item.IsImportable)
                    {
                        total += item.UnityPackageCount;
                        selected += SelectedPackageCount(item);
                    }
                    else
                    {
                        total += 1;
                        if (selectedPendingItemIds.Contains(item.r_Master.r_Id))
                        {
                            selected += 1;
                        }
                    }
                }
                DrawTriStateToggle(ReserveCenteredToggleRect(18f), total, selected, value => SetAllSelected(filtered, value));

                GUILayout.Label("全選択", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                var selectedItemCount = filtered.Count(IsItemSelected);
                GUILayout.Label(filtered.Count + " 件中 " + selectedItemCount + " 選択", EditorStyles.miniLabel);
            }
        }

        // フィルタ後の全アイテムを選択／解除する（ダウンロード済はpackage、未ダウンロードはアイテム単位）
        private void SetAllSelected(List<ItemRuntime> items, bool selected)
        {
            foreach (var item in items)
            {
                if (item.IsImportable)
                {
                    SetPackagesSelected(item.UnityPackages.ToList(), selected);
                }
                else
                {
                    SetPendingSelected(item.r_Master.r_Id, selected);
                }
            }
        }

        // アイテムが選択されているか（ダウンロード済は1つ以上のpackage、未ダウンロードはアイテム単位）
        private bool IsItemSelected(ItemRuntime item)
        {
            if (item.IsImportable)
            {
                return SelectedPackageCount(item) > 0;
            }
            return selectedPendingItemIds.Contains(item.r_Master.r_Id);
        }

        // アイテム1件のチェックボックス。
        // 未ダウンロードはアイテム単位の選択（ダウンロード待ち）、それ以外は配下 unitypackage のtri-state。
        private void DrawItemSelectionToggle(ItemRuntime item, Rect rect)
        {
            // unitypackageを持たない（未ダウンロード／package無し）アイテムはアイテム単位で選択し、ダウンロード待ちにする
            if (!item.IsImportable)
            {
                var isPending = selectedPendingItemIds.Contains(item.r_Master.r_Id);
                EditorGUI.BeginChangeCheck();
                var toggled = EditorGUI.Toggle(rect, isPending);
                if (EditorGUI.EndChangeCheck())
                {
                    SetPendingSelected(item.r_Master.r_Id, toggled);
                }
                return;
            }
            var packages = item.UnityPackages.ToList();
            var selectedCount = packages.Count(file => selectedPackageFiles.Contains(file));
            DrawTriStateToggle(rect, packages.Count, selectedCount, value => SetPackagesSelected(packages, value));
        }

        private void SetPendingSelected(ItemId itemId, bool selected)
        {
            if (selected)
            {
                selectedPendingItemIds.Add(itemId);
            }
            else
            {
                selectedPendingItemIds.Remove(itemId);
            }
        }

        // 全選択(✓)／一部選択(-)／未選択 を表すチェックボックス。total が0なら無効表示にする。
        // クリック時は onChanged(true=全選択 / false=全解除) を呼ぶ
        private void DrawTriStateToggle(int total, int selectedCount, Action<bool> onChanged)
        {
            var allSelected = total > 0 && selectedCount == total;
            var mixed = selectedCount > 0 && selectedCount < total;
            using (new EditorGUI.DisabledScope(total == 0))
            {
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                var toggled = EditorGUILayout.Toggle(allSelected, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                {
                    onChanged(toggled);
                }
                EditorGUI.showMixedValue = false;
            }
        }

        // rect版（一覧の行で縦中央へ正確に置くため）
        private void DrawTriStateToggle(Rect rect, int total, int selectedCount, Action<bool> onChanged)
        {
            var allSelected = total > 0 && selectedCount == total;
            var mixed = selectedCount > 0 && selectedCount < total;
            using (new EditorGUI.DisabledScope(total == 0))
            {
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                var toggled = EditorGUI.Toggle(rect, allSelected);
                if (EditorGUI.EndChangeCheck())
                {
                    onChanged(toggled);
                }
                EditorGUI.showMixedValue = false;
            }
        }

        // 行内で 16px チェックボックスを縦中央へ置くための rect を確保する（rowHeight は行の見込み高さ）
        private static Rect ReserveCenteredToggleRect(float rowHeight)
        {
            var slot = GUILayoutUtility.GetRect(16f, rowHeight, GUILayout.Width(16f));
            return new Rect(slot.x, slot.y + (slot.height - 16f) * 0.5f, 16f, 16f);
        }

        // ツールバー行内でミニラベルを縦中央に置く（内容幅で確保）
        private void MiddleMiniLabel(string text)
        {
            var rowHeight = EditorStyles.toolbar.fixedHeight > 0f ? EditorStyles.toolbar.fixedHeight : 21f;
            var width = styles.RowSub.CalcSize(new GUIContent(text)).x + 2f;
            var rect = GUILayoutUtility.GetRect(width, rowHeight, GUILayout.Width(width));
            DrawMiddleLabel(rect, text, styles.RowSub);
        }

        // 一覧下部のページ操作。左: 先頭/前、中央: ページ数とページサイズ、右: 次/末尾
        private void DrawPagination(int pageCount)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(currentPage <= 0))
                {
                    if (GUILayout.Button("⏮ 先頭", EditorStyles.toolbarButton, GUILayout.Width(56)))
                    {
                        currentPage = 0;
                        listScroll = Vector2.zero;
                    }
                    if (GUILayout.Button("◀ 前", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    {
                        currentPage -= 1;
                        listScroll = Vector2.zero;
                    }
                }

                GUILayout.FlexibleSpace();
                MiddleMiniLabel((currentPage + 1) + " / " + pageCount + " ページ");
                GUILayout.Space(8);
                MiddleMiniLabel("表示数");
                EditorGUI.BeginChangeCheck();
                pageSize = EditorGUILayout.IntPopup(pageSize, PageSizeLabels, PageSizeChoices, EditorStyles.toolbarPopup, GUILayout.Width(56));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetInt(c_PrefPageSize, pageSize);
                }
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(currentPage >= pageCount - 1))
                {
                    if (GUILayout.Button("次 ▶", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    {
                        currentPage += 1;
                        listScroll = Vector2.zero;
                    }
                    if (GUILayout.Button("末尾 ⏭", EditorStyles.toolbarButton, GUILayout.Width(56)))
                    {
                        currentPage = pageCount - 1;
                        listScroll = Vector2.zero;
                    }
                }
            }
        }


        private void DrawItemRow(ItemRuntime item, int index)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, c_RowHeight, GUILayout.ExpandWidth(true));
            DrawRowBackground(rowRect, item, index);

            // チェックボックス・サムネイル・テキストはすべて行の縦中央に揃える（手動rectで安定させる）
            var checkRect = new Rect(rowRect.x + 8f, rowRect.y + (c_RowHeight - 16f) * 0.5f, 16f, 16f);
            DrawItemSelectionToggle(item, checkRect);

            // 表示領域内の行だけサムネイルのダウンロードを要求する（遅延ロード）
            Texture2D thumbnail = null;
            if (IsRowVisible(rowRect))
            {
                thumbnail = GetThumbnail(item.r_Master.r_ThumbnailUrl);
            }
            var thumbRect = new Rect(checkRect.xMax + 6f, rowRect.y + (c_RowHeight - 46f) * 0.5f, 46f, 46f);
            DrawThumbnail(thumbRect, thumbnail);

            var textX = thumbRect.xMax + 8f;
            var textRect = new Rect(textX, rowRect.y, rowRect.xMax - 8f - textX, c_RowHeight);
            DrawItemRowText(textRect, item);

            HandleRowClick(rowRect, item);
        }

        // 行内のテキスト3行（アイテム名／ショップ・カテゴリ／状態）を縦中央に積む
        private void DrawItemRowText(Rect area, ItemRuntime item)
        {
            const float c_TitleHeight = 18f;
            const float c_LineHeight = 15f;
            const float c_Gap = 1f;
            var totalHeight = c_TitleHeight + c_LineHeight + c_LineHeight + c_Gap * 2f;
            var y = area.y + (area.height - totalHeight) * 0.5f;

            var titleRect = new Rect(area.x, y, area.width, c_TitleHeight);
            if (item.r_Master.r_Adult)
            {
                var nameWidth = styles.RowTitle.CalcSize(new GUIContent(item.r_Master.r_Name)).x;
                var badgeX = Mathf.Min(area.x + nameWidth + 4f, area.xMax - 30f);
                titleRect.width = Mathf.Max(0f, badgeX - area.x);
                GUI.Label(new Rect(badgeX, y + 1f, 28f, 15f), "R18", styles.Badge);
            }
            GUI.Label(titleRect, item.r_Master.r_Name, styles.RowTitle);
            y += c_TitleHeight + c_Gap;

            GUI.Label(new Rect(area.x, y, area.width, c_LineHeight), item.r_Master.r_ShopName + "  •  " + item.r_Master.r_SubCategoryName, styles.RowSub);
            y += c_LineHeight + c_Gap;

            using (new GuiColorScope(styles.StatusColor(item.PackageStatus)))
            {
                GUI.Label(new Rect(area.x, y, area.width, c_LineHeight), BuildStatusLabel(item), styles.RowSub);
            }
        }

        // 背景（ゼブラ・ホバー・フォーカス・区切り線）を描画する
        private void DrawRowBackground(Rect rowRect, ItemRuntime item, int index)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (index % 2 == 1)
            {
                EditorGUI.DrawRect(rowRect, styles.Zebra);
            }
            if (rowRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(rowRect, styles.Hover);
            }
            if (focusedItem == item)
            {
                EditorGUI.DrawRect(rowRect, styles.Focus);
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), styles.Accent);
            }
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), styles.Separator);
        }

        // シングルクリックで詳細表示、ダブルクリックで選択状態を切り替える（チェックボックス上は除く）
        private void HandleRowClick(Rect rowRect, ItemRuntime item)
        {
            var current = Event.current;
            var overCheckbox = current.mousePosition.x < rowRect.x + 30f;
            var clicked = current.type == EventType.MouseDown
                && current.button == 0
                && rowRect.Contains(current.mousePosition)
                && !overCheckbox;
            if (clicked)
            {
                focusedItem = item;
                if (current.clickCount == 2)
                {
                    ToggleItemSelection(item);
                }
                current.Use();
            }
        }

        // 行の矩形（コンテンツ座標）がスクロール表示範囲に入っているか判定する。
        // 行位置はRepaint時のみ確定するため、それ以外は読み込みを保留する。
        private bool IsRowVisible(Rect rowRect)
        {
            if (Event.current.type != EventType.Repaint || listViewport.height <= 0f)
            {
                return false;
            }
            const float c_PreloadMargin = 120f;
            var visibleTop = listScroll.y - c_PreloadMargin;
            var visibleBottom = listScroll.y + listViewport.height + c_PreloadMargin;
            var belowTop = rowRect.yMax >= visibleTop;
            var aboveBottom = rowRect.y <= visibleBottom;
            return belowTop && aboveBottom;
        }

        private static string BuildStatusLabel(ItemRuntime item)
        {
            var status = item.PackageStatus;
            if (status == ItemPackageStatus.NotDownloaded)
            {
                return "未ダウンロード";
            }
            if (status == ItemPackageStatus.NoUnityPackage)
            {
                return "unitypackage なし";
            }
            return "unitypackage " + item.UnityPackageCount + " 件";
        }

        // アイテム配下で選択中の unitypackage 数
        private int SelectedPackageCount(ItemRuntime item)
        {
            return item.ImportablePackageFiles(selectedPackageFiles).Count();
        }

        // ダブルクリックでの選択トグル。未ダウンロードはアイテム単位、それ以外は配下全パッケージ。
        private void ToggleItemSelection(ItemRuntime item)
        {
            if (!item.IsImportable)
            {
                SetPendingSelected(item.r_Master.r_Id, !selectedPendingItemIds.Contains(item.r_Master.r_Id));
                return;
            }
            var packages = item.UnityPackages.ToList();
            var allSelected = packages.Count > 0 && SelectedPackageCount(item) == packages.Count;
            SetPackagesSelected(packages, !allSelected);
        }

        // ---- スプリッタ（一覧幅の可変） ----

        // アイテム一覧と詳細ペインの境界。ドラッグで一覧の幅を変える
        private void DrawSplitter()
        {
            var rect = GUILayoutUtility.GetRect(c_SplitterWidth, c_SplitterWidth, GUILayout.Width(c_SplitterWidth), GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, styles.Separator);
            }
            HandleSplitterDrag(rect);
        }

        private void HandleSplitterDrag(Rect rect)
        {
            var current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                draggingSplitter = true;
                current.Use();
            }
            if (!draggingSplitter)
            {
                return;
            }
            if (current.type == EventType.MouseDrag)
            {
                // 一覧はウィンドウ左端から始まるため、マウスX座標がそのまま一覧幅になる
                listWidth = Mathf.Clamp(current.mousePosition.x, c_ListWidthMin, c_ListWidthMax);
                Repaint();
                current.Use();
            }
            if (current.type == EventType.MouseUp)
            {
                draggingSplitter = false;
                EditorPrefs.SetFloat(c_PrefListWidth, listWidth);
                current.Use();
            }
        }

        // ---- 詳細ペイン ----

        private void DrawDetailPane()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (focusedItem == null)
                {
                    EditorGUILayout.LabelField("左の一覧からアイテムを選択してください。");
                    return;
                }
                DrawDetailContent(focusedItem);
            }
        }

        // ヘッダは固定表示し、タグ・unitypackage・説明はそれぞれ固定枠内でスクロールさせる
        private void DrawDetailContent(ItemRuntime item)
        {
            DrawDetailHeader(item);

            if (item.r_Master.r_Tags.Count > 0)
            {
                DrawSeparator();
                GUILayout.Label("タグ", styles.SectionHeader);
                EditorGUILayout.Space(2);
                DrawTagSection(item.r_Master.r_Tags);
            }

            DrawSeparator();
            DrawPackageSection(item);

            if (!string.IsNullOrEmpty(item.r_Master.r_Description))
            {
                DrawSeparator();
                GUILayout.Label("説明", styles.SectionHeader);
                EditorGUILayout.Space(2);
                DrawDescriptionSection(item.r_Master.r_Description);
            }
        }

        // タグを固定高さの枠内でスクロール表示する。タグのクリックで検索の絞り込みに追加する
        private void DrawTagSection(IReadOnlyList<string> tags)
        {
            tagScroll = EditorGUILayout.BeginScrollView(tagScroll, GUILayout.Height(c_TagsHeight));
            var wrapWidth = position.width - listWidth - 52f;
            var clicked = DrawClickableTagFlow(tags, wrapWidth);
            if (clicked != null)
            {
                AddSelectedTag(clicked);
            }
            EditorGUILayout.EndScrollView();
        }

        // 説明は残りの高さいっぱいの枠内でスクロール表示する
        private void DrawDescriptionSection(string description)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                GUILayout.Label(description, styles.Description);
                EditorGUILayout.EndScrollView();
            }
        }

        // サムネイル＋基本情報を横並びのカードで表示する
        private void DrawDetailHeader(ItemRuntime item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var thumbnail = GetThumbnail(item.r_Master.r_ThumbnailUrl);
                var thumbRect = GUILayoutUtility.GetRect(168, 126, GUILayout.Width(168), GUILayout.Height(126));
                DrawThumbnail(thumbRect, thumbnail);

                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    // アイテム名をBOOTHページへのリンクにする
                    if (GUILayout.Button(item.r_Master.r_Name, styles.NameLink, GUILayout.ExpandWidth(true)))
                    {
                        Application.OpenURL(item.ItemPageUrl());
                    }
                    EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                    if (item.r_Master.r_Adult)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label("R-18", styles.Badge, GUILayout.Width(38), GUILayout.Height(16));
                            GUILayout.FlexibleSpace();
                        }
                    }

                    // アイテム名の下にオーダーページへのリンクを置く
                    DrawOrderButtons(item);

                    EditorGUILayout.Space(4);
                    DrawShopRow(item);
                    DrawMetaRow("カテゴリ", item.r_Master.r_ParentCategoryName + " / " + item.r_Master.r_SubCategoryName);
                    DrawMetaRow("商品ID", item.r_Master.r_Id.r_Value.ToString());
                    DrawStatusRow(item);

                    // アイテム状態の下にフォルダを開くボタンを置く
                    if (item.r_FolderExists)
                    {
                        EditorGUILayout.Space(2);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("フォルダを開く", GUILayout.Height(20), GUILayout.Width(120)))
                            {
                                EditorUtility.OpenWithDefaultApp(item.r_FolderPath);
                            }
                            GUILayout.FlexibleSpace();
                        }
                    }
                }
            }
        }

        private void DrawShopRow(ItemRuntime item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("ショップ", styles.MetaKey);
                var hasShopPage = item.r_Master.r_ShopId.IsValid;
                if (hasShopPage)
                {
                    if (GUILayout.Button(item.r_Master.r_ShopName, styles.Link))
                    {
                        Application.OpenURL(item.ShopPageUrl());
                    }
                    EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                }
                else
                {
                    GUILayout.Label(item.r_Master.r_ShopName, styles.DetailMeta);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawMetaRow(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(key, styles.MetaKey);
                GUILayout.Label(value, styles.DetailMeta);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawStatusRow(ItemRuntime item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("状態", styles.MetaKey);
                using (new GuiColorScope(styles.StatusColor(item.PackageStatus)))
                {
                    GUILayout.Label(BuildStatusLabel(item), styles.DetailMeta);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawPackageSection(ItemRuntime item)
        {
            var packages = item.UnityPackages.ToList();
            var header = "unitypackage";
            if (item.r_FolderExists && packages.Count > 0)
            {
                header = "unitypackage（" + packages.Count + " 件）";
            }
            GUILayout.Label(header, styles.SectionHeader);
            EditorGUILayout.Space(2);

            if (!item.r_FolderExists)
            {
                EditorGUILayout.HelpBox("このアイテムはまだダウンロードされていません。", MessageType.None);
                return;
            }
            if (packages.Count == 0)
            {
                EditorGUILayout.HelpBox("インポート可能な unitypackage がありません。", MessageType.None);
                return;
            }

            DrawPackageToolbar(packages);
            packageScroll = EditorGUILayout.BeginScrollView(packageScroll, GUILayout.Height(c_PackageListHeight));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (var i = 0; i < packages.Count; i += 1)
                {
                    DrawPackageRow(packages[i], i);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // すべて選択 ＋ 選択分の一括インポート
        private void DrawPackageToolbar(List<ItemFile> packages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var allSelected = packages.All(file => selectedPackageFiles.Contains(file));
                var toggled = GUILayout.Toggle(allSelected, GUIContent.none, GUILayout.Width(16));
                if (toggled != allSelected)
                {
                    SetPackagesSelected(packages, toggled);
                }
                GUILayout.Label("すべて選択", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                var selectedCount = packages.Count(file => selectedPackageFiles.Contains(file));
                using (new EditorGUI.DisabledScope(selectedCount == 0 || SequentialPackageImporter.IsRunning))
                {
                    if (GUILayout.Button("選択した " + selectedCount + " 件をインポート", GUILayout.Height(20), GUILayout.Width(190)))
                    {
                        ImportSelectedPackages(packages);
                    }
                }
            }
        }

        // 1行=1パッケージ。チェックボックス／2段ファイル名／単体インポートを縦中央で揃える
        private void DrawPackageRow(ItemFile file, int index)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (index % 2 == 1)
                {
                    EditorGUI.DrawRect(rowRect, styles.Zebra);
                }
                if (rowRect.Contains(Event.current.mousePosition))
                {
                    EditorGUI.DrawRect(rowRect, styles.Hover);
                }
            }

            var checkRect = new Rect(rowRect.x + 6f, rowRect.y + (34f - 16f) * 0.5f, 16f, 16f);
            var isSelected = selectedPackageFiles.Contains(file);
            EditorGUI.BeginChangeCheck();
            var toggled = EditorGUI.Toggle(checkRect, isSelected);
            if (EditorGUI.EndChangeCheck())
            {
                SetPackageSelected(file, toggled);
            }

            var iconRect = new Rect(checkRect.xMax + 4f, rowRect.y + (34f - 18f) * 0.5f, 20f, 18f);
            GUI.Label(iconRect, "📦");

            const float c_ButtonWidth = 84f;
            var buttonRect = new Rect(rowRect.xMax - 6f - c_ButtonWidth, rowRect.y + (34f - 22f) * 0.5f, c_ButtonWidth, 22f);
            using (new EditorGUI.DisabledScope(SequentialPackageImporter.IsRunning))
            {
                if (GUI.Button(buttonRect, "インポート"))
                {
                    ImportPackage(file.r_FullPath);
                }
            }

            var textX = iconRect.xMax + 4f;
            var textRect = new Rect(textX, rowRect.y, Mathf.Max(0f, buttonRect.x - 6f - textX), 34f);
            DrawPackageNameText(textRect, file);

            HandlePackageRowClick(rowRect, file);
        }

        // パッケージ行のファイル名（とフォルダ）を縦中央に積む
        private void DrawPackageNameText(Rect area, ItemFile file)
        {
            const float c_NameHeight = 16f;
            const float c_DirHeight = 13f;
            var directory = Path.GetDirectoryName(file.r_RelativePath);
            var hasDirectory = !string.IsNullOrEmpty(directory);
            var totalHeight = hasDirectory ? c_NameHeight + c_DirHeight : c_NameHeight;
            var y = area.y + (area.height - totalHeight) * 0.5f;
            GUI.Label(new Rect(area.x, y, area.width, c_NameHeight), Path.GetFileName(file.r_RelativePath), styles.PackageName);
            if (hasDirectory)
            {
                GUI.Label(new Rect(area.x, y + c_NameHeight, area.width, c_DirHeight), directory, styles.RowSub);
            }
        }

        // ダブルクリックで選択トグル（チェックボックス・インポートボタン上は除外）
        private void HandlePackageRowClick(Rect rowRect, ItemFile file)
        {
            var current = Event.current;
            var overCheckbox = current.mousePosition.x < rowRect.x + 28f;
            var overButton = current.mousePosition.x > rowRect.xMax - 96f;
            var doubleClicked = current.type == EventType.MouseDown
                && current.button == 0
                && current.clickCount == 2
                && rowRect.Contains(current.mousePosition)
                && !overCheckbox
                && !overButton;
            if (doubleClicked)
            {
                SetPackageSelected(file, !selectedPackageFiles.Contains(file));
                current.Use();
            }
        }

        private void SetPackageSelected(ItemFile file, bool selected)
        {
            if (selected)
            {
                selectedPackageFiles.Add(file);
            }
            else
            {
                selectedPackageFiles.Remove(file);
            }
        }

        private void SetPackagesSelected(List<ItemFile> packages, bool selected)
        {
            foreach (var file in packages)
            {
                SetPackageSelected(file, selected);
            }
        }

        private void ImportSelectedPackages(List<ItemFile> packages)
        {
            if (SequentialPackageImporter.IsRunning)
            {
                EditorUtility.DisplayDialog("BLMImporter", "インポート処理中のため、新しいインポートは開始できません。", "OK");
                return;
            }

            var targets = packages.Where(file => selectedPackageFiles.Contains(file)).ToList();
            var requests = targets
                .Select(file => new PackageImportRequest { m_PackagePath = file.r_FullPath })
                .ToList();
            ImportProgressWindow.ClearTargets();
            SequentialPackageImporter.Run(requests, BuildImportOptions());
            ImportProgressWindow.OpenIfActive();
            // 取り込み開始した分は選択を外す
            SetPackagesSelected(targets, false);
        }

        // タグを折り返しレイアウトのボタンとして並べる。クリックされたタグを返す（無ければnull）
        private string DrawClickableTagFlow(IReadOnlyList<string> tags, float wrapWidth)
        {
            if (wrapWidth < 120f)
            {
                wrapWidth = 120f;
            }
            const float c_Spacing = 4f;
            var offsets = new List<Vector2>();
            var sizes = new List<Vector2>();
            var x = 0f;
            var y = 0f;
            var lineHeight = 0f;
            foreach (var tag in tags)
            {
                var size = styles.TagChip.CalcSize(new GUIContent(tag));
                if (x + size.x > wrapWidth && x > 0f)
                {
                    x = 0f;
                    y += lineHeight + c_Spacing;
                    lineHeight = 0f;
                }
                offsets.Add(new Vector2(x, y));
                sizes.Add(size);
                x += size.x + c_Spacing;
                if (size.y > lineHeight)
                {
                    lineHeight = size.y;
                }
            }

            var totalHeight = y + lineHeight;
            var area = GUILayoutUtility.GetRect(wrapWidth, totalHeight);
            var clicked = (string)null;
            for (var i = 0; i < tags.Count; i += 1)
            {
                var chipRect = new Rect(area.x + offsets[i].x, area.y + offsets[i].y, sizes[i].x, sizes[i].y);
                EditorGUIUtility.AddCursorRect(chipRect, MouseCursor.Link);
                if (GUI.Button(chipRect, tags[i], styles.TagChip))
                {
                    clicked = tags[i];
                }
            }
            return clicked;
        }

        private void DrawOrderButtons(ItemRuntime item)
        {
            if (item.r_Master.r_OrderIds.Count == 0)
            {
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var singleOrder = item.r_Master.r_OrderIds.Count == 1;
                foreach (var orderId in item.r_Master.r_OrderIds)
                {
                    var label = "オーダー " + orderId.r_Value;
                    if (singleOrder)
                    {
                        label = "オーダーページを開く";
                    }
                    if (GUILayout.Button(label, GUILayout.Height(22), GUILayout.Width(160)))
                    {
                        Application.OpenURL(orderId.OrderPageUrl());
                    }
                }
                // オーダーページを拡張の自動ダウンロード用URL（targets＋ライブラリパス）で開く
                DrawDownloadButton(item);
                GUILayout.FlexibleSpace();
            }
        }

        // オーダーページに BLMImporterDLtargets（itemid/variationid の配列）を付けて開く赤いボタン
        private void DrawDownloadButton(ItemRuntime item)
        {
            var orderId = item.r_Master.r_OrderIds[0];
            var previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
            // ダウンロード中は他のダウンロードをロックする
            using (new EditorGUI.DisabledScope(BLMDownloadServer.IsDownloading))
            {
                if (GUILayout.Button("ダウンロード", GUILayout.Height(22), GUILayout.Width(110)))
                {
                    var server = BLMDownloadServer.BeginDownload();
                    Application.OpenURL(item.DownloadUrl(orderId, server.port, server.token));
                }
            }
            GUI.backgroundColor = previousBackground;
        }

        // ---- フッター（一括インポート） ----

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var toggleSlot = GUILayoutUtility.GetRect(210f, 24f, GUILayout.Width(210f));
                var toggleRect = new Rect(toggleSlot.x, toggleSlot.y + (toggleSlot.height - 18f) * 0.5f, 210f, 18f);
                EditorGUI.BeginChangeCheck();
                interactiveImport = EditorGUI.ToggleLeft(toggleRect, "個別のインポートダイアログを表示", interactiveImport);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(c_PrefInteractive, interactiveImport);
                }
                GUILayout.FlexibleSpace();

                var hasSelection = selectedPackageFiles.Count > 0 || selectedPendingItemIds.Count > 0;
                using (new EditorGUI.DisabledScope(!hasSelection || SequentialPackageImporter.IsRunning))
                {
                    if (GUILayout.Button("選択した unitypackage をインポート", GUILayout.Width(220), GUILayout.Height(24)))
                    {
                        ImportSelectedPackages();
                    }
                }
            }
        }

        // チェックの入ったアイテムをインポートする。
        // ダウンロード済みと未ダウンロードをまとめてインポートダイアログへ渡す。
        // ダイアログ側で完了を待ち、「インポート開始」で取り込む。
        private void ImportSelectedPackages()
        {
            if (SequentialPackageImporter.IsRunning)
            {
                EditorUtility.DisplayDialog("BLMImporter", "インポート処理中のため、新しいインポートは開始できません。", "OK");
                return;
            }

            var plan = PackageImporter.BuildPlan(snapshot.r_Items.Values, selectedPackageFiles);
            var readyPaths = plan.r_Packages.Select(package => package.m_PackagePath).ToList();
            var pendingIds = selectedPendingItemIds.Select(itemId => itemId.r_Value).ToList();
            if (readyPaths.Count == 0 && pendingIds.Count == 0)
            {
                EditorUtility.DisplayDialog("BLMImporter", "インポート対象が選択されていません。", "OK");
                return;
            }

            ImportProgressWindow.Open(pendingIds, readyPaths, interactiveImport);
            // 取り込みへ渡したので本体側の選択は解除する
            ClearImportSelection();
        }

        // インポートへ渡した選択（unitypackage・未DL）を解除する
        private void ClearImportSelection()
        {
            selectedPackageFiles.Clear();
            selectedPendingItemIds.Clear();
        }

        private PackageImportOptions BuildImportOptions()
        {
            return new PackageImportOptions
            {
                m_Interactive = interactiveImport
            };
        }

        // 1件のインポート要求を直列インポータに渡す（インポートダイアログのスキップを防ぐ）
        private void ImportPackage(string packagePath)
        {
            if (SequentialPackageImporter.IsRunning)
            {
                EditorUtility.DisplayDialog("BLMImporter", "インポート処理中のため、新しいインポートは開始できません。", "OK");
                return;
            }

            var request = new PackageImportRequest { m_PackagePath = packagePath };
            ImportProgressWindow.ClearTargets();
            SequentialPackageImporter.Run(new[] { request }, BuildImportOptions());
            ImportProgressWindow.OpenIfActive();
        }
    }
}
