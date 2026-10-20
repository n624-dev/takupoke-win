# 非表示の留学生向け変更と時間割表示

通常・特別授業の置き換えは、利用者設定で表示対象となる変更だけで判断する。
非表示の留学生向け休講・補講だけがある場合、元の授業を表示する。
表示対象の休講を通常授業へ戻すことや、試験／返却の空欄を通常授業で埋めることはしない。
留学生表示ON、変更反映OFF、変更の原文・詳細・週の移動範囲は従来どおり。

`TimetableEngine`はホーム・時間割共通の読取り専用projectionである。
変更対象はBlocks内の表示行の確定順序のみ。Strict本文・取得入口・通過経路・Parser・保存は不変。
既存一覧のTimetableEngineTestsを確認し、既存のprefix単独・補講優先では検出しない組合せを追加した。
独立の架空2032年4月5日・1_CN・1〜2限で、prefixと学科別対応表、ON/OFF、翌週、
表示対象の休講／補講、試験／返却と空欄、曜日振替のある土曜、共通時刻と原文保持を確認する。
変更前は対象33件の5件失敗・28件成功。変更後Linux Core341件・失敗0。
Linux Integration1168件・一覧Python15件・配布Python13件も失敗0。catalog311と公開履歴検査が成功。

## 0.1.0-dev.10453の配布検証

source `5d1035cac3a7ffc162290490991f2649a7955761`、[run39391712035・attempt1](https://github.com/n624-dev/takupoke-win/actions/runs/39391712035)の全8必須チェックが成功、全job終端・保存Actions artifact0。
CoreはLinux／Windows各341件、IntegrationはLinux1168件／Windows1170件・失敗0。
通常実UI1058チェックと実WebView2 50チェックも成功した。
x64の新規導入、同版再導入、移動・更新、実dev.5から同／別folder・無関係startupを含む6状態で実UIが成功（各[1059, 1058, 1058, 1058, 1058, 1058]）。元の設定・保存済み資料・無関係ファイル／startupを保持し、アンインストールと所有領域の回収まで確認した。

通常Release／Latestとして公開後、公開6添付を匿名で全bytes／GitHub digest／ハッシュ照合し、5件のチェックサムと両ZIPの全manifest payloadを確認した。テスト用Playwright／FlaUIの混入もない。
製品差分はTimetableEngineの表示判断のみで、他169製品ファイルのbytesは68a2ba1から不変。Strict本文・全入口／経路・Parser・モデルと保存は変更していない。

| 添付 | bytes | SHA-256 |
|---|---:|---|
| INSTALL.txt | 4092 | `858981daad019f771c1c6a96fef6ce4c9ee1a8fc768f6a69d1b8468830b03be6` |
| SHA256SUMS.txt | 495 | `0be7190925067950529e215ceeaf1bb1e751d772e408dc4e0d2b080bd4853976` |
| takupoke-0.1.0-dev.10453-arm64-Setup.exe | 77482191 | `ebd43f623dd3c7dd1c0c1fc7b8f3146459aa98b100fadf3e533efd955709cf38` |
| takupoke-0.1.0-dev.10453-arm64.zip | 114919715 | `c4ccec077d0ef4d8470515bc7ec7cd1c112151772e71815ca010c9d749af0b3a` |
| takupoke-0.1.0-dev.10453-x64-Setup.exe | 80094583 | `b1d71cf5c13f49137a8ddc07ed8064d2e80e361703df1466dce55b42e2e58597` |
| takupoke-0.1.0-dev.10453-x64.zip | 117762306 | `352cdc3e94d7dd12ed77a15a70c5b5d07e68c445dce9546f7271e94d14541e00` |

CI・独立架空入力・配布物照合と、物理Windows／ARM64での導入・今回の実資料表示の再確認は区別する。後者は未確認。
学校PDFや利用者画像は再取得せず、資料の本文／画像は公開しない。追加モデル・追加学習は行っていない。
