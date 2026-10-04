# 非Paddle OCR：架空ラベル付き部品の比較

本番PDFでは項目名のない3行が使われるため、この結果は**ラベル付きの原因分離用部品試験**です。本番に似た無ラベル時間割、全40枠、Strict/Rules/Validatorによる正式復旧は未評価です。モデルは有効化せず、製品・公開アプリを変更していません。

同じ8枠を24pxと16pxで描いた完全架空の2画像を使用しました。これは16観測のpaired条件で、独立した2文書ではありません。元のPDF・画像・学校の教科名・教員名・教室名は取得・転用していません。native入力はPNGのみで、別の原文・位置oracleは後処理専用です。モデル・フォント・画像・PDFは同梱しません。

|候補|実取得|文字・位置・対応のstrict枠一致|観測した主な問題|
|---|---:|---:|---|
|NDLOCR-Lite default|2/2|0/16|コロン表現、重複、箱の越境、両条件の紫→業|
|Tesseract best jpn OEM1 PSM11|2/2|0/16|文字欠落・誤読、余分な罫線文字、箱の越境|
|現在のPaddle実装|0/2|未評価（評価済み0枠）|切り出し一意性・重なりの検証で認識前に停止|
|NDL 白い外周だけ除去|2/2|0/16|文字・箱は改善したが重複と見出しの結合が残る|

NDL defaultの文字品質を0点とは評価していません。24pxでは24項目中23項目にコロン幅だけを別扱いすれば原文一致する単一候補があり、16pxは実領域の連結で23/24でした。白い外周だけを除く1つの固定変更では、24pxの単一候補24/24、16pxの連結24/24になり、紫→業は両条件とも正しい紫になりました。全候補を保持した24pxの連結は重複があるため18/24です。いずれも後処理診断で、文字の自動補正・正解候補の選択・正式採用に加算していません。

trimはRGB全値255の外周だけを除き、**元の認識切り出し箱内**の全nonwhite pixelを保存しました。元検出箱と元のparsed parent cropを残し、JSONの箱はpixel-derived ActualRecognitionROIです。未変更のdetector箱や実文字箱ではありません。以前の検出箱の外に切れた文字を保存できたとは証明しません。48項目でこのROIが項目帯内に収まりましたが、metadata領域や重複所有によりwhole-source資格は両画像ともfalseです。全ページのink coverage、空欄、全役割対応、正式復旧は未証明です。

同じPARSeq session.runの実logitsを観測し、argmax/EOS・tensor SHA・softmax・次点差を保存しました。追加推論や予測変更はありません。正しい紫のderived softmaxは24px .9991、16px .9934でしたが未校正です。以前の誤った業のlogitsは採っておらず、誤読が低確信だったとは言えません。NDLの出力confidenceは検出の値で、文字の確信度ではありません。Tesseract word確信度、Paddle CTC、softmaxを同じ尺度としません。

NDLのiou引数は固定上流でNMSとして使われていません。classes/countsにscore maskを適用しない一般的な実装懸念は、現graphのTopK(sorted=1)と実降順scoreではprefix選択となるため、この測定の対応ずれ原因ではありません。maskだけの再推論は実行していません。

Tesseractの最初の2回はnamed出力設定を見つけられず、exit0でもTXTのみ、座標未評価でした。元ログを保持し、実インストール済み設定と同じ出力フラグだけで必要な2回を再収集しました。NDL/Paddleは再実行していません。jpnが暗黙にjpn_vertを探す不足警告も保持し、別重みや認識設定を追加していません。

所有プロセス群のRSSを100ms間隔で合計したpeakはNDL default約890MB、trim約898MB、Tesseract約74MB、Paddle約346MBです。共有ページの二重計上と採取漏れがあり、端末の必要RAMではありません。Python ORT1.26.0/.NET ORT1.23.2/Tesseract5.5.0と起動条件が異なり、速度・認識差をruntime単独の原因とは断定しません。Linux CPU測定で、Windows実機測定ではありません。

モデル帰属：NDLOCR-Lite ndl-lab/ndlocr-lite 636d1cfeb1331f89f4048f416e49e23a09a714b5、code/model CC BY4.0、157110896B。Tesseract tessdata_best e12c65a915945e4c28e237a9b52bc4a8f39a0cec、jpn Apache2.0、14330109B。元Paddle PP-OCRv5_mobile_det/rec_onnx Apache2.0。NDL実detector入力は800×800で、ファイル名の1024とは異なります。依存ライセンス・original source/model/hash・実設定・生出力・採点・各対象の原因は同梱ファイルを参照してください。

rawのzlib+base64 JSONは元byte数/SHAを併記しています。原出力は再書換えず、派生評価だけを別ファイルに保存しました。無ラベルの新架空入力が次の優先評価であり、このラベル付き入力の追加推論は停止します。
