# iOS Issue 41〜43との同種不具合の確認（2026-10-22）

基点は`944662b277f7d8eb31e3ce66f6a447c822144561`。学校資料・本番通信は使わず、完全架空データで検証した。

- iOS #43と同じくSchedulePresentation.OriginalSlotsがGridPeriodsを参照していた。詳細用DetailPeriodsへ変更し、1・3限だけの通常／試験／返却授業の参照と空欄の変更前補完を追加検査した。週グリッドのGridPeriods判定は変更していない。関連変更の詳細表示は元からDetailPeriodsを使っている。
- iOS #42と同じく、保存した授業時刻の復号後の型変換失敗が公開revision確認・認証後の取得を阻んでいた。保存内容の変換失敗をDB・鍵・I/Oの失敗と区別し、授業時刻だけを未取得として再取得可能にした。読み込み時には削除・書換えをせず、認証・応答検証・DB保存を完了してから置換する。画面の再読込みも同じ検証済み読出しを使う。
- iOS #41の別ファイルPDF回収はWinにない。Winは原本を暗号化BLOBとして同じDBに保存し、同じ保存期間内のDB欠損時は既存のEnsurePeriodAsyncが停止する。欠損DBを再作成せず残る鍵を保持する回帰を追加した。半期切替の既存削除仕様は変更していない。

テスト一覧をSchedulePresentation・SharedDataUpdater・SchoolDataStore・AppViewModelで検索した。新しい回帰を既存ネイティブテストへ追加し、一覧を再生成、`check --base 944662b`を確認した。

Debian13/x86_64、.NET SDK10.0.402でCore184件・統合149件が失敗0。後者には破損内容の復旧、認証中止、公開確認失敗、取得失敗、SQLite triggerによる保存失敗、破損DBの停止と保持を含む。Pythonの一覧14件・配布通知13件、公開tree／noreply検査、差分空白検査も成功。WinUI実操作・Windows固有ビルドはこのLinux結果に含めない。Actions結果は別途追記する。

## Windows CI初回の回帰テスト準備失敗

source`ccd11ab`、run39702647850／試行1、Windows2025・.NET10で、追加したFailedTimeRequestPreservesCorruptCacheAndDatabaseFailuresStillStopがschool.sqliteのファイルロックによるIOExceptionで失敗した。最終スタックは所有一時ディレクトリの回収（同テスト77行）であり、DB破損確認の成否はこの結果では確定できない。Core184件は成功、統合は1件失敗。Linux側とWinUI x64 Debug／Release・ARM64のビルド／既存操作は成功した。

対処前に記録。`pool|sqlite|IOException|school.sqlite`で既存documentationとstore・テストを照合する。テストのtrigger設定用SqliteConnectionだけが既定poolを使い、製品SchoolDataStoreはPooling=falseで開く条件の違いを調べる。検証条件を削除せず、同じ失敗後保持とDB破損停止をWindowsでも実行する。

change-row-exclusion-verification.mdの既存「SQLite接続の既定プールが削除を妨げた」記録と一致した。今回も製品接続はPooling=falseで、追加したtrigger設定用接続だけが既定値だった。この所有接続をPooling=falseにそろえ、DB破損の作成・例外・bytes保持のassertionをすべて維持した。製品コードは変更しない。修正後のWindows結果はまだ未確認。

## 追加Issue 45・46の実装中Linux検査（2026-10-22）

基点f300e90からの作業差分、Debian13／.NET10.0.402。追加Issueの時限・文脈判定を変更した段階で既存Core検査を実行し、184件中11件が失敗した。成功173件、コンパイルは成功。詳細ログを読む前に観測を記録した。対応する実装・固定iOS参照・独立期待値を照合し、仕様修正に必要な差か、別の回帰かを調べる。期待値を一律変更しない。

11件の失敗は、全て固定iOS参照の`changes-false-special-3`（試験と返却が同時に存在する各行事タグ）であり、差は両カードのtimeだけだった。元のSwift fixtureと生成元hash・再生成照合は変更しない。比較の期待側で対象11IDを明示し、元の2カードがexam／examReturn・time=nilであることも検査してから、独立した確定期待値09:10〜09:50／09:10〜10:00を要求する。C#結果から期待値を生成せず、その他の全フィールド・共通欄・変更時刻も引き続き固定参照へ完全一致させる。追加の独立回帰は別時刻・並び順・単独／同時・同時刻・結合・時刻欠落・授業中を検査する。参照差を隠すためのskipや実結果の正規化は行わない。

## 追加Issue 45・46の修正内容

- #45：共有の表示判定へ変更日由来の学校年度とクラスを渡し、文脈付き教員名の確認済み括弧だけを分離する。Win側は非表示変更の影響で通常／特別授業を抑制しないよう、置換前のchanges絞り込みも接続した。元のSlotと原文は保持する。3月・別年度・別クラス・未確認括弧・一般教員規則・OFF／ON・変更一覧と通常授業の保持を回帰で検査する。
- #46：単独特別カードは保存された自分の時刻、結合カードはkindと元授業が一致する解析に限定した各コマの時刻を使う。欠落を別資料から補わず、共通欄と変更カードの曖昧さは維持する。単独／同時／逆順／同時刻／異なる時刻・結合・時刻欠落・授業中を独立した架空入力で検査する。

LinuxのCore187件・統合149件は失敗0。固定iOS fixture・生成元・hashは未変更であり、追加仕様に一致するカード時計だけ明示した期待値を使う。Swift原参照の再生成照合は既存CIで維持する。追加分のWindows CIはこれから確認する。

### 2026-10-22 追加Issue 初回ローカル失敗（対処前記録）

- source ebe0848 からの未コミット追加Issue差分、Debian13 / .NET10.0.402、Core test初回（ログ /tmp/additional-win-core.log）。195件中193成功・2失敗。
- PartialStartupPreservesPersistedPendingButConfirmedAbsenceInvalidatesIt はJSON再読込後のPendingNotice dictionary比較で、同じ値のChangeTargetsがListとarrayで異なるとされた。観測内容を先に記録し、原因・対処はまだ確定していない。
- ConflictingOverridesWarnAndUseRealWeekdayWithoutLosingOtherTags は警告一致後、BlocksのAssert.Singleで0件だった。他タグとしてSupplementaryを同時に与えた条件。対処前に現行・過去仕様を照合する。次の試行前に根拠と修正をpushする。

- 既存検証記録全件とebe0848のSlot/NotificationDiffTestsを照合した。Supplementaryは従来から通常授業を抑制する確定仕様で、振替競合のfallbackがこのタグを無効にする仕様ではない。回帰では行事メモを併記して実曜日授業を確認し、補講日併記では通常授業が抑制され警告・タグが残ることを別に要求する。
- PendingNoticeはIReadOnlyListを含むrecordなので、再読込で生成されたListとreconcileのarrayはrecord参照比較では等値にならない。件数・キー・fingerprint・count・全target列をそれぞれ検証する。期待する保留3要素等を減らさず、製品変更でテストへ合わせない。

### 追加Issue ローカル第2試行の観測（対処前）

source63e801a、Debian13/.NET10.0.402。Core第2試行は共通Takupoke.Core参照assemblyのcopy段階でIOExceptionになり、テストを開始していない。同じ作業ツリーでCoreと統合を同時buildした条件だった。統合初回は158件中157成功、PreviouslyAcceptedOsNoticeIsAcknowledgedWithoutResendingが送信済みexamのpending保持（期待は空）で失敗。新しい読込確認条件に対しテスト入力にSpecialsがない。まだ原因・対処を決めず記録した。

- ebe0848の通知処理・既存テスト全文とsource-refresh-pending-policyを照合。以前の送信済み通知テストは読込状態のない空入力でpendingを処理していたが、#50では未読込は保留し配信時に現在digestを確認する必要がある。テストに当該examの検証済みdigestを与え、OSへの再送0・pending消去の両assertionを維持する。追加で3種の読込順全6通りを暗号化SQLite再読込経路で検査する。
- Core再試行のIOExceptionは共有objへの同時buildと一致し、製品処理開始前。今後は同じ参照出力を使う.NET buildを逐次実行する。既存SQLite接続poolの失敗とはpath・段階が異なる。製品やテスト条件を変えず、修正記録push後に逐次試行する。

### 追加Issue47〜58の修正と検証

- #47/56：変更内容をChangeKindへ保存し備考と分離する。OOXML escapeをshared/inline/richのt要素で一回だけ展開する。旧保存形式のNote fallbackは保持しparserVersionを6に更新。元の固定fixture/hashは変更せず旧フィールドと新規仕様の独立期待を検査する。
- #48/53：不正な非空年度は補完しない。coordinatorの全入口で変更の取得前に拒否し、既存SQLite内容を保持する。Home・週・詳細の鮮度判定をdigest/version/現在年度/除外同意へ統一する。WinUIは元から不正な年度draftを保存しないが、破損した保存設定と直接呼出しも防御した。
- #49：結合セルから残ったfragmentは、その元解析の各時限時計だけを使い、欠落時は不明にする。元セル全体の時計・原文は保持する。
- #50：Winは元から全資料をまとめてロードするためiOSと同じ部分起動の消失はなかった。ただし入力が未読込のときの古いpending配信と、明示的対象削除を区別するためavailabilityを追加した。暗号化SQLite再起動・全6読込順・現在対象確認・3種の一回配信を検査する。
- #51/52：クラス規則を一般exact aliasより優先する。確定した部屋だけを一段分離して教員文脈を確認し、末尾の部屋／教員の順序の両方を扱う。検証済みZIPから時間割合成までON/OFF・別クラスを検査する。
- #54：競合時は振替を適用せず、実曜日の暫定時間割と他タグを保ち、Home/週へ合意済み警告を表示する。
- #55：不正なmapping payloadだけを復旧可能として扱い、鍵・DB・I/O失敗は従来どおり停止する。レビューで見つかったnullのteacher-context classNameも型検証で拒否する。認証取消・保存前bytes保持・検証済みZIPによる置換を実DBで検査した。
- #57/58：WinのSourceWatcherには版の先行ackや同一Sourceの再登録省略はない。既存の有界debounce・取消を維持し、欠損フォルダー復旧後の同一path Replaceで監視が戻る実FileSystemWatcher回帰を追加した。

一覧を機能・ファイルで事前検索し、write/check --base ebe0848を実行。Linux/.NET10.0.402のCore195件・統合166件が失敗0。Python一覧14件・配布通知13件・公開tree検査も成功。新規GPT-6.1 Sol/Highによるコード差分だけのレビューでnull入力の指摘を修正し、修正差分の再確認もblockingなし。Windowsビルド・UIは次のCIで確認する。

### Issue59 初回ローカル検査（対処前）

基点5bbca48からのIssue59作業差分、Debian13/.NET10.0.402。Core195件中151成功・44失敗、統合168件は成功。Coreの失敗は固定Swift参照比較の時間欄で、具体的な差とfixture生成元はこれから照合する。対処前に観測を記録し、期待値・fixtureを一律変更しない。次の試行前に根拠と修正をpushする。

44件はすべて返却を含む固定参照ケースで、追加情報のない旧入力を「元PDFの先頭列不明」とする新しい安全条件により時計がnilになった差だった。ExportParity.swift:37〜40を全文照合すると、このfixtureは元資料解析のキャッシュではなく、2032-04-05を専用日として直接構築した2日分の架空モデルである。固定fixture・生成元・hash・全期待値は維持し、この既知の構築条件を入力adapterで明示する。日付列が2つの固定日であることもassertし、実出力やexpectedから値を生成しない。旧キャッシュの推測禁止は別の独立回帰で要求する。

### Issue59 修正と確認範囲

Windowsにも同じ日付sort後の専用日取り違えと保存済み時計の上書きがあった。元列順の先頭日をDedicatedTimeDateへ保存し、PDF特別解析版を7へ更新した。受理条件は変更しない。元列順[2,1,3,4,5]と[1,2,3,4,5]で各5日×8限、JSON再読込、変更の空枠、結合カード・進捗・fragmentと原文保持を検査し、実暗号化SQLiteの再起動後も同じ日付・時刻を要求する。旧キャッシュの不明な先頭列は推測せず、保存済み範囲は保持する。

修正記録push後のLinux/.NET10.0.402はCore196件・統合169件が失敗0（共有参照buildは逐次実行）。一覧check --base 5bbca48と公開tree・noreply検査も成功した。固定参照のfixture・生成元Swift・hash・期待値は変更していない。前版5bbca48のCI39742410763は全7成功（Windows Core195・統合168、Linux Core195・統合166、WinUI操作とx64/ARM64 build）。Issue59追加後のWindows CIはこれから確認する。
