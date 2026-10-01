# たくポケ Win

時間割・時間割変更・試験・返却・学校行事・リンクを確認する、Windows向けの非公式アプリです。

現在は開発中です。共通の時間割統合・名称照合・検索・通知差分、時間割変更XLSXの読み取り、学校データの暗号化保存を実装しています。WinUIの画面は起動用ひな形で、利用者向けの機能画面、インストーラー、配布版はまだありません。

初回正式版は、iOS版の現行機能への対応、同じ入力に対する結果の比較、失敗時の動作確認、Windows実機での確認が揃ってから公開します。

## 開発構成

C# / .NET 10 / WinUI 3によるWindowsネイティブアプリとして開発します。
以下の構成で実装を進めます。PDF解析・API連携・機能画面は開発対象です。

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

## ビルド・自動確認

.NET 10 SDKを使用します。WinUIのビルドと起動にはWindowsの開発環境が必要です。

```sh
dotnet test tests/Takupoke.Core.Tests/Takupoke.Core.Tests.csproj --configuration Release
dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --configuration Release
dotnet build src/Takupoke.Win/Takupoke.Win.csproj --configuration Release -p:Platform=x64
```

GitHub Actionsはpush・pull request・手動実行に対応します。現在は以下を確認します。

- 時間割の優先関係・授業時刻・クラス制約・名称照合・検索・通知差分、iOSと共用する架空のXLSX正規化データ（Linux / Windows）。
- XLSXの構造・数式の保存値・不正入力拒否、暗号化保存・正常結果保持・ロック・日本時間の半期切り替え（Linux / Windows）。
- WinUIのx64 Debug / Release、ARM64 Releaseビルド。
- x64 Release版の開発用ウィンドウの起動。
- 内部文書・資料などの追跡禁止ファイルと、GitHub noreplyのコミット作者情報。

GitHub Actionsの保存用キャッシュやartifactは作成しません。NuGet・CLIの作業用キャッシュはジョブ専用の一時領域へ置き、成功・失敗時とも終了処理で削除します。runner終了後も残るキャッシュを利用者が手動削除する運用にはしません。

資料解析・API・通知・画面のテストは実装に合わせて追加します。CIでの起動確認と、学校アカウント認証・OneDrive同期・通知・インストールや更新の実機確認は別に扱います。

## データの扱い

学校資料は端末内で解析し、OneDriveの同期フォルダーから利用者が選んだファイルを読み取る方式を予定しています。個人設定は端末内に保存します。

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
