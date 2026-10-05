# 実画素の行単位OCR比較（2026-10-05）

同じ独立架空の無ラベル時間割1画像から、罫線を除いた実画素の占有行170個を切り出し、PARSeq30・PARSeq100・Tesseract jpnで全行を一律に認識しました。PARSeq100は本文120項目が一致しましたが、時限・クラス見出しの誤読が残り、どの処理も正式復旧に至りませんでした。

|固定処理|本文の生文字＋診断位置一致|見出し一致|3値一致のセル|正式復旧候補|
|---|---:|---:|---:|---:|
|PARSeq30|119/120|18/50|39/40|0|
|PARSeq100|120/120|39/50|40/40|0|
|Tesseract jpn OEM1 PSM7|4/120|20/50|0/40|0|

本文3値は科目・教員・教室の局所診断です。クラス・曜日・時限と結び付いた完全tupleではありません。見出し50項目は年度・学期・表題3、曜日5、学年・クラス2、時限40です。正解データは出力後の採点だけに使い、認識器やホストの入力に渡していません。

元のnon-rule印字81,752画素がそれぞれちょうど1つの行ROIに属することを検証しました。行は連続した占有y-runにより固定し、3行の役割・期待文字・正解数を使って結合や選別をしていません。両PARSeqは同じ原RGB・順序・原画素support箱に直接入力し、DEIM/XML/cascadeを使いません。page uprightとしてCCW分岐だけ外した前処理を明示し、resize・decoder・weightsは固定しています。全行へ同一モデルを使い、行ごとの良いモデル選択はありません。

PARSeq30は本文「図研07」を「図07」と読み、数字にも余分な文字が付きました。PARSeq100は本文120/120ですが、学年1と時限1を11、時限7を1と読む誤りが残りました。Tesseractでは1・3・4は一致する一方、2→ノ、5→う、6→4、7→/、8→のが反復し、本文にも多くの誤読がありました。各170行の同一RGB SHAを照合した実出力差は確認済みです。エンジン・モデル・前処理・設定が異なるため、モデルだけの欠陥やtight cropの影響を断定しません。追加のPSM・言語・余白探索は止めます。

Tesseractは1回のwarm初期化後、各行でClear/履歴解除し、PSM7を一律適用しました。1行1Recognizeの170回だけ実行し、同じ認識済み出力からUTF8 text・HOCR・TSV・BOXを取得しました。主入力wireは実TSV word box516個を原座標へ整数変換したものです。文字数で箱を等分せず、実BOX文字596個は別の診断として残しました。空TSV word0、原行外word0、原行外BOX0です。jpn_vertの不足警告はそのまま保存しており、初期化と170認識は返却されています。この警告を170件の実行失敗や各誤読の確定原因にはしていません。

原文のホスト再生は実Strict→Builder→Rules/Engine/Validator5/正式変換へ接続しました。PARSeq2処理は時限見出しP05、Tesseractは年度見出しP03でStrictが拒否し、Builderも対応する原文条件で停止しました。PARSeqはglobal ink coverageを満たしますが、Tesseractのword箱では満たしません。全3処理の実行エラー0、ホストの正式候補0、誤った正式候補0、実採用0、negative分母0です。完全tupleは0 assessed /120 unavailableであり、0/120という正解率ではありません。候補がないことから採用精度を保証しません。

confidenceNoneは校正済み転記confidenceがないことを表し、PARSeqのtoken softmaxやTesseract word0～100をPaddleの0.8へ換算しません。幾何・画素coverageが正しいだけでも本番の取得契約が成立したとはせず、全処理のproduction acquisition eligible=falseを保持しました。正解へ数字を修復、隣セルから補完、bbox clip、per-cell best選択はありません。

Linux CPUでPARSeq30は9.17秒・sampled process-tree RSS206,647,296 bytes、PARSeq100は29.26秒・278,597,632 bytes、Tesseractは2.74秒・95,920,128 bytesでした。PARSeq2処理は並行実行、Tesseractは別の時点です。モデルロード込みで、実行条件も異なるため速度や必要端末RAMを一般化しません。Tesseract実library/engine/data/config、NDLOCR-Lite4モデル、ORT1.26と依存版、raw SHAは各事前recipeとruntimeに保存しました。

入力は[前の独立架空source](../windows-unlabeled-ocr-development-20261005/fictional-source/)の3740×800 PNGで、既に消費した開発データです。実PDFReader/WindowsRender、未使用holdout、結合・並記・verifiedblank・negative全体品質は未評価です。本文120/120をモデル合格やAI完成としません。raw logはzlib+base64で元bytesを完全復元でき、原文字・順序・箱を保全しています。学校資料、画像、PDF、フォント、モデルweights、画像base64は同梱していません。

NDLOCR-Lite code/modelは公式revision636d1cfeb1331f89f4048f416e49e23a09a714b5、CC BY4.0。Tesseract5.5.0と公式tessdata_best jpn e12c65a915945e4c28e237a9b52bc4a8f39a0cecはApache2.0です。原noticeを保存し、実モデルやbinaryは配布していません。NDLOCR-Lite原noticeの非UTF8 bytesも変更していません。
