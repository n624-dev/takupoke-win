# 独立生成する架空の横長時間割

学校原本・私的画像・元の描画命令を読まず、新規の座標・結合・授業配置を seed `20320819` から組み立てる。クラス集合は iOS 本体の `RecoveryValidator.specialClasses` に定義された17 canonical classes、曜日5群×時限8の680枠。509物理セル、111結合セル、33並記セル、76全空欄を含む。全ての値は「架空科目…」「架空教員…」「架空室…」、年度・学期は令和14年度／後期。フォントは公開 Noto Sans JP の固定リビジョン、SIL OFL 1.1。`FONT-LICENSE.txt` を保持する。

`unlabeled.pdf` が本体。科目・教員・教室はラベルなしの複数行で、学年／クラスの縦見出しと曜日／時限の横二層見出しを持つ。`labeled-control.pdf` は同じ配置・値に明示ラベルを付けた別 control。その成功を本体成功として数えない。末尾の独立した架空注記も取得対象に含む。

PDF・PNG・フォントのバイナリは Git に入れない。生成先はこのタスク専用の一時ディレクトリとし、処理が使用し終わった後で、その所有ディレクトリだけ削除する。CI でも生成コードを呼び、同じバイト列の PDF を再現する。

```bash
python3 -m pip install -r tools/independent-wide-timetable/requirements.txt
python3 -B tools/independent-wide-timetable/test_generator.py
python3 -B tools/independent-wide-timetable/generate.py \
  --source-root . --output /tmp/unique-owned-wide-timetable --images
```

既に取得した公開フォントを使う場合は `--font-file` を指定できる。指定ファイルも固定 SHA256 が一致しなければ拒否する。ネットワーク先は固定した公開フォント URL だけで、学校サイトへ接続しない。

Windows 側で同じ生成コードを使う場合は `--class-contract tools/independent-wide-timetable/canonical-class-source.txt` を指定する。このファイルは iOS 公開ソースのクラス定義1行だけで、実定義と byte 同値であることをテストする。別のクラス列や実資料を渡す入口ではない。Swift/C# の本番パーサー実装は共有しない。

`expected.json` は680個の `className`／`weekday`／`period`／`lessons` を持つ独立 literal oracle。結合セルは同じ原文 tuple を全ての該当時限へ展開し、並記は二つの tuple、全空欄は空配列、個別の教員・教室の空欄は空文字で表す。**Reader／Builder へ expected や generator の配置を渡さない。** 実 PDF だけを入力として取得した文字・座標・罫線から解析し、採用後に全680枠を assertion として照合する。一般の通常時間割で17クラス必須という新仕様は設けない。

正常な ReportLab 出力を本体回帰に残す。`*-no-unused-font.pdf` は未使用 Helvetica の初期 `Tf` だけを除いた診断用で、本文・座標・期待値は同じ。本体を Reader に通せず、この診断版だけ通っても解決としない。ReportLab の未使用 CMap NUL を生成側で削除しない。未使用の metadata と実際に表示した未対応文字を区別する Reader 修正を測定する。

`parallel-mismatch.pdf` は科目二つに対して教員一つの不整合。`unreadable-body.pdf` は授業本文を可視の不透明マークへ置き換えたもの。その印字を証明済み EMPTY と扱えない。`missing-class.pdf` は16クラスだけの別資料であり、その subset 自体をアプリの不正入力と断定しない。元の完全17クラス資料の取得途中でクラス証拠だけが落ちる場合は、実 Reader の成果から別途欠落安全性を確認する。

生成時の PyMuPDF 文字抽出は PDF の簡易健康確認に限る。iOS／Windows の実 Reader 成功、Strict／Recovery／Validator／Analysis の意味成功、画像版の実 OCR 成功、モデルの有用性は別に実測して記録する。実測を行うまで成功とは記載しない。
