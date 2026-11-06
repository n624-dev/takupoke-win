# 学校行事の元PDF確認結果への追従

利用者の追加指示でiOSと同じ問題をWindowsでも確認した。base253512955c9ebb6572a73289140a9cd55a120316
ではEventSourceCheckerのTTLは確認中60秒だが、AppViewModelが完了状態を自動取得しない。
状態APIだけを60秒間隔で追加最大3回取得し、完了/失敗/非アクティブ/保存対象変更で止める。
元の保存行事と成功日時を保持し、上限後は未確認とする。iOS/APIのコード共有やAPI変更は行わない。
編集前にtest-maintenance-policy/test-catalogを読み、EventSource/AppViewModel/MainWindowを検索した。
新しい実Model検証と既存のAPI/ライフサイクル検査で通信・保存・取消を確認する。

## W20 初回の失敗記録

2026-11-06 01:22 UTCに、base2535129+未コミット候補、Debian13/.NET SDK10.0.403/Releaseの
Integration対象検査（AppLifecycleTestsまたはEventSourceCheckerTests）を実行し、56件中55成功/
失敗1、全体5.1547秒、終了1を観測した。具体case/失敗箇所/原因は完了log未確認で、対処案は
まだ選択していない。Windows実UIの失敗や製品原因とは未確定。logは所有stateの
win-event-source-targeted.logへ保存した。これから新しい診断と過去の詳細/旧現コードを照合し、
次の試行前に根拠と候補をpushする。開始済みのiOS42136180150は取消しせず継続する。

完了logの失敗はAFailedFollowUpStopsWithoutReplacingEventsOrLastSuccessfulResultのAPI回数で、
期待2/実測3（new test:133）。他55件は成功し、上限/0秒・120秒復帰/背景in-flight取消/
原本変更の新ケースも通過した。Handler以外の学校通信や実WinUIの操作はない。
W15/W17のコンパイル境界、W18/W19の独立期待の詳細と旧2535129のCheckAsync・今回のStepを読んだ。
今回はコンパイルやPDF受理ではなく、テストStepが失敗通知の後に公開Checkメソッドを呼び、
期限切れcacheの新しい通常取得を開始していた。timer側はFailedで停止している。
候補はSnapshotChanged callback内（元requestの所有中）でTaskを合流取得し、そのTaskだけを
通知後にawaitする。期待2回・失敗時保持・全56件を変えず、余分なテスト由来GETを除く。
この事実/旧現比較/候補を隔離refへpushした後にのみ同じ対象検査を行う。

W20候補ae80224のpush後に同じ対象56件をRelease/Linuxで実行し、失敗0・skip0、4.8124秒。
自動GETの期待回数・失敗時保持と前回結果/日時の全検査を維持した。実Windows UIはCIで別途確認する。

## W21 配布契約のローカル検査失敗

2026-11-06、ae80224+README/公開文面の未コミット差分、Linux/Python3.12。
Core228/Integration358/Platform5は失敗0。Python catalog14成功後、test_store_release.pyは9件中
23assert失敗で終了した。具体的失敗と原因はlog未読で未確定として対処前に記録する。
次に所有win-store-tests.logと旧配布契約/今回の差分を照合する。実Windowsビルド/公開はまだ開始していない。

W21の23assertは全てPowerShell7未設置の前提チェック（PWSH=None）で、配布script実行前だった。
旧2535129と現test_store_release.pyのrun_scriptは同じ必須条件を維持している。前回40c792eの
配布Python23件成功記録も読み、今回の製品や文面差分の失敗とは区別した。対処は公式PowerShell7を
所有toolchainsへ設置し、同じ9件/全23条件を再実行する。skipや期待変更はしない。本記録と候補を
隔離refへpush後にだけ再検証する。Core228/Integration358/Platform5は全件失敗0・skip0。
