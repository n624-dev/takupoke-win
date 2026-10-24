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

通常のIntel/AMD PCは takupoke-$Version-x64-Setup.exe、Windows on Armは takupoke-$Version-arm64-Setup.exe を実行してください。
スタートメニューの「たくポケ」から起動できます。.NET・Windows App SDK・VCランタイムを同梱しています。
WebView2 Runtimeが未導入の場合はセットアップがMicrosoftの公式インストーラーを実行します（通信が必要です）。

今回の更新
- 時間割変更の文字列・日付・先頭ゼロ付き学年の読み取りと、科目・教員名の対応を修正しました。
- 検索の長音・後方一致、休み時間を含む授業中表示、試験・返却の変更時刻と単独休講の時刻表示を修正しました。
- 設定を続けて変更した場合の保存と、自動起動設定に失敗した場合の表示を修正しました。
- 保存済み資料の一部を読み込めない場合も、利用できる授業と警告を表示し、他の更新後も未解決の警告を保持します。
- 更新確認の中止・通信待ち・変更監視を改善し、送信待ち通知が別の更新で失われる問題を修正しました。
- バックグラウンド起動時に、ネットワーク応答を待たず通知領域とウィンドウの表示状態を準備します。
- PDFとブラウザの重複表示・終了処理を修正し、再起動後も保存済みPDFの解析エラーの説明を保持します。

未署名のためWindowsの警告や組織のポリシーで起動が制限される場合があります。
学校アカウントでのログイン完了、実資料の解析、OneDrive同期、通知、ロック・復帰、アクセシビリティの実機確認は継続中です。
自動更新は未実装です。更新時はアプリを完全に終了し、新しいSetup.exeを実行してください。

ZIPは同梱ファイルを全て展開して takupoke.exe を起動します。Setup.exeを使う方法を推奨します。
SHA256SUMS.txtで配布ファイルのハッシュを確認できます。

ビルド元コミット: $env:GITHUB_SHA
導入・フォルダー移行・再インストール・アンインストールと画面操作の自動確認: $runUrl
ARM64はビルドと実行形式の確認を行い、ARM64実機での起動は未確認です。
"@ | Set-Content -LiteralPath $notes -Encoding utf8
$assets = @(Get-ChildItem -LiteralPath (Join-Path $env:TAKUPOKE_CI_TEMP 'release-assets') -File | Select-Object -ExpandProperty FullName)
if ($assets.Count -ne 6) { throw 'Unexpected release asset count.' }
gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -eq 0) { throw 'This release version already exists; published assets will not be replaced.' }
try {
    gh release create $tag @assets --repo $repo --target $env:GITHUB_SHA --draft --prerelease=false --title "たくポケ $Version（開発確認版）" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Creating the development release failed.' }
    gh release edit $tag --repo $repo --draft=false --prerelease=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the development release failed.' }
    $publishedJson = gh release view $tag --repo $repo --json isDraft,isPrerelease,targetCommitish
    if ($LASTEXITCODE -ne 0) { throw 'Reading the published release failed.' }
    $published = $publishedJson | ConvertFrom-Json
    if ($published.isDraft -or $published.isPrerelease -or $published.targetCommitish -ne $env:GITHUB_SHA) { throw 'The published release does not match the verified source or normal release status.' }
    $latestTag = gh api "repos/$repo/releases/latest" --jq '.tag_name'
    if ($LASTEXITCODE -ne 0 -or $latestTag -ne $tag) { throw 'The published release is not Latest.' }
} catch {
    $releaseJson = gh release view $tag --repo $repo --json isDraft,body 2>$null
    if ($LASTEXITCODE -eq 0) {
        $release = $releaseJson | ConvertFrom-Json
        if ($release.isDraft -and $release.body.Contains($runUrl)) { gh release delete $tag --repo $repo --yes --cleanup-tag }
    }
    throw
}
