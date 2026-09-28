using System.Collections.Generic;
using System.Linq;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    public partial class BLMImporterWindow
    {
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
                    GUILayout.Label($"キャッシュ更新中: 残り {thumbnails.RemainingCount} 件", EditorStyles.miniLabel);
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

                settings.SortMode = (ItemSortMode)EditorGUILayout.Popup((int)settings.SortMode, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(110));
                DrawSortDirectionToggle();

                settings.ShowAdult = GUILayout.Toggle(settings.ShowAdult, "R-18表示", EditorStyles.toolbarButton, GUILayout.Width(70));
                GUILayout.FlexibleSpace();

                visibleItems = ComputeVisibleItems();
                var selectionLabel = $"選択 {selection.PackageCount} package";
                if (selection.PendingCount > 0)
                {
                    selectionLabel += $" / 未DL {selection.PendingCount}";
                }
                GUILayout.Label($"{visibleItems.Count} / {snapshot.r_Items.Count} 件   {selectionLabel}", EditorStyles.miniLabel);
            }

            // 検索窓の直下に、選択中タグ（×付き）→ タグ候補 の順で表示する
            DrawSelectedTagChips();
            DrawTagSuggestions();
            return visibleItems;
        }

        // 現在の絞り込み条件と並び順を適用したアイテム一覧を作る
        private List<ItemRuntime> ComputeVisibleItems()
        {
            return ItemSorter.Sort(BuildFilter().Apply(snapshot.r_Items.Values), settings.SortMode, settings.SortDescending);
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
                    ClearSearch();
                }
                return;
            }
            // キャンセルボタンスタイルは左マージンが負で、フィールド右端に重なって表示される
            if (GUILayout.Button(GUIContent.none, cancelStyle))
            {
                ClearSearch();
            }
        }

        // 検索語を消したあとも入力中のままだと古い文字が残って見えるため、検索窓からフォーカスも外す
        private void ClearSearch()
        {
            searchText = "";
            GUI.FocusControl(null);
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
            var rowRect = ReserveChipRow(needsScroll);
            // 見出し・操作群・チップはチップ帯（上端 c_ChipBand）の縦中央に揃える。スクロールバーは帯の下に出る
            var bandCenterY = rowRect.y + c_ChipBand * 0.5f;

            const float c_ClearWidth = 72f;
            var clearRect = new Rect(rowRect.xMax - 8f - c_ClearWidth, bandCenterY - 9f, c_ClearWidth, 18f);
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
            var contentWidth = ChipsContentWidth(tags, withClose);
            var needsScroll = contentWidth > area.width;
            // スクロールバーが出るぶん、チップを並べる帯はその上側に確保する
            var band = area.height - ScrollbarAllowance(needsScroll);
            var viewRect = new Rect(0f, 0f, contentWidth, band);
            string clicked = null;
            var tooltip = "クリックで追加";
            if (withClose)
            {
                tooltip = "クリックで解除";
            }
            using (var view = new GUI.ScrollViewScope(area, scroll, viewRect, false, false))
            {
                scroll = view.scrollPosition;
                var x = 0f;
                foreach (var tag in tags)
                {
                    var content = new GUIContent(ChipText(tag, withClose), tooltip);
                    var size = styles.TagChip.CalcSize(content);
                    var chipRect = new Rect(x, (band - size.y) * 0.5f, size.x, size.y);
                    EditorGUIUtility.AddCursorRect(chipRect, MouseCursor.Link);
                    if (GUI.Button(chipRect, content, styles.TagChip))
                    {
                        clicked = tag;
                    }
                    x += size.x + c_ChipSpacing;
                }
            }
            return clicked;
        }

        // チップ群を1列に並べたときの合計幅（スクロール要否・配置に使う）
        private float ChipsContentWidth(IReadOnlyList<string> tags, bool withClose)
        {
            var total = 0f;
            foreach (var tag in tags)
            {
                total += styles.TagChip.CalcSize(new GUIContent(ChipText(tag, withClose))).x + c_ChipSpacing;
            }
            return total;
        }

        // チップに表示する文字。解除用のチップには末尾に解除の印を付ける
        private static string ChipText(string tag, bool withClose)
        {
            var text = tag;
            if (withClose)
            {
                text = $"{tag}  ✕";
            }
            return text;
        }

        // タグのチップを並べる1行ぶんの領域を確保し、枠を描く。横スクロールバーが要るときはその高さも足す
        private Rect ReserveChipRow(bool needsScroll)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, c_ChipBand + ScrollbarAllowance(needsScroll), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }
            return rowRect;
        }

        private static float ScrollbarHeight()
        {
            var height = GUI.skin.horizontalScrollbar.fixedHeight;
            return PositiveOr(height, 15f);
        }

        // タグの一致方法（AND/OR）と「一致」ラベルを右端 rightX から左へ縦中央で置く。
        private void DrawTagMatchMode(float rightX, float centerY)
        {
            var isAll = settings.TagMatchMode == TagMatchMode.AND;
            var y = centerY - 9f;
            const float c_SegWidth = 42f;
            var orRect = new Rect(rightX - c_SegWidth, y, c_SegWidth, 18f);
            var andRect = new Rect(orRect.x - c_SegWidth, y, c_SegWidth, 18f);
            var pickAll = GUI.Toggle(andRect, isAll, new GUIContent("AND", "すべてのタグを含む"), EditorStyles.miniButtonLeft);
            var pickAny = GUI.Toggle(orRect, !isAll, new GUIContent("OR", "いずれかのタグを含む"), EditorStyles.miniButtonRight);

            var next = settings.TagMatchMode;
            if (pickAll && !isAll)
            {
                next = TagMatchMode.AND;
            }
            if (pickAny && isAll)
            {
                next = TagMatchMode.OR;
            }
            settings.TagMatchMode = next;
            const float c_LabelWidth = 32f;
            var labelRect = new Rect(andRect.x - c_LabelWidth, centerY - 9f, c_LabelWidth, 18f);
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
            var rowRect = ReserveChipRow(needsScroll);
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
            titles.AddRange(BuildListChoices().Select(choice => choice.Title));
            return titles.ToArray();
        }

        // 絞り込みに使えるリストを、表示名と組にして通常リスト→スマートリストの順で並べる
        private List<(string Title, IItemList List)> BuildListChoices()
        {
            var choices = new List<(string Title, IItemList List)>();
            choices.AddRange(snapshot.r_Lists.Values.Select(list => ($"★ {list.r_Master.r_Title}", (IItemList)list)));
            choices.AddRange(snapshot.r_SmartLists.Values.Select(smartList => ($"🔍 {smartList.r_Master.r_Title}", (IItemList)smartList)));
            return choices;
        }

        // フィルタ用のリスト一覧。通常リスト→スマートリストの順（BuildListTitles の並びと揃える）
        private List<IItemList> AllFilterLists()
        {
            return BuildListChoices().Select(choice => choice.List).ToList();
        }

        // 昇順／降順を切り替えるボタン（▲=昇順 / ▼=降順）
        private void DrawSortDirectionToggle()
        {
            var arrow = "▲";
            var tooltip = "昇順（クリックで降順）";
            if (settings.SortDescending)
            {
                arrow = "▼";
                tooltip = "降順（クリックで昇順）";
            }
            if (GUILayout.Button(new GUIContent(arrow, tooltip), EditorStyles.toolbarButton, GUILayout.Width(26)))
            {
                settings.SortDescending = !settings.SortDescending;
            }
        }

        // UIの選択状態をCore側のフィルタ条件へ詰め替える
        private ItemFilter BuildFilter()
        {
            var filter = new ItemFilter {
                m_Keyword = searchText,
                m_IncludeAdult = settings.ShowAdult
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
            filter.m_TagMatchMode = settings.TagMatchMode;
            return filter;
        }
    }
}
