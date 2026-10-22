# 公開後に追加された Windows issue の検証

## 対象と試行記録

初回公開: 0.1.0-dev.10454 / main 008b108ffc65738150aea76d3044e82f4cc743a2。
追加 issue #6、#7、#9、#10 を実装し、#4、#5、#8、#11–14 は公開済み修正との対応を再確認する。

### W15: PDF 可視性テスト初回のコンパイル失敗

- ソース: 008b108 を基点とする未コミット差分、`codex/fix-post-release-issues-6-7-9-10`。
- 実行: 2026-10-22 15:46 UTC、Linux Debian 13 / .NET SDK 10.0.402、`dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --filter FullyQualifiedName~PdfVisibilityTests`。
- 段階: コンパイル。テストは未実行。
- 観測: PdfVisibilityTests.cs の AnalysisMatches 呼出し2箇所が CS7036。必須 schoolYear 引数が無い。既存警告 CS8602 / xUnit2031 も表示された。
- ログ: 作業環境 `/tmp/post-release-pdf-visibility-first.log`。PDFの採否・描画結果についてはまだ判断できない。

W15 対処前に既存 MaterialCoordinatorTests の AnalysisMatches 呼出しと実装を確認した。
過去の change-row-exclusion-verification.md の共有fixture import不足は別のコンパイル失敗であり、今回の引数不足とは条件が異なる。
テストの入力年度2032を必須引数へ明示する。期待値・可視性条件・製品実装はこの修正では変更しない。
次の試行前に、この観測と修正を自動CI対象外の作業ブランチへpushする。

## 追加4件の実装と確認範囲

- #6: 自動更新のBusy判定で要求を失わず、処理終了後にdispatcherへ1件を予約する。ロック中・利用者の明示停止中は開始しない。実AppViewModelと実ファイル読取・暗号化SQLiteを通し、OSファイルidentity取得だけを境界で待機させる。トレイOFF/ON、lock→unlock→旧処理終了、旧処理終了→unlock、ロック継続、明示停止と再開、保存期間切替を検証した。
- #7: メモリ初期値から保存期間切替を推測せず、実SchoolDataStoreのRetentionChangedだけでOS通知履歴を無効化する。通知削除を直列化し、取消しがReload前に起きてもfinallyで削除する。OS受理タグを保持する通知sinkで起動から実Coordinator・SQLite保存までを通し、受理済み/未受理pending、OS受理後の実DB保存失敗、同じ半期の再起動、実半期切替と取消しを検証した。
- #10: 実SelectionChangedハンドラーを共通化し、await前に観測値を更新する。リンク、解析クラス2種類、解析曜日で初期/重複イベントと実選択を区別する。実SavePreferencesAsync・PreferencesStoreを使い、通知待ちの間の0→1→0と連続往復、保存失敗・コントロール再構築・再起動を確認した。
- #9: 通常時間割の描画順序、device Gray/RGB/CMYK、q/Q、塗り/線の描画モード、不透明な矩形背景を追跡する。同色文字、透明度・blend・カスタム色空間・クリップ等の未確認条件、文字の一部だけを覆う背景、後からの上塗りをP01で拒否する。白文字を削除して部分成功にはしない。通常時間割の解析版を8→9へ更新し、旧結果を確認済みとして流用しない。特別資料の解析条件・版は維持した。

`AppLifecycleTests` はOS境界（WinUI dispatcher/control、DPAPI、toast、browser）だけを代替する。製品の実AppViewModel・選択ハンドラー・store・Coordinatorをリンクする。実際のWindowsロックイベント、OneDriveの待機、トースト履歴そのものを操作した証拠には数えない。Windows Actionsで製品ビルドと既存の実WinUI操作も別途確認する。

UI変更前の検索は test-catalog の AppViewModel / WindowsNotifications / MainWindow.Settings / MainWindow.Analysis。過去の change-row-exclusion-verification.md のメインカラー往復（固定初期値guardの失敗とobserved値による修正）と当時の実装を読み、今回のリンク/解析選択にも残る同型のguardを照合した。新しい保存失敗ケースでは元設定ファイルを別名保存し、所有する試験ディレクトリだけを使用する。

### 実フォントと独立描画

`fixtures/fictional-noto.cff` はNoto CJKの公開フォントをfontTools 4.61.1で架空文字24字へsubsetし、CFF名称をFictionalNotoCJKSubsetへ変更した3922 bytes。元のCIDと幅を保持し、正しいToUnicodeを生成する。元URL・commit・SHA256・subset SHA256・手順は同梱JSON、OFLは同梱txt。CIはネットワーク取得・fontTools不要。学校資料・教師名等は含まない。

実Reader→Parserの黒文字対照が1件の架空授業として成功してから、白文字の拒否を評価した。PyMuPDFで同じ科目領域（x163..194 / y94..106、4倍、罫線除外）5952画素を独立描画し、黒文字の非白画素1174、白地の白文字0、有色背景上の白画素332を観測した。黒・白・青地白文字の画像を主担当が目視した。未対応フォントによる両方拒否を成功とは数えない。

### ローカル結果

- W15修正後のPDF可視性28件成功（Debian13 / .NET10.0.402、2026-10-22）。初回のコンパイル失敗は上記のまま残す。
- 実アプリ経路のライフサイクル/設定16件成功。初回13件から、取消し中の半期切替・連続往復等を追加した。
- Release構成の全体: Core196件、Integration213件、失敗/skipとも0。
- #4の通常・試験・返却×留学生変更の種別/表示ON-OFF/反映OFF/振替土曜と、#5の変更カード時刻・授業中開始/終了境界を追加確認したCore199件も成功。
- 実学校資料、本番通信、Windows実機、ARM64実機は未確認。

## 公開済み修正と重なる7件

全issue本文の再現条件と現行実装・以下の既存ケースを照合した。別の実装変更を重ねず、追加検査が必要な組合せだけを上記で拡張する。

| Windows issue | 公開済み修正 / 主な回帰ケース |
| --- | --- |
| #4 | iOS #45 相当。BlocksでvisibleChangesを先に確定。ContextualInternationalChangesUseClassAndSchoolYearBeforeReplacingOrdinaryLessons、HiddenInternationalChangesKeepOrdinaryAndSpecialLessonsIncludingSaturday（今回追加） |
| #5 | iOS #49 相当。ReplacedMergedSpecialUsesVerifiedClocksForRemainingFragments（今回、置換側のカードと開始/終了境界も追加）、MergedSpecialCardsUseOnlyTheirOwnSourceAndNeverBorrowMissingTimes |
| #8 | iOS #59 相当。ReturnDedicatedClockFollowsOriginalColumnAfterSortedDatePersistence、DedicatedReturnDateSurvivesEncryptedSqliteStoreRestart、LegacyReturnRangeRemainsVerbatimButUnknownColumnDateCannotSupplyPeriodClocks |
| #11 | iOS #47 相当。WorkbookKindIsIndependentOfRemarksAndEscapesDecodeOnlyOnce、WorkbookMakeupWithRemarksKeepsPriorityOverFollowingCancellation。ChangeKindを備考から独立、旧データは既存fallback |
| #12 | iOS #43 相当。DisjointChangeDetailsFindOnlyRequestedOriginalsWithoutChangingGrid。通常・試験・返却のDetailPeriodsを使用、GridPeriods仕様は維持 |
| #13 | iOS #45 相当。ContextualInternationalChangesUseClassAndSchoolYearBeforeReplacingOrdinaryLessons。対象クラス/学校年度の文脈付き教員、通常教員、未確認括弧、別年度/クラス |
| #14 | iOS #56 相当。WorkbookKindIsIndependentOfRemarksAndEscapesDecodeOnlyOnce。shared/inline/rich、CR/LF/CRLF、リテラル保護・surrogate・類似不正表現。一度だけ復号、原文保持、XLSX解析版6 |

iOSとの照合: ApplicationData.prepareは永続installedPeriodの実変更時だけresetForRetentionを呼び、SettingsViewはAppStorageへの直接Picker bindingで固定初期値guardを持たない。#6はWindows独自ロック/Busy経路。#9はWindows通常読取の未確認色命令への対応であり、固定Strict239の受理規則変更や共通実装の導入は行っていない。
