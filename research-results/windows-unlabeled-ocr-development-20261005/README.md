# 無ラベル時間割OCRの開発測定（2026-10-05）

独立に作成した架空の無ラベル時間割1画像（1クラス×5曜日×8時限、各セル3行）で、NDLOCR-Liteの全頁処理と原画素に基づく裁断を比較しました。本文の局所的な一致は改善しましたが、見出し・原文帰属の条件を満たさず、正式復旧は3処理とも失敗しました。モデルを有効化していません。

|処理|本文120項目の生文字一致|文字＋診断用位置一致|3値すべて一致したセル|実復旧成功|推論呼出し|
|---|---:|---:|---:|---:|---|
|全頁 default|87/120|0/120|15/40|0/1|DEIM1＋既定cascade|
|全頁・白縁trim|92/120|0/120|20/40|0/1|DEIM1＋既定cascade|
|実測罫線cell裁断＋白縁trim|101/120|101/120|29/40|0/1|DEIM90＋PARSeq153|

「3値一致」は科目・教員・教室の局所診断です。クラス・曜日・時限との完全な対応や正式変換の合格ではありません。診断用の役割bandは正解データによる後処理であり、実コードの役割証明には使いません。全3処理はStrict/Builderの見出し条件で停止し、正式変換・完全tuple採点は未評価（0 assessed /各40 unassessed）です。実行エラー0、採用0、誤採用0、negative分母0であり、誤採用がなかったことから精度保証はできません。

裁断は原RGB画素と実RasterRules/PdfGridだけで決め、正解座標・名前・役割・期待文字は認識入力に与えていません。89の実測closed boxから87の非空cell領域と、画素の空白で分けた3つの外部見出し領域を作成しました。元のnon-rule文字画素81,752点を一意に帰属させました。認識器の2領域では複数parentが同じ元文字画素を所有し、正式採用資格は得られませんでした。

罫線マスクには別途、実際の2pixel罫線の端の白点により3772の黒い罫線画素を覆えないホスト側の不具合がありました。独立修正3b5b23bcは3772黒画素だけ追加し白画素0/旧mask削除0、23対象テストと473integrationテストが通りました。修正maskは裁断だけに使い、旧全頁2処理のsource/mask/rawはそのまま保存しました。描画文字による入力契約preflightは40件の正式原文一致を示しますが、旧maskのglobal ink failureを含めて記録しています。これはOCR能力や実PDF readerを通した品質の証明ではありません。

生出力は段階を分けて診断しています。period7はDEIMのretained detectionが空です。一方、ある科目はline_title検出に存在するのに、元のXML/reading-order処理で消えていました。認識済みの文字を正解へ直す・欠けた1～8を補う・bboxを切り詰める処理はありません。数字1/3/5の誤読では、tight cropが既定のCCW回転条件に入る事実を観測しました。

回転原因を調べる小さな2比較も失敗結果を保存しました。元の白い横余白だけを保持する13回のPARSeq比較は、単一見出し9種類の文字一致が5→1へ悪化しました。元tight画素とresize/decoderを固定しCCWだけ外す11回の比較では、単一見出し7種類の一致が4→4、元37位置への対応は21→20でした。回転は一部出力へ因果的に影響しますが、数字認識を十分に直しません。全頁/cell90回を再実行せず、元153atomや正式採点も変更しません。追加のpadding・幅・回転探索は止めました。

Linux CPU/ORT1.26で測定しています。裁断の1実行は210.37秒、所有process-treeのsampled peak RSS901,193,728 bytesでした。DEIM90回、PARSeqモデル256/384/768への実callは109/0/44です。少数元ROIを再利用した診断とモデルロード込み性能は別の欄で記録しました。共有ページの重複計上・同時ホスト負荷の影響があるため、Windows実機の必要RAMやエンジン単独の速度差を断定しません。detector confidenceは転記文字confidenceではなく、token softmaxも未校正の診断値です。

[results-summary.json](results-summary.json)、各原文位置ごとの原因、元native logのzlib+base64完全bytes、実ホスト結果、事前freeze、独立架空generator/oracleを保存しています。画像・PDF・フォント・weights・学校資料は含みません。generatorは固定公開フォントとPillow版を使い、所有する一時出力だけに架空画像を作成します。生成画素は各測定終了後に削除します。この1入力は消費済みの開発データで、未使用holdoutや本番相似の結合・並記・verifiedblank・PDFReaderまでの資格評価は別途必要です。

NDLOCR-Lite公式revision636d1cfeb1331f89f4048f416e49e23a09a714b5のcode/model（4models157,110,896 bytes）を使用しました。CC BY4.0原文と依存noticeをnoticesに同梱しています。研究用crop/観測hookの変更を明示し、既定のNMS適用を主張していません（公開DEIMに実NMSはありません）。モデル/依存source pinsはruntime、描画フォントのSIL-OFL原文はfictional-sourceにあります。別のTesseractも入った過去manifestを参照するrecipeにはscope addendumがあり、今回の全40/crop測定ではTesseract/Paddleを実行していません。
