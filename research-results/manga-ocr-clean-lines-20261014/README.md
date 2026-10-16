# MangaOCR：日本語画像認識の別基盤

研究のみ。不採用・未有効化。`kha-white/manga-ocr-base` revision
`aa6573bd10b0d446cbf622e29c3e084914df9741`、Apache-2.0、444,135,475 bytes、SHA256
`c63e0bb5b3ff798c5991de18a8e0956c7ee6d1563aca6729029815eda6f5c2eb`。
既消費192原行を、公式RGB224/PIL入力・CPU FP32・元のbeam4設定で一回ずつ読んだ。
固定64新token上限、EOS未完了0。pickle形式はtorchのweights_only=Trueで読み、remote codeや
外部推論は使用しない。モデル重み／画像／学校原本をGitへ追加しない。

| 字体 | 本文一致 /72 | 教室一致 /24 | 教室の高スコア誤読 | 見出し一致 /21 |
| --- | ---: | ---: | ---: | ---: |
| Sans | 30 | 1 | 8 | 4 |
| Serif | 37 | 2 | 10 | 6 |

空白6画像は全て文字を出力した。192呼出しの推論82.46秒、ピークRSS943,648,768 bytes。
生成tokenのbeam経路確率（EOS込み）0.8は未校正の診断で、CTCや原インク所属の証拠ではない。

測定前に全6144語彙のASCIIを確認したが、literal空白はなかった。
原文の空白を任意に維持するモデルにはなれず、その制約をfreezeへ先に明記した。
nativeの各文字tokenを連結し、既知BOS/EOS/PADだけを除く。UNKは残す。
tokenizerが表示用に挿入する空白付きdecodeも別のrawに保存する。
文字の幅・Unicode・大小文字・原文の空白を補正しない。

原文はworker終了後にのみ採点した。raw SHA256
`62c45b388f1041129fc881b71682841e3d5d56fdf36be8ec850ddcc1bc4d6de1`。
最終字体・PDF取得・セル所属・実Windows全文書合格の証拠ではない。
