# Microsoft Store自動提出の検証記録

2026年10月23日、日本時間。Windowsのローカル作業ディレクトリでの検証。
ベースは`e001729aee0afadde59032d42ed5c11e015e4bc0`。
この記録はStore公開や審査通過の記録ではない。

## 実行結果

- .NET SDK 10.0.402でWinUI x64 Releaseのビルド成功。変更したパッケージ判定・プロトコル登録・自動起動APIをコンパイルした。
- x64の自己完結型publishとMakeAppxによるMSIXスキーマ検証・包装に成功。
- 生成した実MSIXを開き、Store identity、版番号、x64 PEヘッダー、コンパイル済みXAML、.NET同梱、規約・依存ライセンス・画像を検証して成功。
- Coreの既存テスト199件、Integrationの既存テスト215件が成功。skipなし。
- Store配布境界・内容検査の新規Pythonテスト9宣言が成功。
  開発版・曖昧な版番号・MSIX範囲外の番号の拒否、Windowsのファイル名大小文字、誤ったCPU形式、必須ファイル欠落、版番号不一致、.NET未同梱の拒否を確認した。
  開発者PCでは導入検証スクリプトが証明書を作る前に拒否することも確認した。
- actionlint 1.7.12で、新規Microsoft Store workflowと変更したCI workflowの検査に成功。

## 既存検査の十分性と追加の理由

CoreとIntegrationの規則・保存・認証検証の期待結果は変更していない。
自動起動・URI登録はWindows依存であり、上記414件の成功だけではMSIX導入後の動作確認にはならない。
そのため新規workflowは、既存の包括的CIに加え、署名したテスト用MSIXを使い捨てのWindows runnerへ導入して、
既存の`Takupoke.Win.UITests`による画面操作、保存・再起動、URIコールバック、架空データの画面撮影を実行する。
これらが失敗すると配布ファイルの保存・Storeへの提出へ進まない。
包装の境界とCPU形式検査は従来のライセンス検査だけでは検出できないため、独立した失敗入力を追加した。

## 未完了の確認

ローカルではARM64のpublishまで成功したが、インストール済みVisual Studio Build ToolsにARM64のVC再配布DLLがなく、
ARM64の包装と2アーキテクチャのbundle作成は完了していない。
既存の配布経路が使用しているGitHub Windows runnerで、この残りを確認する必要がある。
ローカルPCでは、証明書の信頼設定を変更するMSIX導入テストは実行していない。
Store認証・初回掲載情報・初回審査・実際の公開は別途確認する。
既存の重複using、nullable、xUnitアナライザーの警告はこの変更の修正対象に含めていない。
