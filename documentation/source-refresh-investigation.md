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

## 最初のWindows実測の失敗

ソースeed3b015773f799f12f755aac9282a21cba45f35の通常CI38060799044と
配布検証38060800412は完了し、Windows Integration145件のうち1件が失敗した。
同じパスの実ID変更・再取得・変更後の解析は成功したが、追加したchanged=trueケースが
正常更新後にも旧原本が保存され続けることを要求していた。正常採用後の旧原本回収は
既存仕様であり、保持を要求するのは更新の取得／解析失敗時である。
新しい解析に対応する新原本の全byte一致を検証するよう、テストの責務を修正する。
取得／解析失敗で前回正常結果と旧原本を保持する既存ケースはそのまま残す。
changed=falseの実ID更新・解析再利用・再起動は成功した。公開ジョブは実行されていない。

## 公開・配布物の確認完了

ソース20d990f238fa536b1683b0e3d62ec6b28f432e5cの通常CI38061116648と
配布検証38061117361・試行1は全て成功した。配布検証の7検査と公開ジョブの計8ジョブを確認した。
Windows Core183件・Integration145件（実ファイルIDの2件を含む）は失敗0。
Linux Core183件・Integration143件は失敗0で、Windows専用2件を加算していない。
導入・移動・再導入・dev.5から同じ／別フォルダーへの更新・起動移行の実UIは
406／406／407／406／406／406検証が成功し、ショートカット・アンインストールも確認した。
ARM64はビルドと実行形式を検証し、ARM64実機の起動とは区別する。

[0.1.0-dev.10438](https://github.com/n624-dev/takupoke-win/releases/tag/v0.1.0-dev.10438)は
通常Releaseとして公開され、Latestに一致した。target_commitishも上記ソースに一致した。
Setup.exe2種・ZIP2種・INSTALL.txt・SHA256SUMS.txtの6添付を認証なしで取得し、
全サイズと公開チェックサム5件・API側digest6件の一致を確認した。バイナリは保存せずストリームで検証した。
x64 Setupは70,782,093 bytes、SHA-256
`680060aa304a3c9e6ef14bfabbfaa6824623bf90cc81e8a860ad3cd8369a0c90`。
利用者のOneDrive更新操作・Windows25H2実機での確認は未実施であり、自動テストの成功とは区別する。

[全検証・公開](https://github.com/n624-dev/takupoke-win/actions/runs/38061117361)
