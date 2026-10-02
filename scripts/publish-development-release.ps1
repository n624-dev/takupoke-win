param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+-dev\.\d+$') { throw 'Only a development release can be published by this workflow.' }
$repo = $env:GITHUB_REPOSITORY
if ($repo -ne 'n624-dev/takupoke-win') { throw 'Unexpected release repository.' }
$tag = "v$Version"
$runUrl = "https://github.com/$repo/actions/runs/$env:GITHUB_RUN_ID"
$notes = Join-Path $env:TAKUPOKE_CI_TEMP 'release-notes.txt'
@"
Windowsでビルドせずに導入できる開発確認版です。初回正式版ではありません。

通常のIntel/AMD PCは win-x64-Setup.exe、Windows on Armは win-arm64-Setup.exe を実行してください。
スタートメニューの「たくポケ Win」から起動できます。.NET・Windows App SDK・VCランタイムを同梱しています。
WebView2 Runtimeが未導入の場合はセットアップがMicrosoftの公式インストーラーを実行します（通信が必要です）。

未署名のためWindowsの警告や組織のポリシーで起動が制限される場合があります。
Windows用認証クライアントを登録し、invalid_requestの原因を解消しました。認可開始の同意画面まで確認済みです。学校アカウントでのログイン完了と実データ取得の実機確認は継続中です。
ファイル選択画面から戻った際の自動更新との競合により、選択した資料の保存が実行されない問題を修正しました。選択・キャンセル・再起動後の保持をWindowsの標準ファイル選択画面を使って自動確認します。
現行iOS main（bbd2bd7）の画面・仕様・更新処理を再比較して修正しました。
メインカラーはiOSの用途に合わせ、標準操作・明示的な移動ボタン・「授業中」の目印に使います。本文・見出しはテーマ色、時間割の「今日」の背景はOSのアクセント色です。「デフォルト」を加え、個別の色指定を解除できます。
ホームの常時変更反映、特別時間割の表示名、留学生授業の絞り込み、変更前の授業や同じ枠の変更、週表示と時刻を修正しました。
資料の詳細情報、曜日・クラスによる独立した解析結果の絞り込み、授業ごとの詳細、警告を確認したXLSXの閲覧専用プレビュー、段階式の初期設定、目的別の使い方、個別ライセンスを追加しました。
更新がない場合の不要な認証、解析失敗後のコピー回収、旧解析版の再試行、キャンセル・ロック・保存期限時の表示と処理も修正しました。
架空データの期待結果を固定したiOSのSwift処理から生成し、時間割88ケース、XLSX正規化11ケース、PDFの位置と罫線5ケース、検索・表示文字・クラス・色の比較をActionsで実施します。実資料での適合性と実機でのOneDrive・学校認証・通知・ロック・復帰・アクセシビリティの確認は未完了です。
自動更新は未実装です。更新時は完全終了して新しいSetup.exeを実行してください。

ZIPは同梱ファイルを全て展開してTakupoke.Win.exeを起動します。Setup.exeを使う方法を推奨します。
SHA256SUMS.txtで配布ファイルのハッシュを確認できます。

ビルド元コミット: $env:GITHUB_SHA
導入・再インストール・アンインストールと画面操作の自動確認: $runUrl
ARM64はビルドと実行形式の確認を行い、ARM64実機での起動は未確認です。
"@ | Set-Content -LiteralPath $notes -Encoding utf8
$assets = @(Get-ChildItem -LiteralPath (Join-Path $env:TAKUPOKE_CI_TEMP 'release-assets') -File | Select-Object -ExpandProperty FullName)
if ($assets.Count -ne 6) { throw 'Unexpected release asset count.' }
gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -eq 0) { throw 'This release version already exists; published assets will not be replaced.' }
try {
    gh release create $tag @assets --repo $repo --target $env:GITHUB_SHA --draft --prerelease --title "たくポケ Win $Version（開発確認版）" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Creating the development release failed.' }
    gh release edit $tag --repo $repo --draft=false --prerelease --latest=false
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the development release failed.' }
} catch {
    $releaseJson = gh release view $tag --repo $repo --json isDraft,body 2>$null
    if ($LASTEXITCODE -eq 0) {
        $release = $releaseJson | ConvertFrom-Json
        if ($release.isDraft -and $release.body.Contains($runUrl)) { gh release delete $tag --repo $repo --yes --cleanup-tag }
    }
    throw
}
