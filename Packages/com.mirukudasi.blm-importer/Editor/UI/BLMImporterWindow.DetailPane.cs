using System.Collections.Generic;
using System.IO;
using System.Linq;
using BLMImporter.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace BLMImporter.Editor
{
    public partial class BLMImporterWindow
    {
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
                DrawSectionHeader("タグ");
                DrawTagSection(item.r_Master.r_Tags);
            }

            DrawSeparator();
            DrawPackageSection(item);

            if (!string.IsNullOrEmpty(item.r_Master.r_Description))
            {
                DrawSectionHeader("説明");
                DrawDescriptionSection(item.r_Master.r_Description);
            }
        }

        // タグを固定高さの枠内でスクロール表示する。タグのクリックで検索の絞り込みに追加する
        private void DrawTagSection(IReadOnlyList<string> tags)
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(tagScroll, GUILayout.Height(c_TagsHeight)))
            {
                tagScroll = scroll.scrollPosition;
                var wrapWidth = position.width - settings.ListWidth - 52f;
                var clicked = DrawClickableTagFlow(tags, wrapWidth);
                if (clicked != null)
                {
                    AddSelectedTag(clicked);
                }
            }
        }

        // 説明は残りの高さいっぱいの枠内でスクロール表示する
        private void DrawDescriptionSection(string description)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll))
                {
                    detailScroll = scroll.scrollPosition;
                    GUILayout.Label(description, styles.Description);
                }
            }
        }

        // サムネイル＋基本情報を横並びのカードで表示する
        private void DrawDetailHeader(ItemRuntime item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var thumbnail = GetThumbnail(item.r_Master.r_ThumbnailUrl);
                var thumbRect = GUILayoutUtility.GetRect(168, 126, GUILayout.Width(168), GUILayout.Height(126));
                BLMGuiDraw.Thumbnail(thumbRect, thumbnail, styles);

                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    // アイテム名をBOOTHページへのリンクにする
                    LinkButton(item.r_Master.r_Name, styles.NameLink, () => Application.OpenURL(item.ItemPageUrl()), GUILayout.ExpandWidth(true));
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
                    DrawMetaRow("カテゴリ", $"{item.r_Master.r_ParentCategoryName} / {item.r_Master.r_SubCategoryName}");
                    DrawMetaRow("商品ID", item.Id.r_Value.ToString());
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
                    LinkButton(item.r_Master.r_ShopName, styles.Link, () => Application.OpenURL(item.ShopPageUrl()));
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
                header = $"unitypackage（{packages.Count} 件）";
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
            // プロジェクトに入っている版との新旧はアイテム内の unitypackage を見比べて決まるため、
            // 一覧全体をまとめて判定してから各行へ結果を渡す
            var itemPackagePaths = packages.Select(package => package.r_FullPath).ToList();
            var packageActions = ImportedPackageIndex.GetActions(itemPackagePaths);
            using (var scroll = new EditorGUILayout.ScrollViewScope(packageScroll, GUILayout.Height(c_PackageListHeight)))
            {
                packageScroll = scroll.scrollPosition;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (var i = 0; i < packages.Count; i += 1)
                    {
                        DrawPackageRow(packages[i], i, packageActions[packages[i].r_FullPath]);
                    }
                }
            }
        }

        // すべて選択 ＋ 選択分の一括インポート
        private void DrawPackageToolbar(List<ItemFile> packages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var allSelected = packages.All(file => selection.Contains(file));
                var toggled = GUILayout.Toggle(allSelected, GUIContent.none, GUILayout.Width(16));
                if (toggled != allSelected)
                {
                    selection.SetPackages(packages, toggled);
                }
                GUILayout.Label("すべて選択", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                var selectedCount = packages.Count(file => selection.Contains(file));
                using (new EditorGUI.DisabledScope(selectedCount == 0 || SequentialPackageImporter.IsRunning))
                {
                    if (GUILayout.Button($"選択した {selectedCount} 件をインポート", GUILayout.Height(20), GUILayout.Width(190)))
                    {
                        ImportCheckedPackagesOfItem(packages);
                    }
                }
            }
        }

        // 1行=1パッケージ。チェックボックス／2段ファイル名／単体インポートを縦中央で揃える
        private void DrawPackageRow(ItemFile file, int index, PackageImportAction importAction)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, c_PackageRowHeight, GUILayout.ExpandWidth(true));
            DrawStripedRowBackground(rowRect, index);

            var checkRect = CenterVertically(rowRect.x + 6f, rowRect.y, c_PackageRowHeight, 16f, 16f);
            var isSelected = selection.Contains(file);
            EditorGUI.BeginChangeCheck();
            var toggled = EditorGUI.Toggle(checkRect, isSelected);
            if (EditorGUI.EndChangeCheck())
            {
                selection.SetPackage(file, toggled);
            }

            var iconRect = CenterVertically(checkRect.xMax + 4f, rowRect.y, c_PackageRowHeight, 20f, 18f);
            GUI.Label(iconRect, "📦");

            // 分割ボタンの主ボタンに「再インポート」が収まる幅にする
            const float c_ButtonWidth = 110f;
            var buttonRect = CenterVertically(rowRect.xMax - 6f - c_ButtonWidth, rowRect.y, c_PackageRowHeight, c_ButtonWidth, 22f);
            DrawPackageActionButton(buttonRect, file, importAction);

            var textX = iconRect.xMax + 4f;
            var textRect = new Rect(textX, rowRect.y, Mathf.Max(0f, buttonRect.x - 6f - textX), c_PackageRowHeight);
            DrawPackageNameText(textRect, file);

            HandlePackageRowClick(rowRect, file);
        }

        // プロジェクトに入っている版と比べた新旧でボタンを切り替える。
        // 入っている版そのものは「開く」を主にし、それより古い版は取り込み直しを主にする。
        // 新しい版、まだ取り込んでいないもの、判定が終わっていないものは「インポート」だけを出し、ユーザーを待たせない。
        private void DrawPackageActionButton(Rect buttonRect, ItemFile file, PackageImportAction importAction)
        {
            if (importAction == PackageImportAction.Open)
            {
                DrawSplitButton(buttonRect, "開く", false, () => RevealImportedFolder(file), "再インポート", true, () => ImportPackage(file.r_FullPath));
                return;
            }
            if (importAction == PackageImportAction.Reimport)
            {
                DrawSplitButton(buttonRect, "再インポート", true, () => ImportPackage(file.r_FullPath), "開く", false, () => RevealImportedFolder(file));
                return;
            }
            using (new EditorGUI.DisabledScope(SequentialPackageImporter.IsRunning))
            {
                if (GUI.Button(buttonRect, "インポート"))
                {
                    ImportPackage(file.r_FullPath);
                }
            }
        }

        // 主となる操作のボタンと、右端の下矢印から選べるもう1つの操作を並べる。
        // インポート中は取り込みの操作だけを押せなくし、ボタン全体の大きさは単独のボタンと同じに保つ。
        private void DrawSplitButton(Rect buttonRect, string mainLabel, bool isMainImport, System.Action mainAction, string menuLabel, bool isMenuImport, System.Action menuAction)
        {
            const float c_ArrowWidth = 20f;
            var mainRect = new Rect(buttonRect.x, buttonRect.y, buttonRect.width - c_ArrowWidth, buttonRect.height);
            var arrowRect = new Rect(mainRect.xMax, buttonRect.y, c_ArrowWidth, buttonRect.height);
            using (new EditorGUI.DisabledScope(isMainImport && SequentialPackageImporter.IsRunning))
            {
                if (GUI.Button(mainRect, mainLabel, styles.SplitButtonMain))
                {
                    mainAction();
                }
            }
            if (GUI.Button(arrowRect, "▼", styles.SplitButtonArrow))
            {
                ShowPackageActionMenu(arrowRect, menuLabel, isMenuImport, menuAction);
            }
        }

        // 下矢印から開くメニュー。インポート中は取り込みの操作を選べなくする
        private void ShowPackageActionMenu(Rect anchorRect, string label, bool isImport, System.Action action)
        {
            var menu = new GenericMenu();
            var content = new GUIContent(label);
            if (isImport && SequentialPackageImporter.IsRunning)
            {
                menu.AddDisabledItem(content);
            }
            else
            {
                menu.AddItem(content, false, () => action());
            }
            menu.DropDown(anchorRect);
        }

        // 取り込み先のルートフォルダを Project ウィンドウで選択してハイライトする
        private void RevealImportedFolder(ItemFile file)
        {
            var rootAssetPath = ImportedPackageIndex.GetRootAssetPath(file.r_FullPath);
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(rootAssetPath);
            if (folder == null)
            {
                EditorUtility.DisplayDialog(BLMDialogs.c_Title, "取り込み先のフォルダが見つかりませんでした。", "OK");
                return;
            }
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        // パッケージ行のファイル名（とフォルダ）を縦中央に積む
        private void DrawPackageNameText(Rect area, ItemFile file)
        {
            const float c_NameHeight = 16f;
            const float c_DirHeight = 13f;
            var directory = Path.GetDirectoryName(file.r_RelativePath);
            var hasDirectory = !string.IsNullOrEmpty(directory);
            var totalHeight = c_NameHeight;
            if (hasDirectory)
            {
                totalHeight += c_DirHeight;
            }
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
            var overCheckbox = current.mousePosition.x < rowRect.x + c_PackageCheckboxHitWidth;
            var overButton = current.mousePosition.x > rowRect.xMax - c_PackageButtonHitWidth;
            var isLeftDoubleClick = current.type == EventType.MouseDown && current.button == 0 && current.clickCount == 2;
            var isInsideRow = rowRect.Contains(current.mousePosition);
            var doubleClicked = isLeftDoubleClick && isInsideRow && !overCheckbox && !overButton;
            if (doubleClicked)
            {
                selection.SetPackage(file, !selection.Contains(file));
                current.Use();
            }
        }

        private void ImportCheckedPackagesOfItem(List<ItemFile> packages)
        {
            if (BLMDialogs.WarnIfImportRunning())
            {
                return;
            }

            var targets = packages.Where(file => selection.Contains(file)).ToList();
            var paths = targets.Select(file => file.r_FullPath).ToList();
            ImportProgressWindow.RunImmediately(paths, BuildImportOptions());
            // 取り込み開始した分は選択を外す
            selection.SetPackages(targets, false);
        }

        // タグを折り返しレイアウトのボタンとして並べる。クリックされたタグを返す（無ければnull）
        private string DrawClickableTagFlow(IReadOnlyList<string> tags, float wrapWidth)
        {
            var effectiveWidth = Mathf.Max(wrapWidth, 120f);
            var offsets = new List<Vector2>();
            var sizes = new List<Vector2>();
            var x = 0f;
            var y = 0f;
            var lineHeight = 0f;
            foreach (var tag in tags)
            {
                var size = styles.TagChip.CalcSize(new GUIContent(tag));
                if (x + size.x > effectiveWidth && x > 0f)
                {
                    x = 0f;
                    y += lineHeight + c_ChipSpacing;
                    lineHeight = 0f;
                }
                offsets.Add(new Vector2(x, y));
                sizes.Add(size);
                x += size.x + c_ChipSpacing;
                if (size.y > lineHeight)
                {
                    lineHeight = size.y;
                }
            }

            var totalHeight = y + lineHeight;
            var area = GUILayoutUtility.GetRect(effectiveWidth, totalHeight);
            string clicked = null;
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
                    var label = $"オーダー {orderId.r_Value}";
                    if (singleOrder)
                    {
                        label = "オーダーページを開く";
                    }
                    if (GUILayout.Button(label, GUILayout.Height(22), GUILayout.Width(160)))
                    {
                        Application.OpenURL(orderId.OrderPageUrl());
                    }
                }
                // 中継ページ経由で拡張機能に自動ダウンロードさせるボタン
                DrawDownloadButton(item);
                GUILayout.FlexibleSpace();
            }
        }

        // 中継ページを BLMImporterDLtargets（itemid/variationid の配列）付きで開く赤いボタン。
        // 拡張機能が中継ページでジョブを受け取り、オーダーページへ移動してダウンロードする
        private void DrawDownloadButton(ItemRuntime item)
        {
            var orderId = item.r_Master.r_OrderIds[0];
            BLMGuiDraw.DownloadButtonLayout(() => BLMGuiDraw.OpenDownloadPage(item, orderId), GUILayout.Height(22), GUILayout.Width(110));
        }
    }
}
