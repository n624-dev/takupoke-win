# Windows標準OCR：元寸法の原行に対する初回

Windows Actions [38503472811](https://github.com/n624-dev/takupoke-win/actions/runs/38503472811)、
source `bcfbb38998a3cd265b7b1c58f640a1ef5fee0d5e`。
Windows10.0.26100/.NET10.0.13で公式OCR APIへ192原行を一回ずつ渡した。
PNG・復号BGRA・原寸法のハッシュは全て一致。アプリやiOSに接続しない研究のみ。

インストール済み認識言語はen-USだけで、日本語の機能は未導入だった。
この元寸法条件では、本文・英語見出しを含む186印字画像全てで文字を検出せず、空白6画像も空出力。
取得漏れを正解の空欄へ置き換えない。日本語モデルの精度は未測定であり、CI成功は導入合格ではない。
ピークWorkingSet54,681,600 bytes。APIにconfidenceがないためnullを保持した。

閉じたraw33681 bytes、SHA256 `d0180f37ce303b5e2f95ce3b3e54790dbfc6b7bcab4dd1447fc56360944fd0a7`。
Actionsの行単位JSONから元の改行とbyte数・SHAを検証して再構築した。
空stderrも保存し、実行時のソースをGit固定commitからfreezeに照合した。

次条件は日本語のOS機能を導入し、全原画素を3x3反復・固定32px白余白へ変換して測定する。
原入力の未検出を隠す再実行ではなく、別recipeの既消費開発条件として保存する。
元画素を落とさず、モデルの文字・箱を修正しない。新条件の精度はこの記録に加算しない。
