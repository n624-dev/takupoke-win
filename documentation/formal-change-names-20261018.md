# 時間割変更一覧の正式名称（2026-10-18）

## 変更

配布10447の元fce8bb4と同じ製品コードを持つbb4250aを基準にする。
main944662bは10438の記録であり、そこから新しい配布を作らない。
時間割変更一覧の変更前・変更後は、現在の名称対応表の正式名称を優先する。
変更前が空欄の場合も、同じ日・時限の元の通常授業を対応表へ通す。
対応がなければ元の名前を使い、試験・返却の授業から推測して補完しない。
原本・保存結果・グリッドの略称・Strict PDFと全入口／経路・OCRモデル構成は変更しない。

## 検証

編集前にtest_catalog.py search SchedulePresentationを実行し、既存の
SchedulePresentationTestsを更新した。新仕様の正式名称優先に加え、対応表なしのfallback、
明示の変更前、変更後、未対応の変更後、通常の略称、試験から補わない条件を確認する。
Linux .NET10.0.402でCore301件・Integration1161件、失敗0・skip0。
配布のPython検査と一覧14件も成功し、一覧write／check --base bb4250aを実行した。
WinUI・実Windowsの操作、導入済みパッケージは配布Actionsで別に確認する。
旧mainだけで行った183／143件の結果を現配布コードの検証へ加算しない。

配布workflowは検証済みインストーラーを同じjob内でReleaseへ添付する。
今回開始する配布経路の保存用Actions artifactを除き、キャッシュも作らない。
Release添付・正常結果の保持・全事前検査・導入パッケージ検査を維持する。

## iOSの除外報告との比較

配布中のWindows10447には、適用完了までプレビューを閉じない処理と、失敗時の選択・理由保持がある。
入口で年度・Parser・原本を照合し、適用前に実ファイルを再取得して全残存行を解析する。
iOSの画面消失をWindowsでも未修正とは扱わず、この経路は今回変更しない。
確認中に原本ID／SHAが変われば拒否する条件も維持する。物理OneDriveの挙動は未測定。

状態表示の追加調査では、`MaterialSnapshot.AnalysisStatus` がSHA／Parserだけで
解析済みを判定し、failureを原本・年度で照合しない条件も確認した。
iOSの年度・古い失敗表示に相当する条件はWindowsにもある。今回の正式名称変更では未修正。
解析条件・原本照合を緩める根拠とは扱わず、表示の意味と最終操作の失敗通知を分けて検討する。

## 公開結果

[配布39060106972・試行1](https://github.com/n624-dev/takupoke-win/actions/runs/39060106972)は
同一source b7d789d831bcd8b15cb6eefff2383ba4852bd477の全8チェックが成功し、全jobが終端となった。
Coreは両OS301件、IntegrationはLinux1161件・Windows1163件で失敗・skip0。
x64 Debug／Release・ARM64 Releaseと、通常アプリの実Windows1026 UI検証を確認した。
導入済みアプリの新規・移動・再インストール・旧版更新等の六状態もすべて成功した。
各状態の検証項目数は 1026／1027／1026／1027／1026／1026。個々のUIassert数でありテスト関数数ではない。

[0.1.0-dev.10448](https://github.com/n624-dev/takupoke-win/releases/tag/v0.1.0-dev.10448)を通常Release・Latestとして公開。
匿名で6添付を取得し、APIのsize／digest・SHA256SUMSの5項目と照合した。
両ZIPは全ファイルの集合・ハッシュ・版・元sourceをmanifestと照合し、x64 589／ARM64 585ファイルが一致した。
x64 Setupは80,064,677 bytes、SHA-256は
`9effd674a45a42b39352ceccd826fae1c737417298b5a96d4d9b96499f7a9f31`。
今回のrunの保存用Actions artifactは0。確認用の所有ダウンロードは回収済み。
実Windows利用者の導入・ARM64実機・学校資料の品質は今回確認していない。
正式名称以外の除外／状態表示の指摘を解消済みとは扱わない。
