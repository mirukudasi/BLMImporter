# BLM Importer

BOOTH Library Manager (BLM) のライブラリを Unity エディタ上で閲覧・絞り込み・一括インポートするウィンドウです。

- メニュー **Tools/BLMImporter** からウィンドウを開きます。
- 前提: [BOOTH Library Manager](https://booth.pm/library_manager) がインストール済みで、データベース(data.db)が存在すること。

## 依存関係

このパッケージは `com.mirukudasi.blm-importer-core`(GUI 非依存のコア API)に依存します。VCC から本パッケージを追加すると Core も自動的に導入されます。

## API のみ利用する場合

ウィンドウを使わずプログラムから利用したい場合は、コアパッケージの README を参照してください:
`Packages/com.mirukudasi.blm-importer-core/README.md`
