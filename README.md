# BLM Importer

BOOTH Library Manager (BLM) のライブラリ(data.db)を Unity エディタから扱うためのパッケージ群です。VPM (VRChat Package Manager / VCC) で配布しています。

## パッケージ

| パッケージ | ID | 説明 |
|---|---|---|
| BLM Importer Core | `com.mirukudasi.blm-importer-core` | GUI 非依存のコア API(読み込み・絞り込み・インポート) |
| BLM Importer | `com.mirukudasi.blm-importer` | 上記を使うエディタウィンドウ。Core に依存 |

## インストール (VCC / ALCOM)

1. 下記リスティングを VCC に追加します:

   ```
   https://mirukudasi.github.io/BLMImporter/index.json
   ```

   または [リスティングページ](https://mirukudasi.github.io/BLMImporter/) の「Add to VCC」ボタンから追加できます。
2. プロジェクトの Manage Project から `BLM Importer`(ウィンドウ)または `BLM Importer Core`(API のみ)を追加します。
   - `BLM Importer` を追加すると Core も自動的に導入されます。

## 使い方

- メニュー **Tools/BLMImporter** から BLM Importer ウィンドウを開きます。
- API のみ利用する場合は Core の README を参照してください:
  `Packages/com.mirukudasi.blm-importer-core/README.md`

## 開発

このリポジトリ自体が Unity プロジェクト(2022.3)兼パッケージ配布元です。`Packages/` 配下に両パッケージを埋め込んでおり、プロジェクトを開けばそのままコンパイル・動作確認できます。

### リリース手順

1. 対象パッケージの `package.json` の `version` を更新します。
2. GitHub Actions の **Build Release** を実行し、リリースするパッケージを選択します。
3. zip / unitypackage / package.json を含む GitHub Release が作成され、続いて **Build Repo Listing** が走って GitHub Pages のリスティング(`index.json`)が更新されます。

## ライセンス

MIT License — リポジトリルートおよび各パッケージの `LICENSE` を参照してください。
