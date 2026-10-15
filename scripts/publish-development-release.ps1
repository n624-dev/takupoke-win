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
- CPU用OCRを、日本語PP-OCRv6 medium＋追加学習した英数字PP-OCRv5へ切り替えました。設定から約99 MiBのモデルを取得／更新してください。旧モデルの更新に失敗しても以前のファイルを保持します。
- PDFのWindows描画倍率を144DPIへ修正し、PNGの原画像と取得時の寸法・描画方式を訂正時にも使用します。
- プレビューの科目・教員・教室を複数選択し、原画像を見ながら訂正できます。高信頼度の誤読も対象です。以前の入力、中止復帰、明示採用、再起動後の値を保持します。
- 新規の取得／訂正記録は確認・本文訂正を最大128項目まで扱います。旧記録の3項目訂正上限は維持します。見出しは元文字列の確認だけで、欠落枠の推測追加はしません。

OCRの実測と使用方法
- 設定の「AI・OCRを使用する」は初期OFFです。ONにし、モデル管理で日本語・英数字OCRを取得／更新してください。通常解析とPDF原文字の直接取得を優先し、失敗した資料で復旧を開始します。
- 復旧プレビューと原PDFを照合してください。複数欄を訂正する場合は「まとめて選択」から確認へ進みます。各項目を確認し、訂正後の全資料プレビューを明示採用して初めて正式結果へ反映します。
- Windowsの物理2／論理4CPU・約16GiB RAMで、架空の2字体・各5ページを全120枠／本文360項目取得しました。CER1.37%／1.03%、本文完全一致329/360／337/360、欠落負例4件拒否。OCR・解析約43～51秒、最大working set約459MBでした。ダウンロード・初期smoke・利用者の照合時間は含みません。
- 本文の誤読31／23項目、高信頼度の誤読27／23項目が残ります。confidenceだけで照合を省略できません。同じ架空帳票を描画・解析の修正に使用した回帰検証で、未知の書式・スキャン・実学校資料での成功率は示しません。
- 原PDF／ページ／cropのSHA、原文・位置・native confidenceを保持します。学校資料やOCR結果を外部AIへ送信しません。初回の明示採用が必要で、失敗時は前回の正常結果を保持します。
- 追加の生成AI（Foundry）候補は未合格のため配信しません。iOSのコードは変更していません。
- Windows 11 24H2（build 26100）以降が必要です。Setup版とZIP版の両方に適用します。

OCRの全記録と訂正手順：
https://github.com/n624-dev/takupoke-win/blob/$env:GITHUB_SHA/documentation/local-ai-status-20261014.md

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
    gh release create $tag @assets --repo $repo --target $env:GITHUB_SHA --draft --prerelease --title "たくポケ $Version（開発確認版）" --notes-file $notes
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
