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
