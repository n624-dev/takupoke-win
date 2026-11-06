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

W21補完：公式PowerShell7.5.4を所有toolchainsへ設置して元9件を実行し失敗0（11.637秒）。
catalog14件/失敗0（0.546秒）、license検査、public-tree、catalog164/check --base2535129、
公開scriptのPowerShell構文が成功した。主担当は2535129以降のコード差分全体（実partial Model、
activation、実Checker、取消/Dispose/Prune、独立fake通信と9追加Modelケース）を読み、元の検査・
CI/配布gateの削減がないことを確認した。WinにはAGENTS.mdはなく、iOSの実装共有やAPI変更はない。
この最終sourceを既存main CIでWindows実ビルド/x64実操作まで確認し、成功後に通常Release/Latestの
既存配布workflowへ進む。ARM64実機/学校実PDF/実ログイン/自然なロック復帰は未確認のまま残す。

## W22 正式CIのx64 Release実操作失敗

予定01:50 UTCの一括照会、source81b6ddf7d94f68d0bf44b61a272861892c010c63/run42144738673/attempt1、
WinUI x64 Release build and launch/job127653482575は01:34:52Z開始・01:38:01Z failure終端。
具体case/ビルドか実操作か/原因は完了log未読で未確定として対処前に記録する。他6jobは成功、MSIXは
元構成でskip。全job終端、配布は未開始。7実job logを各一度・artifactを一度確認し、過去詳細/旧現sourceを
照合してから候補を選びpush前には再試行しない。

W22完了log：build成功後、独立screen-reviewのLight/200%で184項目通過後、ScreenReview.cs:90の
時刻Textとcellの左右11px/下9px余白assertに失敗した。通常のnative smokeは前段成功。
画像Light-200-timetableを全16片から復元し70431bytes/SHA256
35702273a88410dd34a083e8774c4c336e1f0bbcb4da8d99f0bb830717f2995cを照合・目視した。
状態APIの失敗案内が上に表示され、1限cellの下側がアプリの表示領域を越えている。時計は表示されている。
元のUIA測定値はlogにないため、製品の文字幅/余白自体の不良かclipとの差かをこの画像だけで断定しない。
padding/textscale/clock/拡大で過去記録を検索し、10457の色UI/fixture寿命失敗とPR47の週選択詳細を読み、
2535129と81b6ddfのScreenReview/Timetable/Controlsを比較した。これらは同じbytesで、以前の色の
invoke未成立や日付fixtureとは異なる。新activationによる状態確認は失敗案内を表示しうるが、保存行事は保持する。
候補は元のスクリーン撮影を残し、時刻余白を測る前に既存のnative page ScrollPatternと実viewport/cell位置/
VerticalViewSizeから算出した一回のscrollで1限cellをviewport内へ出す。元30秒内にcell全包含/有限正枠を要求し、
元左右11/下9pxの余白・実200/150%・全画面/明暗/狭幅・全CI/導入検査は維持する。期待値や時間を緩めない。
実測frameを短いconsole行へ加え、scroll後でも余白がなければ失敗のままにする。製品のレイアウトや警告を消さない。
候補/根拠を隔離refへpush後、Windows UI test targetクロスbuildとcatalogを確認し、新sourceの全CIで実操作を確認する。
Win42144738673は全8表示job終端（6成功/1失敗/1構成skip）、7log各一度・artifact0確認済みで再照会しない。

W22候補4a3f0bbをpush後、Windows UI test targetのRelease/Linuxクロスbuildが警告0/エラー0（17.86秒）、
catalog164/check --base81b6ddfが成功した。主担当は元撮影と余白literal/両倍率/全画面を保持し、一回のnative
page scrollだけを元の測定前へ加えたことを確認した。クロスbuildをWindows実操作成功に換算せず、新source全CIで確認する。
