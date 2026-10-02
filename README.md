# たくポケ Win

時間割・時間割変更・試験・返却・学校行事・リンクを確認する、Windows向けの非公式アプリです。

現在は開発中です。ホーム・一覧・時間割・設定をWinUIで実装し、資料の選択・解析・保存・更新、授業や変更の詳細、リンクの検索・個人設定につないでいます。正式版はまだ公開していません。ビルド済みの開発確認版はGitHub Releasesから配布します。

初回正式版は、iOS版の現行機能への対応、同じ入力に対する結果の比較、失敗時の動作確認、Windows実機での確認が揃ってから公開します。

## Windowsへの導入（ビルド不要）

[開発確認版のダウンロード](https://github.com/n624-dev/takupoke-win/releases)からSetup.exeを取得してください。全機能の確認を終えた正式版ではありません。

1. Intel/AMDの通常の64ビットPC：`win-x64-Setup.exe`。Windows on Arm：`win-arm64-Setup.exe`。
2. Setup.exeを通常ユーザーとして実行し、画面の案内に従います。
3. スタートメニューの「たくポケ Win」から起動します。

.NET・Windows App SDK・VCランタイムを同梱しているため、Visual StudioやSDKのインストール、ソースのビルドは不要です。アプリ内ブラウザ用のWebView2 Runtimeが未導入の場合は、セットアップがMicrosoftの公式インストーラーを実行します（インターネット接続が必要です）。

開発確認版は未署名です。Windowsの警告や組織のポリシーにより起動が制限される場合があります。Windows用認証クライアントは登録済みで、認可開始の同意画面まで確認しました。学校アカウントによるログイン完了と実データ取得の実機確認は継続中です。自分が利用を認められた資料を選択し、端末内で解析できます。

更新時はアプリを「完全に終了」して新しいSetup.exeを実行します。自動更新は未実装です。アンインストールはWindowsの「インストールされているアプリ」から行います。再インストールに備えて保存領域は保持します。完全に削除する場合は、アプリ終了後に`%LOCALAPPDATA%\TakupokeWin`を削除してください。OneDrive上の原本は削除しません。

ZIPも配布します。全ファイルを展開して`Takupoke.Win.exe`を起動してください。実行ファイルだけを取り出すと起動できません。ARM64はビルドと形式を確認し、ARM64実機での起動は未確認です。

## 開発構成

C# / .NET 10 / WinUI 3によるWindowsネイティブアプリとして開発します。
時間割の構成規則を画面から分離し、ホームと時間割で共用します。

```text
src/
  Takupoke.Core/              時間割・名称照合・日付・通知差分の規則
  Takupoke.Infrastructure/    資料解析・API・保存・暗号化
  Takupoke.Win/               画面・ファイル選択・認証・Windows連携
tests/
  Takupoke.Core.Tests/
  Takupoke.Integration.Tests/
  Takupoke.Win.UITests/
```

## 実装状況

- 通常時間割・試験・返却PDFの文字位置・フォント・罫線を使う解析と、時間割変更XLSXの読み取り・正規化。曖昧な入力は正常結果として保存しません。
- 通常授業・変更・試験・返却・行事の統合、日付別時刻、条件付き名称照合、連続授業・重なる授業の表示。
- 登録した原本のファイル識別情報とハッシュによる再確認、変更通知の集約、前回正常結果と対応する保存資料の保持。
- 既存APIの公開revision確認と、利用者操作後の1回の認証による一覧・名称・授業時刻の独立更新。Authorization Code・PKCE・OIDC検証を実装し、トークンは永続保存しません。
- AES-GCMによる学校データの暗号化、ユーザー単位DPAPIによる鍵保護、日本時間の半期切り替え、ロック時の利用停止。公開行事・個人設定は別に保存します。
- Windowsローカル通知の比較基準・通知待ち保存と重複抑制、任意のトレイ常駐・自動起動、復帰時の再確認。
- 保存PDFの表示、アプリ内WebView2と外部ブラウザの選択、外部アプリ用リンクの未対応案内。

公開テストは架空データと架空通信を使います。iOS mainの`bbd2bd7`を基準に、固定したSwiftの元コードから生成した時間割88ケース・XLSX正規化11ケース・PDF幾何情報5ケースと検索・表示文字・クラス・色の期待結果をC#と比較します。ActionsでSwift側の結果も再生成して照合します。学校の実資料を使ったPDF全体の適合性確認は未完了です。署名、正式配布・更新方法の確定も残っています。利用規約・プライバシーポリシーと依存ライセンスは開発配布物に同梱します。

## ビルド・自動確認

.NET 10 SDKを使用します。WinUIのビルドと起動にはWindowsの開発環境が必要です。

```sh
dotnet test tests/Takupoke.Core.Tests/Takupoke.Core.Tests.csproj --configuration Release
dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --configuration Release
dotnet build src/Takupoke.Win/Takupoke.Win.csproj --configuration Release -p:Platform=x64
```

GitHub Actionsはpush・pull request・手動実行に対応します。現在は以下を確認します。

- 時間割の優先関係・授業時刻・クラス制約・名称照合・検索・通知差分、iOSと共用する架空のXLSX正規化データ（Linux / Windows）。
- PDFの描画位置・文字対応・並記・結合・時刻、XLSXの構造・数式の保存値・不正入力拒否（Linux / Windows）。
- 架空API応答、OIDC署名・claim・callbackの検証、独立更新・キャンセル時の保持、通知の再試行・受理済み照合（Linux / Windows）。
- 暗号化保存・原本の置換検知・正常結果保持・ロック・日本時間の半期切り替え（Linux / Windows）。
- 固定したiOSの元コードのハッシュを確認し、Swiftで架空データを実行して比較用の期待結果を検証（macOS）。
- WinUIのx64 Debug / Release、ARM64 Releaseビルド。
- x64 Release版をUI Automationで操作し、4画面の移動・クラス制約・設定保存・再起動後の保持・キーボードフォーカス・オフライン更新を確認。
- 内部文書・資料などの追跡禁止ファイルと、GitHub noreplyのコミット作者情報。

導入パッケージ用の手動Actionsでは、全テスト成功後にx64・ARM64の自己完結型アプリとSetup.exeを作成します。x64は作ったSetup.exeを実行し、新規導入・画面操作・設定を保持した再インストール・アンインストールを確認してから開発確認版として配布します。

GitHub Actionsの保存用キャッシュやartifactは作成しません。配布用の完成ファイルだけをGitHub Releasesへ置きます。NuGet・CLIの作業用キャッシュはジョブ専用の一時領域へ置き、成功・失敗時とも終了処理で削除します。runner終了後も残るキャッシュを利用者が手動削除する運用にはしません。

テストは実装に合わせて追加します。CIでの確認と、学校アカウント認証・OneDrive同期・OS通知・ロックや復帰・アクセシビリティ・インストールや更新の実機確認は別に扱います。

## データの扱い

学校資料は端末内で解析し、OneDriveの同期フォルダーから利用者が選んだファイルを読み取ります。再読み取りの成功は、クラウド上の最新版との同期完了を示すものではありません。個人設定は端末内に保存します。

学校の実資料、個人情報、認証情報、署名鍵、内部用の文書は公開ソースに含めません。公開テストには完全に架空の入力・期待結果を使用します。

## コミット設定

このGitHubアカウントで開発する場合、clone後にリポジトリ内で設定します。

```sh
git config --local user.name "n624-dev"
git config --local user.email "91827902+n624-dev@users.noreply.github.com"
git config --local user.useConfigOnly true
```

## ライセンス

プロジェクト自体のライセンスは未選定です。
