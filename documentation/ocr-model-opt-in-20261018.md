# OCRモデルのOFF時の取得操作（2026-10-18）

## 原因と修正

Windows利用者から、OCR更新が「処理を中止しました」となり、削除後の導入も失敗するとの報告。
利用者の版番号は未報告で、調査対象は公開10448と同じ1349641。
「AI・OCRを使用する」はOFFだったとの確認を受けた。OFF時は取得しない仕様で、削除は許可している。
モデル画面は取得ボタンを一度無効にした後、Renderの末尾の共通UpdateStatusで再有効にしていた。
押すと許可確認で拒否されるため、ダウンロード前に汎用の「中止」を表示していた。
以前のViewModel検査はOFF時の拒否だけ、実UIは取得ボタンの存在だけを確認していた。

OFFのOCR取得ボタンは共通の有効化対象へ登録せず、取得・更新にはONが必要なことを案内する。
直接の呼出しでも設定の理由を表示し、ONは既存の許可世代と取消tokenを使って取得する。
削除はOFFでも可能。取消・検証失敗時の前モデル保持、モデルのサイズ・SHA・動作確認は維持する。
共通RunAsync／UpdateStatus・Strict・取得／解析経路・モデル重み・URLは変更しない。iOSも変更しない。

## 検証

編集前1349641。テスト一覧からWindowsRecoveryModels・AppViewModelRefreshTestsを検索し、既存ケースを更新した。
実ViewModelをoffline依存で実行し、OFFはruntime0呼出し・ONは1呼出し後にoffline側の拒否となることを確認。
正常データの保持とBusy解除も確認した。実際のモデル取得成功やOCR品質の測定ではない。
.NET10.0.402／Linuxで対象2ケース・Integration全1162件、失敗0・skip0。
Pythonの一覧14件と配布13件も成功。一覧write／check --base 1349641と公開履歴・空白検査を実行した。
誤った探索先で0件となったW03は成功へ数えず、既存CIの正しいコマンドで実行し直した。

実Windowsでは、実設定トグルでOFFへ変更した後にモデル画面を開き、Render後も実ボタンが無効であること、
説明の表示、ONへ戻した後のボタン有効化を検査する。実モデルの結果注入や押し直しで成功にしない。
配布・匿名取得の完了結果は下に記録する。実機利用者からは、ONへの切替後にダウンロードが進んでいるとの報告があった。
モデル取得の完了と10449の実機導入は未確認であり、CI成功とは区別する。

## 公開結果

[39102835131・attempt1](https://github.com/n624-dev/takupoke-win/actions/runs/39102835131)、
同じsource `6fb7b982d336861990ec4d75316c0ffe7d9817c7` の全8チェックが成功し、全jobの終端を確認した。
Coreは両OS301件、IntegrationはLinux1162件・Windows1164件、失敗0・skip0。
x64 Debug／Release・ARM64 Releaseの構築と、実Windowsの1045 UI検証が成功。
新規導入・移動・再インストール・旧版からの更新等の六状態でもすべて成功した。
各状態は1046／1045／1045／1046／1045／1045項目で、テスト関数数ではなくUI検証のassert数である。
OFF→ONは実設定トグル、取得ボタンは実Automation状態で確認し、モデル取得結果は注入していない。

[0.1.0-dev.10449](https://github.com/n624-dev/takupoke-win/releases/tag/v0.1.0-dev.10449)を通常Release・Latestとして公開。
6添付を匿名で取得し、APIのsize／digest・SHA256SUMSの5項目と全SHAを照合した。
ZIP内の全ファイル集合・SHA・版・sourceもmanifestと一致した（x64 589／ARM64 585ファイル）。
x64 Setupは80,064,882 bytes、SHA-256 `7b46d34604ed1bda26534d67cba3dbd890e96c27e3ab4c99f06fd72928561444`。
保存用Actions artifactは0。匿名取得用の一時ディレクトリは回収済み。
Strict本体・通過経路とOCRインストール以外のViewModel本文、モデル重み／URLは編集前から不変。
利用者本人の10449導入・モデル取得完了・学校資料の精度は今回未確認である。
