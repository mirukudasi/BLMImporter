# Changelog

このパッケージの主な変更点を記録します。

## [1.1.0] - 2026-08-27

### Added

- unitypackage がこのプロジェクトに入っている版と比べて新しいか古いかを判定する `ImportedPackageIndex` を追加。同じアイテム内で収録アセットの GUID が重なる unitypackage 同士を同じ系列の別の版とみなし、系列ごとに「現在の版」を決める。現在の版は、GUID から引いたプロジェクト側ファイルの大きさが書庫内の大きさと一致する割合が最も高い版で、同率なら作者側の更新時刻が新しい版とする。各 unitypackage の状態は、作者側の更新時刻を現在の版と比べて「現在の版」「古い版」「新しい版」「未インポート」「調査中」のいずれかになる。
- 照合は GUID で行うため、インポート後にフォルダを移動・改名していても追従する。中身は読まずファイルの大きさだけを比べるため軽量。
- unitypackage (gzip 圧縮された tar) から収録アセットの GUID・作者側の更新時刻・本体の大きさを取り出す `UnityPackageArchive` を追加。

## [1.0.1] - 2026-07-19

### Changed

- ダウンロードURLを Booth オーダーページ直接(クエリパラメータ付き)から、BLMDownloader の中継ページ(`mirukudasi.github.io/BLMDownloader/Action/`)経由に変更。Booth のページにクエリパラメータを載せない。拡張機能 Booth Downloader 0.3.0 以降が必要。

## [1.0.0] - 2026-06-27

### Added

- 初回リリース。GUI 非依存の BLM ライブラリ読み込み・絞り込み・unitypackage インポート API。
