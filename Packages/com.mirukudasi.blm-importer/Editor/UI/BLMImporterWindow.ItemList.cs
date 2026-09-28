using System;
using System.Collections.Generic;
using System.Linq;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    public partial class BLMImporterWindow
    {
        // ---- アイテム一覧 ----

        private void DrawItemList(List<ItemRuntime> filtered)
        {
            using (var column = new EditorGUILayout.VerticalScope(GUILayout.Width(settings.ListWidth)))
            {
                // 表示領域の高さはRepaint時のみ確定するため、その時だけ控える
                if (Event.current.type == EventType.Repaint)
                {
                    listViewport = column.rect;
                }

                var filterKey = $"{searchText}|{categoryFilterIndex}|{listFilterIndex}|{settings.ShowAdult}|{settings.PageSize}|{settings.TagMatchMode}|{settings.SortMode}|{settings.SortDescending}|{string.Join(",", selectedTags)}";
                if (filterKey != lastFilterKey)
                {
                    currentPage = 0;
                    listScroll = Vector2.zero;
                    lastFilterKey = filterKey;
                }
                var pageCount = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)settings.PageSize));
                currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
                var pageItems = filtered.Skip(currentPage * settings.PageSize).Take(settings.PageSize).ToList();

                DrawSelectAllHeader(filtered);

                using (var scroll = new EditorGUILayout.ScrollViewScope(listScroll))
                {
                    listScroll = scroll.scrollPosition;
                    for (var i = 0; i < pageItems.Count; i += 1)
                    {
                        DrawItemRow(pageItems[i], i);
                    }
                    if (filtered.Count == 0)
                    {
                        EditorGUILayout.Space(12);
                        EditorGUILayout.LabelField("該当するアイテムがありません。", EditorStyles.centeredGreyMiniLabel);
                    }
                }

                DrawPagination(pageCount);
            }
        }

        // 一覧上部の「全選択」チェックボックス。ダウンロード済はunitypackage、未ダウンロードはアイテム単位で対象にする
        private void DrawSelectAllHeader(List<ItemRuntime> filtered)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(6);
                var (total, selected) = selection.CountForSelectAll(filtered);
                DrawTriStateToggle(ReserveCenteredToggleRect(18f), total, selected, value => selection.SetAll(filtered, value));

                GUILayout.Label("全選択", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                var selectedItemCount = filtered.Count(selection.IsItemSelected);
                GUILayout.Label($"{filtered.Count} 件中 {selectedItemCount} 選択", EditorStyles.miniLabel);
            }
        }

        // アイテム1件のチェックボックス。
        // 未ダウンロードはアイテム単位の選択（ダウンロード待ち）、それ以外は配下 unitypackage のtri-state。
        private void DrawItemSelectionToggle(ItemRuntime item, Rect rect)
        {
            // unitypackageを持たない（未ダウンロード／package無し）アイテムはアイテム単位で選択し、ダウンロード待ちにする
            if (!item.IsImportable)
            {
                var isPending = selection.ContainsPending(item.Id);
                EditorGUI.BeginChangeCheck();
                var toggled = EditorGUI.Toggle(rect, isPending);
                if (EditorGUI.EndChangeCheck())
                {
                    selection.SetPending(item.Id, toggled);
                }
                return;
            }
            var packages = item.UnityPackages.ToList();
            var selectedCount = packages.Count(selection.Contains);
            DrawTriStateToggle(rect, packages.Count, selectedCount, value => selection.SetPackages(packages, value));
        }

        // 全選択(✓)／一部選択(-)／未選択 を表すチェックボックス。total が0なら無効表示にする。
        // クリック時は onChanged(true=全選択 / false=全解除) を呼ぶ
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
            return CenterVertically(slot.x, slot.y, slot.height, 16f, 16f);
        }

        // ツールバー行内でミニラベルを縦中央に置く（内容幅で確保）
        private void MiddleMiniLabel(string text)
        {
            var rowHeight = PositiveOr(EditorStyles.toolbar.fixedHeight, 21f);
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
                MiddleMiniLabel($"{currentPage + 1} / {pageCount} ページ");
                GUILayout.Space(8);
                MiddleMiniLabel("表示数");
                settings.PageSize = EditorGUILayout.IntPopup(settings.PageSize, PageSizeLabels, PageSizeChoices, EditorStyles.toolbarPopup, GUILayout.Width(56));
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
            var checkRect = CenterVertically(rowRect.x + 8f, rowRect.y, c_RowHeight, 16f, 16f);
            DrawItemSelectionToggle(item, checkRect);

            var thumbnail = ThumbnailIfVisible(rowRect, item);
            var thumbRect = CenterVertically(checkRect.xMax + 6f, rowRect.y, c_RowHeight, 46f, 46f);
            BLMGuiDraw.Thumbnail(thumbRect, thumbnail, styles);

            var textX = thumbRect.xMax + 8f;
            var textRect = new Rect(textX, rowRect.y, rowRect.xMax - 8f - textX, c_RowHeight);
            DrawItemRowText(textRect, item);

            HandleRowClick(rowRect, item);
        }

        // 表示領域内の行だけサムネイルのダウンロードを要求する（遅延ロード）
        private Texture2D ThumbnailIfVisible(Rect rowRect, ItemRuntime item)
        {
            if (IsRowVisible(rowRect))
            {
                return GetThumbnail(item.r_Master.r_ThumbnailUrl);
            }
            return null;
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

            GUI.Label(new Rect(area.x, y, area.width, c_LineHeight), BLMGuiDraw.ShopAndCategory(item.r_Master), styles.RowSub);
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
            DrawStripedRowBackground(rowRect, index);
            if (focusedItem == item)
            {
                EditorGUI.DrawRect(rowRect, styles.Focus);
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), styles.Accent);
            }
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), styles.Separator);
        }

        // 奇数行の縞模様と、マウスを重ねた行の強調を描く
        private void DrawStripedRowBackground(Rect rowRect, int index)
        {
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
        }

        // シングルクリックで詳細表示、ダブルクリックで選択状態を切り替える（チェックボックス上は除く）
        private void HandleRowClick(Rect rowRect, ItemRuntime item)
        {
            var current = Event.current;
            var overCheckbox = current.mousePosition.x < rowRect.x + c_RowCheckboxHitWidth;
            var isLeftMouseDown = current.type == EventType.MouseDown && current.button == 0;
            var isInsideRow = rowRect.Contains(current.mousePosition);
            var clicked = isLeftMouseDown && isInsideRow && !overCheckbox;
            if (clicked)
            {
                focusedItem = item;
                if (current.clickCount == 2)
                {
                    selection.Toggle(item);
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
            return $"unitypackage {item.UnityPackageCount} 件";
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
                settings.ListWidth = Mathf.Clamp(current.mousePosition.x, c_ListWidthMin, c_ListWidthMax);
                Repaint();
                current.Use();
            }
            if (current.type == EventType.MouseUp)
            {
                draggingSplitter = false;
                settings.SaveListWidth();
                current.Use();
            }
        }
    }
}
