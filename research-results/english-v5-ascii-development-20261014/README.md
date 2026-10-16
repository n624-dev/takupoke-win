# 英語PP-OCRv5：日本語prefixを含まない既存英数字行

研究のみ・不採用・未有効化。以前消費したASCII-v1開発512原画像を再利用した。
全原行の非白インクbbox＋元画像2pxを保持し、日本語prefixの切断や原文からの切り直しをしない。
公式7.85MB ONNX・全438辞書・BGR48・FP32・supported CTC区間を固定した。
PythonサンプラーはWindowsのbyte一致を検証したものではない。

正例384行の完全一致223、正しい診断採用191、誤採用75。負例128行の誤採用15。
code64/96、printable44/96、lookalike30/96、natural85/96が完全一致した。
I/l/1/O/0の取り違えが残り、日本語との合成やアプリへの接続はしない。
CPU2の512推論は10.10秒、ピークRSS93,114,368 bytes（Linux・同時負荷）。

workerは512個のopaque ID・原画像・固定資産だけを受け取り、原出力を閉じてから採点した。
as-run-sourceは実行前保存。モデル・原PNGはGitへ入れず、入力/資産/source SHAと全rawを保持する。
raw SHA e5bccac771e7c9cf54a47641f90c5d5b3087eeb4cf6bb7f182178b48d8816072。
既消費開発の比較であり、未使用字体／全文書／Windows／iOSの導入資格ではない。
