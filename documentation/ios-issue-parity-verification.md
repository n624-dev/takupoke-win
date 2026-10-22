# iOS Issue 41〜43との同種不具合の確認（2026-10-22）

基点は`944662b277f7d8eb31e3ce66f6a447c822144561`。学校資料・本番通信は使わず、完全架空データで検証した。

- iOS #43と同じくSchedulePresentation.OriginalSlotsがGridPeriodsを参照していた。詳細用DetailPeriodsへ変更し、1・3限だけの通常／試験／返却授業の参照と空欄の変更前補完を追加検査した。週グリッドのGridPeriods判定は変更していない。関連変更の詳細表示は元からDetailPeriodsを使っている。
- iOS #42と同じく、保存した授業時刻の復号後の型変換失敗が公開revision確認・認証後の取得を阻んでいた。保存内容の変換失敗をDB・鍵・I/Oの失敗と区別し、授業時刻だけを未取得として再取得可能にした。読み込み時には削除・書換えをせず、認証・応答検証・DB保存を完了してから置換する。画面の再読込みも同じ検証済み読出しを使う。
- iOS #41の別ファイルPDF回収はWinにない。Winは原本を暗号化BLOBとして同じDBに保存し、同じ保存期間内のDB欠損時は既存のEnsurePeriodAsyncが停止する。欠損DBを再作成せず残る鍵を保持する回帰を追加した。半期切替の既存削除仕様は変更していない。

テスト一覧をSchedulePresentation・SharedDataUpdater・SchoolDataStore・AppViewModelで検索した。新しい回帰を既存ネイティブテストへ追加し、一覧を再生成、`check --base 944662b`を確認した。

Debian13/x86_64、.NET SDK10.0.402でCore184件・統合149件が失敗0。後者には破損内容の復旧、認証中止、公開確認失敗、取得失敗、SQLite triggerによる保存失敗、破損DBの停止と保持を含む。Pythonの一覧14件・配布通知13件、公開tree／noreply検査、差分空白検査も成功。WinUI実操作・Windows固有ビルドはこのLinux結果に含めない。Actions結果は別途追記する。
