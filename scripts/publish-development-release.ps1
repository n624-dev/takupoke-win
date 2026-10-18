param([Parameter(Mandatory)][string]$Version, [switch]$ValidateTagOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Version -notmatch '^\d+\.\d+\.\d+-dev\.\d+$') { throw 'Only a development release can be published by this workflow.' }
$repo = $env:GITHUB_REPOSITORY
if ($repo -ne 'n624-dev/takupoke-win') { throw 'Unexpected release repository.' }
$tag = "v$Version"
$tagJson = gh api "repos/$repo/git/ref/tags/$tag" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Push tag $tag at the build commit before dispatching this workflow." }
$target = ($tagJson | ConvertFrom-Json).object
for ($depth = 0; $target.type -eq 'tag' -and $depth -lt 4; $depth++) {
    $tagObject = gh api "repos/$repo/git/tags/$($target.sha)"
    if ($LASTEXITCODE -ne 0) { throw 'The release tag could not be resolved.' }
    $target = ($tagObject | ConvertFrom-Json).object
}
if ($target.type -ne 'commit' -or $target.sha -cne $env:GITHUB_SHA) { throw 'The release tag must identify exactly the build commit.' }
if ($ValidateTagOnly) { Write-Output "Verified $tag at $env:GITHUB_SHA."; return }
$runUrl = "https://github.com/$repo/actions/runs/$env:GITHUB_RUN_ID"
$notes = Join-Path $env:TAKUPOKE_CI_TEMP 'release-notes.txt'
@"
Windowsでビルドせずに導入できる開発確認版です。初回正式版ではありません。

通常のIntel/AMD PCは takupoke-$Version-x64-Setup.exe、Windows on Armは takupoke-$Version-arm64-Setup.exe を実行してください。
スタートメニューの「たくポケ」から起動できます。.NET・Windows App SDK・VCランタイムを同梱しています。
WebView2 Runtimeが未導入の場合はセットアップがMicrosoftの公式インストーラーを実行します（通信が必要です）。

今回の更新
- 今日の予定の科目は正式名称を優先し、対応表がない場合は原名を表示します。
- シラバスの学科・入学年度区分を選択でき、一覧やお気に入りからその学科と当年で開けます。
- 一覧・名称・授業時刻の取得対象を選べます。初回一括はMicrosoft認証一回、一覧だけなら学内ネットワークで認証できます。学外はMicrosoft認証です。
- 有効な認証結果をメモリ内だけで再利用し、取消・再起動・保存期間切替で破棄します。
- 時間割変更の解析済み判定に年度と確認内容を反映し、古い失敗を現在の資料の失敗として表示しないよう修正しました。
- ローカルHTTPをアプリ内で開けるようにし、接続失敗・証明書・拒否の理由を表示します。ポップアップは別ページとして処理し、証明書検証は維持します。
- 厳格なPDF解析と、その入口・通過経路は維持しています。

引き続き含む機能
- 時間割変更一覧の前後は正式名称を優先し、対応表がない科目は原名を表示します。
- iOSの最新解析変更に追従し、通常PDF解析の受理条件と復旧用の証拠検証を分離しました。通常解析の成功だけで復旧の証拠を完全と扱いません。
- P分類に加えてWindows固有の固定Rコードを保存・表示し、失敗箇所を本文を含めずに識別します。
- 返却表の結合クラス欄に入り込む時限見出しを罫線と位置で分離します。本文の文字列を一律削除しません。
- 1年生の科目に単独のCN・ES・ITが記載されている場合は所属へ振り分け、原本クラス・空欄の優先・旧保存結果の移行・採用前の保存先表示を保持します。
- XLSXの同じ原本の再選択と解析更新で行除外の確認を保持します。現在の解析で再検証し、成功時だけ確認と結果をまとめて更新します。
- 行除外のプレビューは適用完了まで閉じず、失敗時も選択状態と具体的な理由を表示します。
- OCRモデル取得で実受信量と整合性確認・動作確認を表示します。保存済みモデルの確認をダウンロードと混同しません。
- 返却授業の詳細から、本文・氏名・画像を含まない位置情報を端末内で取得・コピーできます。
- 復旧の描画検証は両方のオーバープリント無効化と状態復元に対応し、後から塗りつぶされた罫線を根拠に使うことを拒否します。

- 認証コールバックの既存ウィンドウへの転送で、起動側がCOM処理を継続しながら完了を待つよう修正しました。
- mainのXLSX行除外を取り込みました。原文を確認して問題行を選び、曜日だけの同じ行はまとめて除外できます。日付から曜日を求める既存の補正も保持します。
- 選択済み資料が同じ場所で更新されたとき、ファイルを選び直さずに最新版を再取得します。
- 内容が変わらない更新では解析結果と行除外の確認を維持し、内容が変わった場合は再解析します。取得や解析の失敗時は前回正常結果を保持します。

- 文字情報のあるPDFの縁取り文字・色付き背景・罫線・矩形の表示範囲に対応しました。通常解析で扱えるPDFはOCRなしで取り込みます。復旧では元の文字・字体・位置・字形を照合し、未確認箇所を原画像で確認します。
- 部分的な切れや重なりが残るnative文字は、原画像を確認してから採用します。架空のOCRスコアは付けず、見出し・注記は書き換えません。原本の注記もプレビューから開けます。
- 学年の物理的な所属、短い欄、上下に分かれた授業、既知native試験表の2行形式と全角時刻を改善しました。
- CPU用OCRを、日本語PP-OCRv6 medium＋追加学習した英数字PP-OCRv5へ切り替えました。設定から約120 MiBのモデルを取得／更新してください。旧モデルの更新に失敗しても以前のファイルを保持します。
- PDFのWindows描画倍率を144DPIへ修正し、PNGの原画像と取得時の寸法・描画方式を訂正時にも使用します。
- プレビューの科目・教員・教室を複数選択し、原画像を見ながら訂正できます。高信頼度の誤読も対象です。以前の入力、中止復帰、明示採用、再起動後の値を保持します。
- 新規の取得／訂正記録は確認・本文訂正を最大128項目まで扱います。旧記録の3項目訂正上限は維持します。見出しは元文字列の確認だけで、欠落枠の推測追加はしません。

OCRの実測と使用方法
- 設定の「AI・OCRを使用する」は初期OFFです。文字情報のあるPDFの通常解析はOFFでも使えます。原画像確認・画像OCR・復旧を使う場合はONにし、モデル管理で日本語・英数字OCRを取得／更新してください。通常解析とPDF原文字の直接取得を優先し、失敗した資料で復旧を開始します。
- 提供された3種類の資料で、通常取り込みまたは原画像確認への経路をローカル検証しました。画像OCRの正解率や未知の帳票での成功率、利用者本人の確認時間を測定した値ではありません。実資料を公開テストや学習には含めず、作業環境の原本・派生ファイルを削除しました。
- 復旧プレビューと原PDFを照合してください。複数欄を訂正する場合は「まとめて選択」から確認へ進みます。各項目を確認し、訂正後の全資料プレビューを明示採用して初めて正式結果へ反映します。
- 新2資料はWindowsの物理2／論理4CPU・約16GiB RAMで41.857秒／46.111秒、全6資料の最大working set約517MiB。モデル取得・初期ロード・PDF描画・利用者の確認時間は含みません。
- 旧6フォントは閉じた実認識結果の順序と確認形式を再検証し、各360項目中330／337／343／354／351／336項目が一致しました。これは新しい実推論・独立した本番精度ではありません。最初の全文書試験のFAILも保持しています。
- 新資料でも誤読16／5項目、高スコアの誤読6／3項目が残ります。必須確認42／6項目と参照誤読のunionは48／9で128項目以内。人の訂正時間は未測定です。鮮明な架空の罫線付き帳票での比較であり、実学校資料・スキャン全般の成功率ではありません。
- 原PDF／ページ／cropのSHA、原文・位置・native confidenceを保持します。学校資料やOCR結果を外部AIへ送信しません。初回の明示採用が必要で、失敗時は前回の正常結果を保持します。
- 追加の生成AI（Foundry）候補は未合格のため配信しません。iOSのコードは変更していません。
- Windows 11 24H2（build 26100）以降が必要です。Setup版とZIP版の両方に適用します。

OCRの全記録と訂正手順：
https://github.com/n624-dev/takupoke-win/blob/$env:GITHUB_SHA/documentation/ocr-source-order-accuracy-20261016.md

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
    gh release create $tag @assets --repo $repo --verify-tag --target $env:GITHUB_SHA --draft --prerelease=false --title "たくポケ $Version（開発確認版）" --notes-file $notes
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
        if ($release.isDraft -and $release.body.Contains($runUrl)) { gh release delete $tag --repo $repo --yes }
    }
    throw
}
