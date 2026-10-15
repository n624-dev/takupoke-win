# PDFを必要としない英数字OCR比較

独立した架空のコード画像で、公式英語V5・配布中の学習済み英語V5・日本語V6を比較する。
モデル・辞書・字体のSHA、生成条件、文字列の正解、実行バイナリを推論前に固定する。
推論プロセスには画像とモデルのmanifestだけを渡す。正解との比較はworker終了後に行う。
推論は.NETの実際の`OcrInputTransform`・`OcrCtcDecoder`とCPU ONNX Runtimeを使用する。

```sh
python tools/ocr-code-comparison/run.py --known-fonts /path/to/sawarabi-fonts --official /path/to/official-english-v5 --bundle /path/to/installed-models --dotnet /path/to/dotnet --output /path/to/new-exploration
python tools/ocr-code-comparison/run.py --validation --exclude-oracle /path/to/new-exploration/oracle.json --known-fonts /path/to/sawarabi-fonts --official /path/to/official-english-v5 --bundle /path/to/installed-models --dotnet /path/to/dotnet --output /path/to/new-validation
```

PythonはPillow 12.3.0、.NETは10を使用した。字体は固定したGoogle Fontsコミットから取得する。
モデルのSHAはコード中で検査する。画像・字体・モデル・ビルド出力はリポジトリ外に保管する。
既存フォルダ、raw、oracleを上書きしない。

`--validation`は新しい文字列を生成し、実際の`OcrAsciiRefinement`にも渡す。
最初の比較で使用した字体なので、未使用字体による汎化検証とは呼ばない。
2pxと6pxの余白は同じ画像から作る対条件で、件数を足して独立標本数としない。
単体文字の墨が一致しても、送り幅や文字列全体が識別不能であるとは断定しない。

変更前後の比較では、同じCodeProbeと入力で変更前のInfrastructureを用いる別workerを実行する。
旧readerのコピーを`baseline-reader`に置き、`baseline-freeze.json`へ全バイナリと入力のSHA、
旧ソースコミットを登録してから実行する。成功終了とrawのSHAを`baseline-receipt.json`に残す。
新旧両workerが閉じてから`compare_routes.py --study /path/to/new-validation`を実行する。
日本語の生出力を変更前の製品結果とみなさず、既存の日本語接頭辞・英数字末尾処理も比較に含める。

この試験は文字認識の部品評価。検出、PDF描画、欄割当て、文書全体の成功率、
利用者の照合・訂正時間や本番可否を実証するものではない。

`--seed`で新しい文字列のseedを指定できる。`--font-assets`は以前の取得receiptのSHAと照合した字体・ライセンスの再利用用。
`--validation --room-candidates`は、推論前に約半数の正解コードと64の未使用コードから架空の名称対応表を凍結し、
製品の候補関数を同じ画像の実際のroute出力へ適用する。正候補の提示、未知コードへの候補、
既に正しい出力への誤誘導を別記する。先頭候補の自動採用は比較上の危険性の診断だけで、製品へ適用しない。
[2026-10-16の結果](../../research-results/ocr-room-candidates-20261016/README.md)。
