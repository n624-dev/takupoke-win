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
- PDFから文字・座標・罫線を完全に取得できた場合は、画像化の前にその情報を再利用します。空欄など画像の根拠が必要な場合だけ画像化します。
- OCR後のセル解析で同じ文字・見出しを繰り返し走査する処理を減らし、原文の順序と位置確認を保つよう改善しました。
- 画像PDFで閉じていない外側の罫線があっても、確認できる内部セルを保持するよう改善しました。未読の印字は省略しません。
- 「AI・OCRを使用する」設定は初期OFFです。OFF時は従来の厳格な解析のみを使用し、ON時は解析失敗時だけ復旧します。
- XLSXの曜日不一致を日付から補正する確認操作を追加しました。承認はファイルが更新されるまで有効です。
- 画像PDFのOCRで、検出モデルのごく小さな計算誤差により読み取りが停止する問題を修正しました。
- 時間割PDFの復旧で、クラス・日付・時限の見出しを誤って結び付ける問題を修正しました。専攻科の学年表記も、対応するクラス見出しと同じ表の領域に属することを確認します。
- 過去に採用した復旧結果の確認を現在の規則で再検証し、既存の訂正内容・原本との照合記録・最初の採用日時を保持します。確認済みの記録は、内容が一致する場合に再確認を求めません。

PDF復旧と手動補正について
- 未確定の科目・教員・教室が資料全体で3項目以内の場合、原本と照合して訂正し、全資料のプレビュー後に明示採用できます。
- 読み取りや検証に失敗した場合は前回の正常結果を保持し、新しい資料が未反映であることを表示します。
- 設定から復旧用モデルの準備状況や削除を確認できます。追加の生成AIモデルは品質確認中のため、この開発版ではダウンロードできません。
- Windows 11 24H2（build 26100）以降が必要です。Setup版とZIP版の両方に適用します。

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
