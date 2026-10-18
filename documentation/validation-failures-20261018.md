## W19 第8焦点CIがfailure終端（2026-10-19）

- source0233f269fc6bb3aaeb4a0c29997ff748cfe1e356、run39154739540/attempt1、job117755518389。18:34:09Z開始・18:36:27Z failure、実ブラウザstepで失敗。CDP比較版の初回。完了ログ取得前で接続/本文/popup/TLSのどこか未確定。対処前に記録し、過去索引/実コード/新しい段階観測を照合する。

- 完了ログSHA0291d775c78d9eb814c50ac3e21231dd54440eae8a1d65b58412d1b1a35c9bc2。HTTP2requests・native表示完了後、18:36:19のCDP attachはloopback ECONNREFUSED。8checks、本文検査前。成功した表示を誤読・TLS失敗とは扱わない。
- [Microsoft公式security規則](https://learn.microsoft.com/microsoft-edge/webview2/concepts/security#for-an-elevated-host-app-use-appropriate-override-flags)を照合。管理者High ILのWebView2はWEBVIEW2環境変数とHKCU overrideを無視し、HKLMのapp単位policyを許可する。今回の管理者CIと一致し、以前のaccessibility flag比較も有効化自体が成立した証拠はなかった。
- 製品コードへdebug hookを追加せず、使い捨てCIのHKLM AdditionalBrowserArgumentsにこのexe名だけのloopback CDP値を一時登録する。既存app値があれば上書きせず失敗、finallyは同値だけ削除/不在照合し、今回作成した空keyだけ回収。通常Startを元へ戻し環境変数補助は除去。TLS/実DOM/実popup/期限は維持し、ユーザーPCや配布アプリにはこのpolicyを作成しない。local cross build0警告/0エラー18.07秒、catalog整合成功、fresh62にblockingなし。

## W18 第7焦点CIがfailure終端（2026-10-19）

- source76b54294a66224f97039c3ecff15699cef00ebfc、run39154118138/attempt1、job117753670643。18:25:57Z開始・18:28:33Z failure。UIA3比較版の初回結果、実ブラウザstepで失敗。完了ログ取得前で段階/原因未確定。W17以前と実コード/ログを照合し、同条件無変更の再試行や検査緩和をしない。

- 完了ログSHA84656497b3699219bdbb6a41b9028b60c85519f8ae76b7e978fc7739cc67187b。UIA3でもHTTP2requests・status表示完了/error空、本文見出し30秒timeout/6checks。WinUI provider境界を越えてDOMを取得できない条件が残り、UIA2→3比較はこの症状を解消していない。既定flagなしの追加試行は根拠を失ったため行わない。
- 次の方針前にWebView2公式のWebDriver/CDPによる実browser自動化を確認する。特定のUIA tree取得を前提にせず実DOM/実popup/TLSを検証し、statusだけの成功には置換しない。

- [Microsoft Playwright公式WebView2手順](https://playwright.dev/dotnet/docs/webview2)のCDP接続で、起動済み実アプリのWebView2へloopback接続し、実DOMの可視見出し/クリック/実popupを検査する。失敗したUIA3依存とaccessibility強制flagは外し、Playwright1.63.0をtest-onlyへ固定。通常起動と製品にdebug接続を追加しない。
- 実popupは異なるnative windowと実CDP page、openerの元URL/本文保持、native child閉鎖後の同page終了まで照合する。TLS検証無効化/通信route差替え/Evaluate/期待値注入/別Chromium起動はしない。実クリック後の必須条件を各30秒で独立観測する。local cross buildは0警告/0エラー（最終6.90秒）、catalog整合成功。fresh61のnullable指摘も修正し、同じ開始前レビューのv2はblockingなし。

## W17 第6焦点CIがfailure終端（2026-10-19）

- sourcec9840de08dbea0ccd7264fff4ec6417a49b97df6、run39153660970/attempt1、job117752293411。18:19:53Z開始・18:22:05Z failure、実ブラウザstepで失敗。accessibility flag比較の初回結果。完了ログ取得前でHTTP/Popup/TLSどの段階かは未確定。W16と実コード/ログを照合し、期待・期限を緩めない。

- 完了ログSHA48c3e899a1e3b0aa5d89f0b40bb48bdcd7c37260035c0e80b0ce01746741fdf2。flag有効比較でも同じHTTP2requests・status表示完了/error空・WebView相当pane752×355にDOM childなし、30秒timeout/6checks。flagだけではこのUIA取得条件を変えられなかった。HTTP/TLS/popup完了と扱わない。
- 次の試行前にWinUI WebView2のAutomationPeerと実UIA検索（control view/Win32 bridge）、公式APIを確認する。同じ条件を無変更で繰り返さず、本文検査をnative statusだけへ置換しない。

- [WinUI公式AutomationPeer実装](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/WebView2/WebView2AutomationPeer.cpp)はCOMのWebView providerを橋渡しする。現在のSystem.Windows.Automationはmanaged UIA2で、[FlaUI公式説明](https://github.com/FlaUI/FlaUI)もWinUI系にはCOM UIA3を別に扱う。flagだけでは不足した実測と照合し、次はbrowser本文の取得/InvokeだけUIA3（FlaUI.UIA3 5.0.0）で比較する。
- 通常WinUI検査/ウィンドウ選択/閉鎖は既存UIA2のまま、本文の実見出し・実リンク/ボタンInvokePattern・popup保持・TLS拒否と全待機は維持。自動座標ClickやExecuteScriptへ置換しない。依存はテストprojectだけでapp配布へ混ぜない。DOMが取得できなければ失敗のまま。

- 同じコード照合で、既存Invoke(AutomationElement)がmainwindowの_windowへ再検索するため、別browser windowのcontrolを操作できないテスト側の対象範囲不具合も確認。新browser操作は取得した実controlのInvokePatternを一回呼び、mainwindowへ再検索しない。実操作・失敗判定を維持する。cross buildは0警告/0エラー、catalog整合と配布Python13件が成功。fresh60はUIA3/直接Invokeと旧artifact削除のコード差分にblockingなし。

## W16 第5焦点CIがfailure終端（2026-10-19）

- source6edbb90c30fd32fabcef0e1e7128570a0dab9978、run39153263397/attempt1、job117751121648。18:14:45Z開始・18:16:49Z failure。診断追加版の初回結果。完了ログ取得前で詳細未確定。W15のHTTP見出し失敗と同じか、画面/サーバーの実測を照合してから対処する。

- 完了ログSHA6d38fe62d26ac61fae6d0a7c9803039815188fff73b41ef17a563e2cdde1f35b。CA成功後HTTP serverは2 requests到達、ブラウザstatusは「ページを表示しました。」、error空。実browser frame768×519、WebView相当pane752×355はoffscreen=falseだがDOM childがUIAへ現れず、HTTP見出し30秒timeout（6checks）。network/初期化エラーを示す証拠ではない。
- W15の推測修正をせずbrowser状態を追加観測した結果。次の方針前にWebView2/Chromium accessibilityの公式資料と実UIA取得条件を照合する。実本文/実popup操作の検査をstatusだけの成功へ置換しない。

- Microsoftの[WebView2 browser flags](https://learn.microsoft.com/microsoft-edge/webview2/concepts/webview-features-flags)とChromiumの[実装](https://chromium.googlesource.com/chromium/src/+/HEAD/ui/accessibility/accessibility_switches.cc)を照合。rendererのaccessibility treeはscreen reader検出時に有効化され、検査用force-renderer-accessibilityで明示有効化できる。今回のUIA空paneと整合する候補であり、描画失敗とは断定しない。
- 次はbrowser-checkで起動する所有app子processだけに同flagを与え、実HTMLの見出し/リンク/popupを元どおりUIA操作する。既定の通常UI起動・製品コード・TLS検証・成功条件・30秒は変えない。HTTP到達/statusだけを本文検査の代用にしない。外部ブラウザbuttonも実有効状態と一回操作を確認し、offlineの外部起動拒否に到達することを検査する。クラウド/学校通信はしない。catalog整合・cross buildが成功し、fresh59にblockingなし。

## W15 第4焦点CIが実ブラウザstepでfailure終端（2026-10-19）

- source5042ecf7e3a23f47b1a1d2273d07a3d5d67ef4cb、run39152709117/attempt1、job117749448846。18:07:29Z開始・18:10:18Z failure。登録/WinUI build成功、実ブラウザstep失敗。完了ログ取得前でCA準備か実画面かは未確定。対処前に記録しW13/W10・コードを照合する。

- 完了ログSHA315274e02532160a9591f52431b8305524207b16250e7d4fc2ac7c8235a8a67e。CAは18:09:33Zに登録成功、loopback起動・app起動・実link0操作後、HTTP見出しの30秒待機がtimeout（6checks）。CA準備停止を通過したがHTTP本文の描画は未確認。UIAが本文を取得できないのかnavigation/initialization/networkなのか未確定。
- W07/W13・旧Documentsと新Browser/fixture/Waitを照合。完了ログはmain windowの診断だけで、別browser windowのstatus/code/実UIA subtreeとserver到達数を記録していない。次は失敗時だけ所有架空browserのbounded診断とHTTP/TLS到達数を記録する。成功条件・30秒・実DOM操作・本体は変更せず、未観測を推測で修正しない。診断中の例外も元の失敗を覆わず、型のみ出力する。ローカルcross buildは0警告/0エラー（最終7.30秒）、fresh58はblockingなし。

## W14 先に待機済みだった10451もUI上限で終了（2026-10-19）

- sourcecef764862c49e7a8188bc24ca21793d0ea281af2、run39150336346/attempt1、job117744932870。17:47:08Z開始・18:07:17Z cancelled終端、主担当は取消していない。前段6成功・package依存skip、公開なし。
- W10失敗判明前に既にpendingだった同じ旧fixture版であり、W10後の無変更再試行ではない。完了ログ取得前で停止段階は未確定。新しい焦点4は別修正source5042ecfを検査する。前後結果を合算せず、実ブラウザ成功後も正式全8を行う。

- 完了ログSHA c328b63a54c50bc8b8996da086bccba4700f30a8b640bb6315f26c6ebe255d40。17:52:13Zに通常UI1058チェック成功、その後captureを経てbrowser準備で無出力、18:07:09Z job上限停止。W10と同じ旧Root準備候補で、製品の通常UI失敗ではない。focus4はLocalMachineの明示importと段階記録で比較する。

## W13 第3焦点CIが実ブラウザstepでfailure終端（2026-10-19）

- source19c715df917dcea2271b42a391602989b30c801d、run39152378034/attempt1、job117748477969。18:03:13Z開始・18:05:36Z failure。公開登録・WinUI buildは成功、実WebView2検査stepが失敗。完了ログ取得前で停止操作/原因は未確定。W10/W11/W12・実fixture/appコードと照合してから候補を決める。

- 完了ログSHA6e464f9529b7c4a6dc35ce52afefcc9e0387cd78b7630924e1f048a50e607ad0。18:05:14に一時CA作成・CurrentUser Root import開始、18:05:30に15秒TimeoutException、app起動前・0checks。certutil -user -fでもこの環境のユーザーRoot追加は完了しない。W10の無制限import候補と同段階だが、確認dialog自体は未観測。
- 既存Root fixtureとWinUI本体の証明書Cancelを照合。次は管理者権限を持つ使い捨てWindows CI runnerのLocalMachine Rootへ所有するランダムCAだけをOSコマンドで一時importする。管理者条件を検査し、不足なら失敗にする。15秒上限・通常TLS検証・自己署名/名前不一致拒否は維持。finallyは同thumbprintだけを削除し削除後の不在も検査する。実利用者PCのtruststoreは変更しない。ローカルcross buildは0警告/0エラー（15.62秒）、catalog/public-tree・Python15件成功、fresh57はblockingなし。実Windows動作は次の焦点実行で確認する。

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
