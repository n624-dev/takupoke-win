# 公式GLM-OCR：非量子化FP32・CPUの別条件

研究のみ・不採用・未有効化。公式`zai-org/GLM-OCR` revision
`2e85a62840ccac27daa451df36c736c4636b8628`、MIT declared、
checkpoint2,650,579,464 bytes／SHAa16eb0de98d199293371c560f95f83130d2a2c9612449df16839f08ff9498815。
同じ既消費192原PNGを公式processor/templateと`Text Recognition:`で読み、
CPU4・FP32・eager attention・greedy・最大128tokenを固定した。

本文の原文字一致はSans46/72・Serif53/72、計99/144。教室25/48、見出し40/42。
本文の高tokenスコア（0.8以上）の誤読31行、そのうち教室16行が残る。
空画像6件は全て` ```markdown\n\n``` `という本文が空のMarkdown枠を出した。
原literalでは非空だが、本文の捏造とは数えない。形式診断を別保存し、原文字採点を変更しない。
全192呼出しを338.73秒で完了、
ピークRSS5,016,727,552 bytes。Linuxの同時負荷実測で、Windows/iOS性能の資格ではない。

以前のGGUF Q8 studyを置換・書換えない。Q8の先頭改行を除く別診断104/144と、
今回の原文字一致99/144では、runtime/template/precision/revision条件が異なり、
量子化だけの因果比較にはならない。原文字・空白・case・Unicodeは補正しない。
モデル／描画辞書の切替や全文書取得の合格として扱わない。

生成ID・既知BOS/EOS/PADのみを除いたliteral・全制御token付きdecode・native token確率・
入力tensor SHAを保存する。AR token確率を校正済みfield confidenceやCTCスコアへ転用しない。
モデルcheckpointのHub parameter count1,325,258,240に対し、loaded inference parametersは
1,107,405,824。設定にはnext-token prediction層があるが、数の差の全原因は未監査で、
READMEの0.9Bやcheckpoint総数をメモリの実測へ換算しない。

実行前freeze/as-run-source、終了後raw receiptを保存した。原文はworker終了後に採点だけで利用。
raw SHA d14c50341c89cf91cd10132ee7c506bc7a8330c257567e2f899df4c2cfdd6dd5。
modelと原PNGはGit外、最終OCR字体は未使用、iOSは編集していない。
