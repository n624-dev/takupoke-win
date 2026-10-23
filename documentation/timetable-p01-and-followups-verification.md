# 通常時間割P01とiOS追加issueの調査

## W16: 利用者の通常時間割P01報告

2026-10-23、利用者から「winの通常時間割が厳格な解析でP01になる」と報告された。直近公開版は0.1.0-dev.10455 / e001729。報告時点で利用者の実際の版・PDF・P01発生枝・OSは未確認。公開済み可視性28件・全Integration215件の成功は、この資料の通過証拠にはならない。

対処を決める前に観測を記録した。直前の可視性変更は同色文字・背景/描画状態が未確認の場合をP01で拒否する。過去のW15はテスト呼出し引数不足によるコンパイル失敗であり、この利用者資料の実行時P01とは異なる。既存PDFの採否・各拒否条件を照合し、不可視文字の検証を外すだけの回避は行わない。

## W17: 復旧処理テストのコンパイル失敗（初回）

2026-10-23、Debian 13 / .NET SDK 10.0.402。作業ブランチ codex/fix-timetable-p01-and-issues-60-63（基点 fb83d74 / 公開コード e001729）の未コミット変更に対し、`dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~RetentionRecoveryTests` を実行した。テスト実行前に AppViewModel.cs:261 が CS0165（ローカル変数 results が未割り当て）でコンパイル失敗した。Core / Infrastructure はビルド済み。ログは作業環境の /tmp/followup-win-retention-first.log。既存の CS8602 / xUnit2031 警告も出力されたが、今回の停止理由は CS0165。修正方針の決定前にこの観測を保存する。

公開コード e001729 の UpdateSharedAsync と W15 の引数不足によるコンパイル失敗記録を照合した。今回は共通 RunAsync の型付き例外処理を追加する際、共有更新内部の try にも同じ catch を挿入し、結果を割り当てず処理を継続できる経路を作った。内部 catch を取り除いて元の例外伝播を維持し、共通 RunAsync だけで復旧案内を表示する。共有更新の結果・APIエラー・キャンセル検証は緩めない。修正と記録を隔離ブランチへ push してから再試行する。

## iOS #60–63 と Windows の対応

- #60: 全時限の変更は各適用特別資料のDisplayTimeに結合範囲全体を渡し、全資料・全時限が同じ時刻を確認できた場合だけ表示する。部分変更は個別時刻を維持する。
- #62: 通常授業だけ、結合カードの各個別時限の開始包含・終了除外で授業中を判定する。既定と日付別API時刻、休憩・昼休みを検査する。専用時刻がある特別授業は連続範囲を維持する。
- #61: 取得/解析失敗の警告は既存で両画面にあるが、sourceのみで初回analysis/attemptがない保存状態は漏れていた。共通MaterialSnapshot.NeedsAttentionに選択済み未解析を含め、実storeから両特別種別の状態を確認する。未選択には警告しない。
- #63: マーカーの欠損・可読な破損・読取不能を区別する。可読な破損だけ明示確認後に隔離し、既存のprivate rootを初期化する。隔離/削除/新規鍵・DB/マーカー書込/隔離回収失敗では利用を止め、後続試行に証拠を保持する。期間不明の停止ではOS通知を消さず、確認済み初期化でリセットする。

UI変更前にAppViewModel / MainWindow / Retentionを一覧と検証記録から検索した。既往のchange-row-exclusion-verificationのチェックボックス表示領域・設定選択・テストimport不足、およびpost-release-issues-verificationの実ライフサイクル/通知保持の記録と実コードを照合した。復旧画面の実操作失敗の既往記録はない。新規UIケースは独立した架空rootで確認のキャンセル・確定・実初期化・設定/公開cache/原本保持・再起動を操作し、元の導入テストrootには書き込まない。既存の画面操作・導入検証は維持する。

## ローカル結果

Debian13 / .NET10.0.402。W17の記録/修正push後、復旧store5件成功。実AppViewModelを使うライフサイクル19件も成功し、破損の3回再試行中は旧私的表示を隠しOS履歴を保持、確認済み初期化後の新operationによる再読込と再起動を確認した。Core全205件・Integration全221件は失敗/skip0。WinUIテストのLinuxクロスビルドはエラー/警告0で成功したが、Windowsの実画面操作成功には数えない。

P01の利用者資料はまだ未受領。10454は利用者も未確認で、もう少し前の版では動いたとの報告。実PDFの採否や拒否枝は未確定のため、P01の解析条件はこの差分では変更していない。

一覧146ファイルの整合検査、一覧自体の14テスト、配布notice検査は成功。作業環境の短い接続断ではコマンドプロセスが作成されず、復旧後の同一ファイルと成功ログを確認した。テストの失敗/再実行とは区別する。

## Windows PR の確認

[PR17](https://github.com/n624-dev/takupoke-win/pull/17)、source4921c6bの[run39906933933](https://github.com/n624-dev/takupoke-win/actions/runs/39906933933)は05:42:25 UTCまでに全7job成功。Windows Core205件・Integration223件、Linux205/221件、固定Swift比較、x64 Debug/ReleaseとARM64 Releaseの製品ビルド、実WinUI435項目が成功した。追加の復旧キャンセル/確定・再起動も実行済み。05:44にmainを同一sourceへfast-forwardした。main側Actionsと正式パッケージの導入検証・匿名配布物の照合は別に確認する。利用者P01の解消証拠ではない。

## W18: 標準Device色空間の同値表記を拒否する架空PDF回帰

2026-10-23、利用者は対象PDFを共有できないと回答した。Debian13 / .NET10.0.402、source f4e9169 を基点とするcodex/fix-device-colour-p01の追加テスト差分。`dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~StandardDeviceColourSpaceOperators` の3件がすべて失敗。実フォントを埋めた完全架空の同じ時間割で、黒文字の表記だけを /DeviceGray cs 0 sc、/DeviceRGB cs 0 0 0 scn、/DeviceCMYK cs 0 0 0 1 sc に変更すると、Readの45行目でP01を返した。ログ/tmp/win-device-colour-regression-first.log。利用者PDFの原因と同じとは未確認。対処方針を決める前にこの観測を保存する。

008b108の実Readはこれらの色指定命令を無視していた。公開後の可視性拒否を追加したe001729では、標準Device色空間でもcs/sc系を一律未確認として拒否するため、旧入力との同値表記が新たにP01となる。W15のテスト引数不足によるコンパイル失敗と異なり、今回は実Reader→Parserの採否回帰。候補は標準DeviceGray/RGB/CMYKの色空間・初期色・成分数・q/Q復元・直接色指定の暗黙切替を追跡すること。未知の色空間・Pattern/ICC・不正成分・白地白文字・後描画は拒否を維持する。既存の背景/描画順/透明度/原本保持条件は外さない。テストと記録を隔離ブランチへpush後に再試行する。

## W19: 色空間修正後の既存拒否ケースとの不一致

2026-10-23、Debian13 / .NET10.0.402、sourcef6797cb、`--filter FullyQualifiedName~PdfVisibilityTests`。42件中41成功、ColourValidationPreservesCancellationAndRejectsUnverifiedColourSpacesだけが134行のAssert.Throwsで「例外が無い」と失敗。ログ/tmp/win-device-colour-regression-second.log。3件のW18入力は成功した。失敗した入力と旧契約を読む前に対処を決めず、この事実を保存する。

W19の旧e001729の拒否リストには標準DeviceRGB自体が含まれていた。今回の同値表記の受理仕様と矛盾するこの入力を、引き続き未確認のPatternへ置き換える。取消・範囲外成分の拒否、3種類の白文字・線描画・成分数・未知空間・q/Qの新検査は維持する。通常解析版を9→10に更新し、同じPDFの旧失敗が現在版の結果と見なされず再解析対象になることを既存freshness経路と版9拒否assertで確認する。特別資料の版は維持する。修正/記録push後に全検査を再実行する。

W19修正後、Core205件・Integration235件（可視性42件を含む）がDebian13 / .NET10.0.402で失敗/skip0。通常版10、特別版不変、一覧146ファイル整合も成功。独立PyMuPDF描画ではGray/RGBの同値表記は0 gと完全一致。CMYK黒は0 gと同じ画素ではなかったため、CMYKの直接k命令との比較を別に確認し、色空間をまたぐ画素一致を通過根拠にしない。利用者の実PDFは未受領で、そのP01解消を保証しない。
