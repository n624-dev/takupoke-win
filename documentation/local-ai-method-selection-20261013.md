# CPU・16GB向けの方式選定（2026-10-13）

**本番で使える精度という依頼は、まだ達成していない。**
追加学習モデルの独立384行は351行一致（91.4%）で、33行の誤りと高信頼度誤読23行が残る。
原文取得の改善も、独立した架空の完全PDF2件に限る検証で、本番資料の成功率を示さない。
モデル形式の変換、CI成功、限定評価の成功を本番導入の合格として扱わない。

改善したのは、**原文字・座標・罫線を直接取得して検証する経路**。
色付きの文字を黒限定で拒否する問題を直し、未知の描画・見出し・構造は引き続き拒否する。
追加認識モデルは部品評価で改善があっても未合格のため、有効化しない。
Strict優先、前景の復旧プレビュー、明示採用、本文最大3項目の訂正、前回正常データ保持を維持する。

## 直接取得の検証

同じ完全な5ページ・680コマ・本文2040項目の色違いでは、旧実装の完全成功1/5から
改善版5/5へ増えた。誤った受入れ0、6つの拒否対照を維持、手動訂正/OCR/生成AI呼出し0。
これは既知内容の色比較で、独立5文書の成功率とは扱わない。

改善を固定した後、新しい文字・2種類の字体・配置・ページ/クラス順・描画順の
独立最終資料を評価した。Windows/Linuxとも完全成功2/2、欠落対照4/4拒否、誤受入れ0。
各成功資料の全2040値・全680原行証明が一致し、手動訂正0。
Windowsの最大process working setは118,427,648bytes、Linuxは131,772,416bytes。
これは資料処理プロセスの実測で、Windows全体のメモリや利用者の実機の測定ではない。

[色比較](../research-results/pdf-colour-acquisition-20261013/README.md) /
[独立全文書評価](../research-results/pdf-source-final-20261013/README.md)。
画像だけのPDF、未対応描画、フォント対応表の意味、任意書式まで合格とは言わない。

## 同一の消費済み192画像で比較した認識器

以下は原画像を固定した認識部品の結果。本文72行/字体で、文書全体の成功率ではない。
VLMの生出力、学習済みHTML形式の解釈、別診断を混同しない。

| 候補・固定条件 | Sans本文 | Serif本文 | 判定 |
|---|---:|---:|---|
| V5 mobile / BGR |46/72|44/72|現行基準。高信頼度誤読あり|
| V5 server / BGR |44/72|49/72|見出し21/21・20/21。本文の高信頼度誤読16・14行、空画像全失敗|
| V6 medium / BGR |54/72|57/72|改善あり。未合格|
| V6 small / BGR |47/72|53/72|軽量。未合格|
| GLM-OCR Q8 |48/72|56/72|先頭改行だけを除く0call診断。原exact0、空画像の幻覚あり|
| Qwen3-VL2B Q8 |45/72|39/72|原plain文字一致。教室の見分けが弱い|
| Qwen3.5日本語OCR F16 |58/72|59/72|標準見出しタグの解釈修正による0call診断。教室/見出し誤読が残る|
| NDLOCR-Lite PARSeq30/50/100 |46/44/46|47/45/46|公式入力・EOS復号。全空画像が幻覚。未合格|
| GOT-OCR2公式 plain / CPU FP32 |21/72|28/72|見出し19/21・21/21だが、教室/空画像は全失敗。汎用置換に不採用|

V5/V6/SmallのWindows/Linux出力・位置・判定は同じ原画素で一致した。
灰色化は字体・内容をまたいで安定した改善にならず、アプリへ採用しない。
日本語F16モデルはLinuxで約11.56GiBを占め、16GBのWindowsで主経路にするには
メモリ余裕が小さい。Windows実測・原墨所属・役割・全文書は未合格。
予測bboxや複数readerの一致だけを原文の証明にしない。
Tesseract English bestの別見出し条件も9/21・8/21一致、空画像0/6で未合格。
GOTはLinuxで約5.24GiB・192行約44分を要した。見出し専用の研究余地はあるが、
確率校正・原位置・全文書・Windows実機の検証なしに採用しない。

[V5/V6](../research-results/ocr-model-color-20261013/README.md) /
[固定原画素](../research-results/ocr-model-color-frozen-20261013/README.md) /
[VLM](../research-results/vision-ocr-frozen-20261013/README.md) /
[日本語OCR](../research-results/vision-jp-ocr-frozen-20261013/README.md)。
[NDLOCR](../research-results/ndlocr-native-20261013/README.md) /
[Tesseract](../research-results/tesseract-eng-best-20261013/README.md) /
[GOT](../research-results/got-ocr2-20261013/README.md)。
[V5 server](../research-results/ocr-v5-server-frozen-20261013/README.md)。

## 画像セルの取得と明示ラベルの別の改善

単一授業の科目・担当・教室ラベルが各1個で別行にある場合、わずかな横位置差だけで
拒否していた構造判定を修正した。原ラベルのIDが独立し、3つの役割範囲が垂直に
重ならない証明を必須とし、並列授業や重なる行の拒否は維持する。
Integration912件、Core288件が通過。文字認識器・確率・座標を変更する修正ではない。

同じ原文IDで観測されたラベルの語部分と「語＋コロン」を重複した授業として数える問題も
修正した。独立して検証したOCR原行が明示ラベル＋コロンで始まる場合、氏名などの途中の
裸の役割語は原行の続きとして保持する。他の役割行の対応する列ラベル、別の明示コロン、
別の原行のラベルは統合しない。並記の完全／不完全対照を含めIntegration923・Core288通過。
保存済み密帳票の0call再検証では、両字体の全680枠で3役割を確認できたが、529・306個の
低信頼度原文字が残り、正式結果は返らない。構造修正を文字精度の合格とは扱わない。
[密帳票の固定原画像と再検証](../research-results/ocr-dense-ink-development-20261013/README.md)。

別の未有効化の研究では、元の文字画素をすべて残して認識入力を物理セル内に限定した。
既消費2文書の原画ではセル30→40、保存済み出力の再検証で正式一致1/2。
しかし実Windows PDF描画では画素寸法が変わり、両文書とも元の切り出し所属で拒否した。
原画での一致をWindows上の成功へ換算できない。取得失敗の原因と新全文書を継続検証する。
後続の実Windows研究では、対応範囲を厳しく限定した単一RGB画像の直接取得により、
全10ページで原画素のハッシュが一致した。物理入力条件で疎な既消費文書1/2が完全一致し、
もう1件は低信頼度見出しで拒否した。この研究経路はアプリに有効化していない。
[原画の診断](../research-results/ocr-physical-framing-20261013/README.md) /
[Windows実描画](../research-results/ocr-physical-framing-windows-20261013/README.md)。
[Windows原画像取得](../research-results/ocr-original-image-windows-20261013/README.md)。

## 軽量認識器の追加学習という別の選択肢

GPT-6.1 Sol/Highの共同担当が、独立合成1536行を使ってV6 smallの一部を学習した。
独立384行で完全一致320→351、CER2.832→1.324%、高信頼度誤読41→23。
36行が改善し5行が退行した。欠けた文字や複数行への過信も増え、単体の拒否能力は不足する。
最大3訂正の全文書資格へは換算せず、研究候補のまま保持する。追加学習は必須経路ではない。

約21MBの標準ONNX、辞書、前処理/CTC仕様、Torch形式、CoreML形式を作成した。
追加学習の元はPaddlePaddle公式のPP-OCRv6 smallの文字認識器
（`PP-OCRv6_small_rec_safetensors`、5,275,238パラメータ）。
現行アプリのPP-OCRv5 mobileからは切り替えていない。
ONNXの文字・区間・0.8判定は384/384で対応した。CoreMLはLinuxで変換できたが、
Apple/iPhone上の推論は未評価。iOSコード・設定・リモートは編集していない。
学習/調整/最終の答えを分離し、最終結果を使った再選定・出力補正はしていない。
[学習と携帯形式の記録](../research-results/ocr-synthetic-training-20261013/README.md)。

その後の英数字専用方式も、最終評価の字体を開く前に適性を確認した。
小型CTCの新規学習は調整用384行で61行一致・誤採用57行となり、不採用。
EasyOCR English G2の既存モデルは同じ調整画像で70行一致、全墨＋2px入力でも75行一致、
誤採用76→80行となった。別の消費済み見出し42行も26行一致・誤採用13行で、
このモデルへの追加学習は保留した。いずれも未使用の最終字体を温存し、アプリへ接続しない。
[新規CTC](../research-results/ocr-synthetic-training-20261013/ascii-line-ctc-v1/README.md) /
[English G2](../research-results/ocr-synthetic-training-20261013/ascii-foundation-ctc-v1/README.md)。

別に凍結したV6 smallの英数字転移学習は、調整用512行で405→472行一致、誤採用46→23行に
改善した。ただし入力の中間保存にFP16丸めがあり、production FP32との画素テンソル一致は
未確認。全候補同条件での相対比較であり、native導入精度へは換算しない。
事前の継続ゲート未達で最終字体・ONNX/CoreML出力を温存した。
[小型転移と入力精度監査](../research-results/ocr-synthetic-training-20261013/ascii-paddle-transfer-v1/README.md)。

自然な教室原行には日本語と英数字が混在する。完了した密帳票1件のV6 medium診断では、
科目・担当が各680/680一致した一方、教室は501/680で179行誤読、169行がnative0.8以上だった。
取得／構造だけの改善や低信頼度の減少では合格できず、本文最大3訂正にも収まらない。
初回の900秒上限で別字体が途中終了したため、未完了分を精度や欠落検出へ数えない。
別の凍結条件で全6帳票を再測定し、日本語の元出力を保つ英数字改善を検討している。
[初回密帳票の状態と本文診断](../research-results/ocr-dense-v6-medium-20261013-initial/README.md)。

V5 serverの全6密帳票は、先頭の低信頼度空白またはセル候補重複で全正式結果が失敗した。
TrOCR base printedの別見出し比較も25/42一致・誤診断採用9行・空白画像全幻覚で不採用。
[V5 server全文書](../research-results/ocr-dense-v5-server-20261013/README.md) /
[TrOCR](../research-results/trocr-printed-headers-20261013/README.md)。

## 配布・再利用時の範囲

検証済みWindowsコードは研究/改善ブランチとPRへ反映する。
未合格モデルの配信、有効化、閾値の緩和、中間版の追加公開は行わない。
画像PDFの一般的な読み取り精度は未達であり、今回の限定的な直接取得の成功で代替しない。
実資料や利用者のWindows/iPhone実機の成功を、架空資料やCIから推定しない。


## 後続の密帳票と学習方式の確認

V6 mediumの余裕ある上限での全6件再測定は、全13048認識呼出しを完了した。
別字体では600/680枠となり、見出しに基づく80枠が欠ける。両positiveとも正式採用は失敗。
元のFP32値とvalidWidthを保って右paddingだけを減らす別条件も、265行で幅を変えたが
文字列／低信頼度判定の改善0。入力幅だけでは今回の原因を解消しない。
[全6件](../research-results/ocr-dense-v6-medium-20261013/README.md) /
[余白0px](../research-results/ocr-dense-v6-medium-ink0-20261013/README.md) /
[右padding](../research-results/ocr-dense-v6-medium-pad160-20261013/README.md)。

共同担当はV6 mediumの全18710語彙を保ち、英数字95行だけを更新する別試験を終了した。
調整用768行で一致434→最大472行に改善したが、正しい診断採用が358→最大313行へ減り、
元モデルを選定した。日本語の元正解250/256と空白40/40の空出力は維持したが、
誤採用と損傷への過信が残り、未使用最終評価と携帯形式の出力は温存する。
[元特徴の一致検証](../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-features-v1/README.md) /
[固定特徴・混在行学習](../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-features-v1/mixed-head-v1/README.md)。
終端SVTR層の適応も全8epochを終了した。768行一致418→480に増えたが、正しい診断採用は
369→327、誤採用103行・損傷画像の採用8行が残り、元モデルを選定した。
初期のLayerNorm epsilon不一致と修正後の数値一致は別記録として保持する。
[SVTRの全結果](../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-svtr-v1/adaptation-v1/README.md)。

さらに元の高さ情報を残したpool/local-neck適応では、512行一致277→366、誤採用117→42。
ただし正しい診断採用232→208、損傷採用11行で継続ゲートに届かなかった。
入力保存はlossless FP32、登録時の字体比率と実際の比率のずれは事後監査へ記録した。
日本語保持と空白拒否だけでは導入を判断しない。両試験とも最終字体・追加形式への変換を温存する。
[高さ情報の適応](../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-height-neck-v1/adaptation-v1/README.md)。

別系統の比較では、日本語PP-OCRv3の固定原行BGRで本文39/72・28/72、見出し8/42。
EasyOCR Japanese G2も本文36/72・35/72、教室6/48、教室の高スコア誤読29行だった。
既存の日本語モデルへ切り替えるだけでは改善できず、両方を不採用とする。
[日本語V3原行](../research-results/ocr-japanese-v3-clean-lines-20261013/README.md) /
[EasyOCR Japanese G2](../research-results/ocr-easyocr-japanese-g2-20261014/README.md)。

同じEasyOCRの大型G1も本文38/72・41/72、教室0/48、教室の高スコア誤読24行で不採用。
英語PP-OCRv5は見出しBGR40/42・Gray41/42まで改善したが、日本語の本文を読めず、
教室の英数字suffixも各字体3/24しか一致しなかった。原文依存の切断や合成はしない。
[Japanese G1](../research-results/ocr-easyocr-japanese-g1-20261014/README.md) /
[英語V5](../research-results/ocr-english-v5-clean-lines-20261014/README.md)。

次にWindows標準OCRのインストール済み日本語／英語エンジンを、同じ原画で比較する研究を用意した。
このAPIには認識信頼度がないため、スコアを捏造して現行アプリへ接続しない。
初回Windows実測はen-USのみ導入済みで192呼出し完了、全186印字画像で検出0。
日本語の精度は未測定。日本語機能を導入し、全原画素の3倍反復＋固定白余白の別条件を測る。
初回のCI成功・空白出力を文字取得の成功には換算しない。導入資格はない。
[初回の未検出](../research-results/windows-system-ocr-original-lines-20261014/README.md)。
[研究条件](../tools/windows-system-ocr/README.md)。
