# CPU・16GB向けの方式選定（2026-10-15更新）

この記事は2026-10-13時点の選定記録です。現在の配布構成・モデル更新方法は[10446の構成と精度](ocr-source-order-accuracy-20261016.md)を参照してください。

製品のCPU OCRは、日本語PP-OCRv6 mediumと、公式Torch英語PP-OCRv5を4epoch追加学習した英数字モデルへ接続した。モデルは設定から約99 MiBを明示取得する。原文字・座標・罫線を直接取得できるPDFはその経路を優先し、画像が必要なページはWindowsでPNG・144DPIへ描画する。

選定の判断は文字誤り率だけでなく、全文書の枠取得、原画像による確認・訂正、欠落負例、CPU時間・メモリを含む。OCR誤読0は条件にしない。高信頼度の誤読もプレビューから選べ、新規schema3は確認・本文訂正を合計128項目まで扱う。原文・native confidence、原PDF／ページ／cropのSHA、利用者の明示採用を保持する。旧schema1／2の3項目上限は変更しない。

[現在の構成・精度・製品操作](local-ai-status-20261014.md)と[製品接続・Windows全文書の記録](../research-results/ocr-production-integration-20261015/README.md)を参照。

以下は各比較を実施した時点の条件・判定・失敗の履歴である。「未合格」「現行」「有効化しない」などはその実験時点を示す。旧数値ゲートとrawはそのまま保存し、後の組合せ・製品構成の結果で上書きしない。追加生成AI（Foundry）の未合格候補は引き続き配信しない。iOSは参照のみで編集していない。

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

MangaOCRの日本語画像基盤も原行192件で本文30/72・37/72、教室3/48・高スコア誤読18行。
測定前に辞書のliteral空白欠落を確認した。文字tokenと表示用decodeを別保存し、
日本語・英数字の幅や原文の空白を後から補正しない。転移学習や導入候補に選定しない。
[別の日本語画像基盤](../research-results/manga-ocr-clean-lines-20261014/README.md)。

次にWindows標準OCRのインストール済み日本語／英語エンジンを、同じ原画で比較する研究を用意した。
このAPIには認識信頼度がないため、スコアを捏造して現行アプリへ接続しない。
初回Windows実測はen-USのみ導入済みで192呼出し完了、全186印字画像で検出0。
日本語の精度は未測定。その後、日本語機能の導入を試み、別実行で全原画素の3倍反復＋固定白余白を測った。
初回のCI成功・空白出力を文字取得の成功には換算しない。導入資格はない。
[初回の未検出](../research-results/windows-system-ocr-original-lines-20261014/README.md)。
[研究条件](../tools/windows-system-ocr/README.md)。

日本語のOS機能取得は8分上限で停止し0測定。別の導入済み英語エンジン測定は
原画素3倍＋固定余白で非空出力88/192まで増えたが、印字186行の完全一致0。
日本語機能の精度は未測定で、Windows利用者の日本語環境の結果へ一般化しない。
[導入失敗](../research-results/windows-system-ocr-japanese-install-20261014/README.md) /
[拡大英語入力](../research-results/windows-system-ocr-scaled-lines-20261014/README.md)。

全8 CTC読取器・全28 pairの0call監査でも、V5 mobileとV6 mediumの同じ高スコア誤読が13行。
model一致だけで文字を自動採用する方式は選ばない。横幅2倍の別入力でも本文54/72→53/72、
57/72→52/72に下がり、原画素の反復だけでは教室コードを改善できなかった。
[共通誤読](../research-results/ocr-reader-agreement-audit-20261014/README.md) /
[横方向の別入力](../research-results/ocr-v6-medium-aspect2-20261014/README.md)。

元CNNの最終depthwise filterだけを調整する新条件も終了した。384行一致228→240だが、
正しい診断採用175→81、誤採用14・負例採用1が残り元モデルを選定。日本語128/128保持だけでは
導入できず、同条件の延長を保留した。最終字体・追加形式への変換を温存する。
[限定CNN適応](../research-results/ocr-synthetic-training-20261013/ascii-paddle-medium-cnn-tail-v1/README.md)。

公式PaddleOCR-VL1.6も原行192件を完了した。本文45/72・40/72、教室3/48、見出し40/42。
教室の高tokenスコア誤読12行と、空白6画像の出力上限到達があり、不採用とする。
CPU4のピークRSS約2.47GiBだが、実Windows精度／性能は未評価。
親担当が未commitのrunnerを実行後に上書きし、元Pythonのbyte列を保持できなかった制限も
raw／freezeを変えずに記録する。別v2 runnerは元studyの完全再現として扱わない。
[公式VLMと保存監査](../research-results/paddleocr-vl16-clean-lines-20261014/README.md)。


英語専用V5を元から英数字だけの既消費384正例で測ると223行一致したが、
正しい診断採用191に対し誤採用75、負例128行の採用15が残った。lookalikeは30/96一致。
日本語本文と組み合わせて導入できる精度ではなく、学習の可否は別に初期忠実性から調べる。
[英数字だけの原行](../research-results/english-v5-ascii-development-20261014/README.md)。

原インクを全て保持する白列分割も、384正例中99行で文字数と領域数が異なる。
原画像保全だけを文字境界の証明へ換算しない。
[画素列の診断](../research-results/physical-ink-column-audit-20261014/README.md)。

既知の2字体で原文字列全体を描き直す照合では、V6 mediumの高スコア誤読18行を全て拒否し、
正しい128行を保持した。ただし印字186行の取得128、教室15/48に留まる。
未知字体やスキャンでの性能・Unicodeの一意性は未証明で、拒否による文字取得の改善とはしない。
[全文字形の照合](../research-results/whole-line-render-veto-20261014/README.md)。


英語V5の元ONNXからTorchへの学習準備は、512原行＋10動的形状の文字・判定が全て一致したものの、
固定数値ゲートに届かず学習未開始で終了した。閾値を変えて学習へ進めず、元の失敗を保持する。
これは学習後の精度結果ではなく、初期計算の一致を満たさなかった結果である。
[英語基盤の初期忠実性](../research-results/ocr-synthetic-training-20261013/ascii-english-v5-transfer-v1/README.md)。


GLM-OCRの公式非量子化FP32版はCPU4で192件を完了した。原文字の本文99/144、教室25/48、
見出し40/42でV6 mediumの本文111/144を上回らない。高tokenスコアの誤読31本文行・
空画像6件の空Markdown枠という形式差があり不採用。本文の捏造とは数えない。ピークRSS約4.67GiB、338.73秒（Linux同時負荷）。
以前のGGUF Q8とtemplate/runtime等も異なるため、量子化だけの因果比較にはしない。
[公式FP32基盤](../research-results/glm-ocr-fp32-clean-lines-20261014/README.md) /
[現在地](local-ai-status-20261014.md)。
