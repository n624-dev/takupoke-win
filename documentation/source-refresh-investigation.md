# 選択済み資料の更新調査

## 2026-10-10 ファイル更新で再選択を要求するとの報告

利用者から、更新時に「選択した原本とは別のファイルになっています。資料を選び直してください。」
が表示されると報告された。調査時HEADは884c811で、製品コード・テストは変更していない。
AGENTS.mdと共通pdf-recovery-pending-policy.mdはこのcheckoutには見つからなかった。
テスト保守方針、一覧material分類、関連する既存ケースを読んだ。

MaterialCoordinator.Runは再取得時に保存したSourceRecord.FileIdentityをFileSourceReaderへ渡す。
FileSourceReaderは開いたハンドルのIDが保存IDと異なるとReplacedを投げ、SHA計算へ進まない。
WindowsFileIdentityのIDはGetFileInformationByHandleのVolumeSerialNumberとFileIndexである。
同じパスへのファイルの作り直しでもIDが変わるため、この条件で拒否される。
利用者の更新処理が具体的にどの作り直しを行ったかは、実機で未測定である。

SourceWatcherは変更・作成・削除・renameを監視し、2秒の通知集約後にAppViewModelの
自動更新へ接続済み。監視がないための手動選択要求ではなく、再取得のID条件が直接の停止箇所である。
既存ReplacedFileAtSamePathIsNotSilentlyAdoptedも、ID変更を拒否して再選択を要求する期待値を持つ。
現在のReparseAsyncは保存コピーを読む経路なので、最新版の再取得の代替にはならない。

読み取り中は初回ハンドルと終了時に開き直した同じパスのID・長さ・時刻を比較し、Changingを拒否する。
この整合性確認と、以前の取得からIDが変わったことの拒否は分けて検討できる。
前回正常Analysisと対応する原本の保持、行除外の採用直前のSHA確認も確認した。
未実装の対応は[再取得方針](source-refresh-pending-policy.md)へ分離した。
この調査では実行テスト・Windows実機測定・配布は行っていない。

## 実装中の検証

通常の再取得は過去のファイルIDで拒否せず、今回のハンドルとパスの整合性確認を残した。
SHA不変ではID・更新時刻を更新して旧解析を再利用する。確認採用時の厳密なID確認は変更していない。
対応テストを一覧で検索し、同じパスでの不変／変更、再起動、読取中の差し替え、除外許可を検証する。
最初のLinux Integration143件は2失敗・141成功・skip0。追加した2ケースの比較が、
逆シリアライズされたChanges配列の参照比較になっていた。保存payload全体の値比較へ直し、
原本ID・SHA・解析時刻・授業内容を含む不変条件を維持して再検証する。実装の期待値は変えていない。

Linux .NET 10の再検証はCore183件・Integration143件、失敗0・skip0。
Windows専用の追加2ケースは実際のWindowsFileIdentityを使い、同じパスでの
SHA不変／変更の原子的置換と再起動を検証する。Linuxではこのファイルをコンパイル対象から
除外し、Windows CIの実行結果を別途記録する。
FileSourceReaderの取得形式・サイズ・リンク・同一読取中のID／長さ／時刻検証と、
行除外採用直前のID／SHA検証は変更していない。

配布前UIの履歴はchange-row-exclusion-verification.mdを
refresh／更新／ApplicationChecks／row／UI／timeoutで検索した。過去の位置確認・色往復・
更新インストーラーの失敗を確認した。今回UI操作や期限を変更せず、既存の実操作と
導入・更新・再起動の包括的検査を同じソースで実行する。

最終のLinux再検証でもIntegration143件・失敗0・skip0。
Windows専用2件はこの数字に含めない。一覧136登録、公開tree・作者検査、
一覧ツール14件、配布契約13件、差分検査が成功した。
