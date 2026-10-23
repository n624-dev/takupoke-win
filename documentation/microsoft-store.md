# Microsoft Storeへの正式版配布

Store名は「たくポケ」、Store IDは`9NC5CVVZD59H`。
MSIXのNameは`n624.484124996A09A`、Publisherは`CN=680A4AD1-A65A-4F06-A2FA-C6BE82E46F72`、PublisherDisplayNameは`n624`。
初回正式版は`1.0.0`とし、MSIXのバージョンは`1.0.0.0`になる。

## 費用

公開リポジトリの標準GitHub-hosted runner、既存のEntra ID Free、Microsoft StoreのMSIX署名を使う。
有料runner、Azureの有料サービス、Microsoft 365ライセンス、商用署名証明書は不要。
Actionsの保存用artifactやキャッシュは作らず、配布用MSIXとSHA-256だけをGitHub Releasesへ置く。
非公開リポジトリへの変更や有料runnerへの変更は、この費用条件に含まれない。

## 初回

1. 個人開発者のPartner Centerに既存のEntra ID Freeを関連付ける。
   設定用ユーザーと、自動提出用のEntraアプリは別のIDである。
   設定用ユーザーのパスワードはGitHubへ保存しない。
2. 自動提出用EntraアプリをPartner Centerへ追加する。
   公式手順のManagerロールは他製品にも及ぶため、権限範囲を確認する。
   個人Microsoftアカウントのパスワードや、学校の認証情報は自動化に使わない。
3. 次のGitHub Actions Secretsを登録する。

   | Secret | 値 |
   |---|---|
   | `MICROSOFT_STORE_TENANT_ID` | 関連付けたEntraテナントID |
   | `MICROSOFT_STORE_CLIENT_ID` | 自動提出用EntraアプリのクライアントID |
   | `MICROSOFT_STORE_CLIENT_SECRET` | そのアプリ専用の有効期限付きシークレット |
   | `MICROSOFT_STORE_SELLER_ID` | Partner CenterのSeller ID |

4. `Microsoft Store stable release`をmainから手動実行し、versionに`1.0.0`を指定する。
   全CI成功後にx64とARM64を包装し、MSIX内容とx64のインストール後UIを検証する。
   成功すると、未公開のGitHub Release `v1.0.0`にMSIX bundleとSHA-256を保存する。
5. Partner Centerで初回の説明・プライバシーポリシーURL・スクリーンショット・年齢区分等を設定し、
   検証済みMSIXをアップロードして初回審査へ提出する。
   初回公開はStore CLIでは完了できない。
6. 初回Store公開を確認し、GitHub Release `v1.0.0`を公開した後に、
   リポジトリ変数`MICROSOFT_STORE_AUTO_SUBMIT`を`true`にする。
   初回準備だけの手動実行は、認証キーが登録済みでもStoreへ自動提出しない。

## 継続更新

`v1.0.1`等の正式版GitHub Releaseを公開すると、全CIとMSIXの作成・導入検証を通してからStoreへ提出する。
開発確認版の`v0.1.0-dev.10455`等、およびGitHubのPre-releaseは対象外。
通常Releaseとして公開している開発確認版も、名前の`-dev.`によって除外する。
MSIXの各数値は65535以下とし、Storeに予約された4つ目の数値は常に0にする。
同じバージョンの公開済み成果物は置き換えない。

ビルドジョブにはStoreの認証キーを渡さない。
提出は別の新しいrunnerで行い、ビルド元のソースコードをcheckoutしない。
ダウンロードしたMSIXは、検証ジョブから渡されたSHA-256と照合する。
ActionsはコミットSHAで固定する。
Storeの認証キーは提出ステップでだけ使用するが、GitHub側の書き込み権限やActionsの改ざんは依然として管理対象である。

一時的な提出エラーで包装ジョブまで成功している場合は、Actionsの「Re-run failed jobs」で提出ジョブだけを再実行する。
修正したバイナリを配布する場合は、新しい正式版番号を使う。
APIによる提出後は、同じ提出をPartner Centerの画面から編集しない。
Store審査の成功と実際の公開は、Actionsの提出成功とは別に確認する。

## 検証と証明書

`scripts/build-store-package.ps1`はWindows SDKのMakeAppxでスキーマを検証し、自己完結型MSIX bundleを作る。
配布するMSIXはMicrosoft Store側で署名するため、配布用秘密鍵をリポジトリに持たない。
`scripts/test-store-installation.ps1`は使い捨てのGitHub-hosted runner限定で、1日有効のテスト証明書を生成する。
導入検証専用コピーへ署名し、架空データによるUI・再起動・設定保存・URIコールバックと画面撮影を実行する。
その後、テスト導入と証明書の信頼設定を回収する。配布用MSIXへテスト署名は付けない。
開発者PCやself-hosted runnerでは、この導入検証スクリプトは証明書を作る前に失敗する。
ARM64の実行形式は検証するが、x64 runnerでARM64実機の起動成功を主張しない。

MSIXではプロトコル、通知COMサーバー、自動起動をパッケージマニフェストで宣言する。
自動起動は利用者が有効にし、Windows側で無効にされた設定を上書きしない。
GitHubのSetup.exe配布では従来の登録方式を維持する。

## 公式資料

- [Store CLIとGitHub Actions](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions)
- [初回公開とStore CLI](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/overview)
- [提出APIの前提条件](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services)
- [アカウントの権限範囲](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/overview-of-roles-and-permissions-for-account-users)
- [公開リポジトリの標準runner](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
