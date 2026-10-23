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

差分の自己確認で、単一subpathが同じ矩形を2周する場合も不透明背景と見なせてしまう余地を見つけた。even-odd fillでは塗りが相殺し得るため、矩形は4頂点＋任意の終端閉鎖1点だけを許し、再走査するpathは未確認として拒否する。テスト失敗からの期待値変更ではなく、入力独立の穴あき背景回帰を追加した。進行中のActionsは取り消さない。

## PR・main の最終結果と公開状況

- 最終製品ソース: e001729aee0afadde59032d42ed5c11e015e4bc0。
- [PR #15](https://github.com/n624-dev/takupoke-win/pull/15) は2026-10-22 16:02 UTCにmainへ反映され、merged=trueをAPIで確認した。#4–14の11件はクローズされた。
- 最初の [PR CI](https://github.com/n624-dev/takupoke-win/actions/runs/39808892974) と、補強後の [最終PR CI](https://github.com/n624-dev/takupoke-win/actions/runs/39809448474) は両方とも全7ジョブ成功。実行を取り消していない。
- 最終PRのログではWindows Core199件・Integration215件、実WinUI操作406チェック成功。x64 Debug/Release・ARM64 Release、Linux、固定Swift参照も成功。
- [main CI](https://github.com/n624-dev/takupoke-win/actions/runs/39810426324) も全7ジョブ成功。16:02:18開始、最後のx64実画面ジョブが16:05:09に成功。16:07 UTCの一括確認で全完了を照合した。
- 16:07 UTC、両リポジトリのopen issue一覧をAPIで取得し、未対応issueが0件であることを確認した。
- GitHub確認は完了実績から間隔を決め、複数実行を一括確認した。最後のPRは約3分、mainも約3分だった。

0.1.0-dev.10454は前回公開済みで、今回の追加4件はまだ配布版へ含まれない。次版は `Development installers` を `main` / `version=0.1.0-dev.10455` で起動する準備まで完了した。公開ゲート・導入検査を省略していない。

環境のGH_TOKENによるgh API呼出しがHTTP 401となった（トークンの値は出力・転記していない）。GitHub連携でコード反映・PRマージ・検査確認は継続できたが、連携にはworkflow_dispatchの開始機能が無い。環境には代替のGitHub認証設定が無く、公開Actionsは未起動。環境認証の更新または利用者によるGitHub画面からの起動を依頼した。401の原因を期限切れ等と断定しない。認証が復旧したら既存の全検証と導入検査を伴う公開経路を使用し、公開後の匿名ダウンロード・全ハッシュを確認する。

### 公開の再開（2026-10-22 22:22 UTC）

利用者から継続指示を受け、同じ環境のghでGitHub認証が成功することを確認した。mainは引き続きe001729、Latestは10454で、未公開の10455を既存のDevelopment installers経路から起動した。
[公開実行39864446227](https://github.com/n624-dev/takupoke-win/actions/runs/39864446227) の元ソースもe001729である。前回は検査開始から公開ジョブ終了まで約18分だったため、22:42 UTCを次の一括結果確認の目安とした。22:24 UTCには両リポジトリのopen issueを1回のGraphQL呼出しで照合し、どちらも0件だった。

### 10455 公開と匿名照合の完了

[0.1.0-dev.10455](https://github.com/n624-dev/takupoke-win/releases/tag/v0.1.0-dev.10455) を2026-10-22 22:38 UTCに通常Release/Latestとして公開した。配布元はe001729aee0afadde59032d42ed5c11e015e4bc0。上記の認証障害による未起動状態は解消した。

公開実行39864446227は全8ジョブ成功（取消しなし）。Windows Core199件・Integration215件成功、x64 Debug/Release・ARM64 Release、Linux、固定Swift参照も成功した。インストール後の実画面検査は6回とも406チェックが成功。導入先の変更・同フォルダーへの再インストール・dev.5からの実更新・自動移行・無関係ファイルと設定の保持・アンインストール回収を確認した。ARM64実機・学校アカウント/実資料・物理Windowsロックと実トーストの確認は未実施。

22:42 UTCの結果照会で全8項目の完了をまとめて確認した。公開起動確認後から予定時刻まで、Actionsの状態照会は行っていない。前回所要約18分に対し、今回の実行は22:22:19〜22:38:39（約16分20秒）だった。

全6添付を認証なしで取得し、GitHubのsize/digestとSHA256SUMSを照合した。ZIP内の版・ソース・アーキテクチャ、x64の561ファイル、ARM64の557ファイルの全ハッシュを確認した。重複・相対逸脱pathや学校資料/ソース/ログ/鍵の混入も拒否した。取得用一時領域は自動回収した。

| 添付 | bytes | SHA256 |
| --- | ---: | --- |
| INSTALL.txt | 2916 | de1c31d71be1fc62f64d83a6054f49f85cac150e2df74b3458b9e3ba21e896ed |
| SHA256SUMS.txt | 495 | cf4e26e1c1a5408a871d9b471e6afcd9fa6e88e0ae6d1cdd76073685d2fd3eb2 |
| takupoke-0.1.0-dev.10455-arm64-Setup.exe | 68274781 | ef960357c432ba916509efc7cb9f99617859541b8b351a88a6a3492082dfcee9 |
| takupoke-0.1.0-dev.10455-arm64.zip | 100434051 | e7fe6b155aba59de89cc394e4cceea6653a661b45d5c34c2fbb5c0be015f8b9e |
| takupoke-0.1.0-dev.10455-x64-Setup.exe | 70826512 | 9d2baa6f83211263efccd456c684862e33a07faedf7dbf31a2df0d2fde5b236f |
| takupoke-0.1.0-dev.10455-x64.zip | 103011732 | ed7e9c15d902bfd5f7c5c2f281e52bce93ff83068f3f001601971fe4f511cbff |

公開後に両リポジトリのopen issueを1回のGraphQL呼出しで照合し、どちらも0件だった。
