## W12 第2焦点CIも登録検査でfailure終端（2026-10-19）

- sourcec23138c6d8d2bdee006c2d02238f368ce7ac01ce、run39152217286/attempt1、job117748003013。18:01:09Z開始・18:02:07Z failure。公開ファイル／登録stepで失敗、build/browserは未到達。完了ログ取得前で詳細未確定。W11とコードを照合してから対処し、検査を省略しない。

- 完了ログSHA3ecbcae4f122639cc9131039b1e395a4d821118afb2a58389373c08be99eeaa0。公開履歴は成功、catalogの日本語対象検索printでcharmap codec encode error。W11のobsolete判定は通過したがWindows既定出力encodingで止まった。既存Linux側CIとworkflowを照合し、焦点jobへPYTHONUTF8=1を明示する。登録判定・日本語出力内容は維持する。

## W11 ブラウザ焦点CIが1分以内にfailure終端（2026-10-19）

- source8b36c72ea0bd4be43ed698516f0f68cbe2cb7451、run39151749175/attempt1、job117746647779。17:55:00Z開始、17:55:59Z failure終端。初回メタデータ確認17:56:19Z。
- 完了ログ取得前で失敗step/原因は未確定。正常終了・証明書準備成功とは判断せず、失敗索引W10と当時のコードを照合してから次を決める。公開ゲートや証明書検証を緩和しない。

- 完了ログSHA0361b9a93f2510053f085b054f56869372127d54beb2de676c23d2c648dad696を照合。全19一覧が「obsolete」となり、ブラウザ起動前のtest_catalog.pyで停止。実ファイル相対パスがWindowsではbackslash、生成キーはPOSIXのため不一致。既存obsolete拒否ケースと実装を照合した。
- 次は一覧照合の相対パスをPOSIXへ統一し、Windows形式でも既存一覧を受理・未知一覧を拒否する独立回帰を追加する。catalogチェックを省略しない。W10の証明書候補は未評価のまま。POSIX統一後、ローカルPython15件とcatalog write/checkが成功。新規Windows形式回帰は既知受理・未知拒否の両方を確認した。

## W10 Windows10450実UIジョブが20分の上限で終了（2026-10-19）

- source1192ce929a938d592d9331cc6727fe578486fcee、run39149590379/attempt1、job117740375064。17:26:47Z開始、17:46:52Zにcancelled終端。20分job timeoutによる終了で、主担当はcancelしていない。
- WinUI app build成功後の「Check native UI navigation and saved settings」で終端した。具体ケース/操作/原因は完了ログ取得前で未確定。ほかの7前段のうち6成功、packageは依存skippedで公開なし。時間切れを成功と扱わない。
- 10451(run39150336346)はこの失敗が判明する前にcapability追加で既にpendingへ入っていた。取消禁止のためその実行も終端まで追い、同じ条件の自発的な無変更再実行はしない。次の修正を決める前に完了ログ・過去のUI索引/コードを照合する。

## W09 HTTPリンクの配信側拒否を追加確認（2026-10-19）

- Issue #2のcross-repo照合で、API src/links.tsもHTTPを拒否していることを確認。アプリだけのURL受理変更では直接HTTPリンクを配信できない。
- APIを単純に許可へ変えると旧iOS/Webが一覧全体を拒否し得るため、対応Windowsが明示するcapabilityで表現を分ける。revision確認と本文取得は同じ表現へ結び付ける。10450のCIは取消さず、追加修正の同一commit検査と10451配布へ進む。

## W08 ブラウザ架空サーバーのcross buildでSystem.IO import不足（2026-10-19）

- Linux/.NET10.0.402、BrowserFixtureServer.cs追加後のUIテストcross buildがCS0246(Stream/StreamReader)・CS0103(IOException)で停止。実ブラウザは未実行。既存ファイルはSystem.IOを明示importしており、このファイルで不足していた。
- 同じビルドを無変更で繰り返さず、System.IOの明示import追加を比較する。期待条件や本体のPDF経路は変更しない。

## W07 Issue #2 ローカルブラウザの無言拒否（2026-10-19）

- 利用者の追加依頼でopen Issue #2を読んだ。現行HEADでもURL検証・内部/外部選択・WebView2遷移・popupはHTTPS限定で、HTTPを無言で取り消す。HTTPSの実際のWebViewエラーは未採取、証明書エラーとpopupは候補。
- 過去の索引にはWebView2ローカル遷移の該当記録なし。DocumentsのPDF表示メソッドは対象外。ローカルHTTPの範囲を限定した共通policy、拒否/完了/証明書の可視化、別WebViewでのpopupを検証候補とする。TLSを無条件許可しない。

## W06 公開前レビューでcache期限の起点を検出（2026-10-19）

- レビュー49: expires_inをJWKS検証後の時刻へ加算しており、JWKS待ち時間だけ期限を延長する。発行300秒・待ち60秒の場合、310秒後も再利用できる。Actions未開始。token要求前の時刻を起点とする候補と遅延時計の実交換検査で確認する。iOSと同様、新規の不足権限認証開始時には前cacheも破棄する。

## W05 本番OIDC認可開始の環境HTTP403（2026-10-19）

- 認証506278c本番反映後、iOS/Windowsのlinks-only standard認可開始GETが各1回ともHTTP403。秘密値・学校資料は送っていない。登録の成否やログイン成功の証拠にはならない。
- 同条件を反復せず、サーバーのCI/production成功、過去の登録確認、実機未確認を分ける。詳細はiOS N45と今回の検証記録。

# 今回の検証失敗の記録

## W04 所有の境界照合スクリプトがbytesリテラルで構文停止（2026-10-18）

- source6fb7b98の読み取り専用ローカル照合で、未使用の日本語bytesリテラルがSyntaxErrorとなった。
  製品・テストの実行失敗ではない。直後のgit pushは別コマンドとして進み、配布branchを送信した。
- 対処前に記録。コードレビュー42は終了し、対象2件・Integration1162・Python14／13と、
  作業差分・index・送信する単一commitの空白検査はこのスクリプトの前に成功していた。
  未使用の記述だけを除き、同一6fb7b98の差分・Strict外境界を読取りで照合する。
  製品・期待値・CI条件は変更せず、開始済みActionsを取消さない。
- 記録をpushした後、同じ6fb7b98のコード差分SHAと、OCR取得メソッド外のViewModel本文を照合した。
  共通RunAsync・Strict経路・モデル重み／URLは不変で、レビュー済み差分と一致した。
  継続メモ追記の引用符でも一度構文停止したが、製品・検査には到達せず、メモの記法だけ修正した。

## W03 Pythonの検査探索先を誤り0件で終了（2026-10-18）

- 1349641上のOCR設定修正差分で `python3 -B -m unittest discover -s scripts -p 'test*.py'` が
  0件・NO TESTS RAN・exit5。公開履歴検査は成功し、OCR許可の.NET回帰2件も成功している。
- 対処前に記録。scriptsのtest_catalog_inventory.pyは一覧生成用でテストケースではない。
  既存CIとテスト一覧に記載された実際のPythonテスト配置・コマンドを確認する。
  0件を成功に数えず、製品・検査条件は変更しない。
- 既存CIのtests/test_test_catalog.py・tests/package-notices-tests.pyを確認して実行し、14件／13件が成功。
  誤ったscripts探索の0件は加算しない。検査・期待値を変更していない。

## W02 Windows OCR更新・削除後の再導入が中止表示となる実機報告（2026-10-18）

- 利用者報告：OCRを更新すると「処理を中止しました」。削除は成功するが、その後の導入も失敗する。
  版・設定状態・経過時間・進捗段階は未確定。調査checkout1349641、公開10448のモデル管理経路を対象とする。
- 対処を選ぶ前に記録。学校資料は取得しない。モデル取得・設定許可・操作取消・保存の各条件と過去記録を照合する。
  表示だけで利用者による取消や通信の時間切れとは断定しない。Strict・入口・共有解析は変更しない。
- 利用者が「AI・OCRを使用する」はOFFだったと確認。削除はOFFでも許可する既存仕様である。
  `BuildRecoveryModels` はダウンロードを一度無効にするが、直後の `Render → UpdateStatus` が
  登録済みoperation controlを一律有効にする。OFFの取得は `AiFeaturePermission.Check` で中止される。
  10448の名称表示変更ではこの経路を変えておらず、既存のOFF拒否テストは汎用「中止」だけを期待し、
  WinUIのモデル画面テストはボタンの存在しか検査していなかった。
- 次はOFFの取得ボタンを共通の有効化対象へ登録せず、取得条件の案内を表示する。
  直接の取得呼出しもOFFなら理由を明示する。ONへの切替後は取得入口を有効にし、OFF時の削除を維持する。
  共通UpdateStatus／RunAsync・Strict・全入口／Parser・モデル重み・取得URLは変更しない。
  実UIのOFF→ON切替・更新表示後の無効状態と、許可前にruntimeへ届かないことを検証する。
- 10449／6fb7b98の同一実行39102835131・attempt1の全8チェックが成功し、通常Releaseを公開。
  実UIでOFF→ON・ボタン無効／有効を確認し、公開6添付も匿名で全ハッシュを照合した。
  利用者からは現インストール版でONへの切替後に取得が進むと報告。版番号・取得完了は未確認。
  [修正と確認範囲](ocr-model-opt-in-20261018.md)を参照する。

## W01 ローカルの全参照履歴検査で作者規則違反（2026-10-18）

- bb4250aに正式名称表示の未コミット変更を載せたLinux checkoutで、
  `python3 -B scripts/check-public-tree.py` がnoreply以外のcommit identityとしてexit1。
  Core301・Integration1161・一覧14件は成功済み。新規Actionsは開始していない。
- 対処前に観測事実を記録。検査は `git log --all` を対象にする。
  最新配布branchへの切替で主担当の差分だけを保存したstashがローカルに存在する。
  違反参照の種類と既存履歴を照合し、製品・検査条件・remote履歴は変更しない。
- 違反した2commitは主担当が今回作成した単一stashの本体とindexだけだった。
  remoteの製品履歴には該当せず、適用済み3ファイルの差分は別の所有patchにも保存済み。
  repoローカルの作者設定をnoreplyへ設定し、適用済みの所有stash参照だけを片付けて再検査する。
  検査・既存commitの書換えは不要。今回の配布sourceは10447の製品を継承する。
- 原因照合後、所有stash参照だけを削除すると同じ公開履歴検査が成功した。
  remote履歴・製品・検査条件は変更せず、所有patchと未コミット差分も保持された。

### W10 完了ログ照合後の候補

元ログSHA d43699d95ab8e2fe5625913a1c0d1a8d03bea46698f9f2812a8ed54c1c9b8f1f。17:30:49Zに通常UI1059チェック成功、17:31:21Zに全画面capture完了。その後、新規browser-checkの進捗・例外出力が一切なく15分以上後にjob上限で停止。通常設定・認証取消・保存・訂正の失敗ではない。
新browser-checkは最初にCurrentUser Rootへ一時CAをX509Store.Addするが、開始段階の観測と操作上限がない。Windowsのroot追加確認に停滞する可能性を候補とし、実際の停止操作は未確定。過去の索引にはこのfixture処理の実行記録がない。
次はfixtureの各段階を記録し、既存の証明書検証を維持したまま、所有一時CAをcertutilの明示import/deleteで準備・回収する。補助プロセスは15秒で失敗を返し、停止を成功扱いにしない。ブラウザだけの焦点CIで切り分け、その成功後も正式全8・6導入状態を省略しない。検査期限の延長・UI押し直し・AlwaysAllowは行わない。
