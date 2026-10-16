# 帳票全体・英数字モデルのローカル比較

実学校資料を入力しない架空PDFの開発用ツール。CPU向け、iOS編集なし。
[閉じた結果と失敗記録](../../research-results/practical-whole-documents-20261014/README.md)。
アプリの既定OCRモデルを変更するツールではない。

prepare.pyは固定の字体SHA・生成条件・独立する架空値を登録してから描画する。
run.pyは既存ocr-physical-framingのnative readerをコピーし、原画像取得とV5/V6のworker終了を
確認してから正解との比較へ進む。score.pyは欠落項目を別に数え、条件付きCERと値の完全一致を出す。
Program.csの検査は実際のCore RecoveryRulesと原画像cropで行い、人間の確認を代行しない。

run_header.py／run_portable_header.pyは閉じたV6原観測の見出しだけに新しい英語推論を行う。
run_portable_tail.pyは閉じた失敗条件、run_portable_white_gap.pyは原画像の白帯で区切る別条件。
run_shared_ascii.pyは同じ白帯条件をInfrastructureのOnnxAsciiOcr／OcrWhiteGapPartitionへ移した
再現確認であり、新しい評価資料や精度加点ではない。登録後に正解を読んでroutingを変えない。
新しい出力フォルダだけを指定する。既存raw・freezeを上書きしない。

例：学習済みモデルと閉じたcohortをlocalに置き、各asset SHAを検証して実行する。

```sh
dotnet build tools/ocr-practical-documents/shared-ascii-probe/SharedAsciiProbe.csproj -c Release
python3 tools/ocr-practical-documents/run_shared_ascii.py --cohort /path/to/closed-cohort --portable-assets /path/to/portable-v1 --dotnet /path/to/dotnet --output /path/to/new-study
python3 tools/ocr-practical-documents/score_header.py --study /path/to/new-study --dotnet /path/to/dotnet
python3 -m unittest discover -s tools/ocr-practical-documents -p 'test_*.py'
```

ビルド成功を確認してからrunnerを開始する。原版の失敗も結果に残す。
字体・モデルの取得手順は既存ocr-physical-framing／ocr-model-comparisonと、
ocr-synthetic-training/ascii-english-v5-font-balancedを参照。
Pythonの描画はPillow／fontTools、ONNX推論は.NET同梱のCPU Runtimeを使用する。
make_runtime_fixture.pyはIntegration test用の定数出力ONNXを生成するだけで、学習モデルではない。
