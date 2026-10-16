# 公式PaddleOCR-VL 1.6：閉じた原行比較とソース保存の制限

不採用・未有効化。公式GGUF revision `511b09642bb324401f15f97cc23bc67e8f0a291d`、
model935,769,056 bytes＋専用projector881,770,560 bytes、Apache-2.0。
原画192PNGと公式`OCR:`のみを、localhostのllama.cpp b11371へ送った。
native bundle全ファイルを固定し、CPU4・GPU0・公式embedded chat template・出力128token。
原文はreader終了後の採点に使用し、文字・空白・大小文字・Unicodeを補正しない。

本文一致はSans45/72・Serif40/72、教室3/48、見出し40/42。
教室のtokenスコア0.8以上の誤読12行、科目にも3行が残る。
空白6画像は全て文字を出して128token上限へ到達した。
全192呼出しを完了してreader停止、748.28秒、ピークRSS2,654,212,096 bytes。
Linux CPUで動作したことはWindows／iPhoneの速度・モデル資格ではない。

raw586679 bytes、SHA256 `eef35f17550cb9d8dc1eafd374eef61d6769f43418945b5e37dfe10a7eb59077`。
freeze・raw・native log・receiptは書き換えていない。
**親担当が実行後に未commitのrunnerを上書きし、元Pythonのbyte列を保持できなかった。**
元のsource hashはfreezeに残るが、現在のソースとの同一性・完全な再現性を主張しない。
`source-availability-audit.json`にこの制限を保存する。元出力の観測を導入精度の合格へ加算しない。

現在の`run_paddle_vl_v2.py`はCPU2・明示的公式templateという別の未測定profile。
元studyを再現したコードとは扱わず、実行前にas-run-sourceを別保存する。
原画素・native runtime・modelの固定記録と今回の失敗を保持したまま、次方式へ進む。
