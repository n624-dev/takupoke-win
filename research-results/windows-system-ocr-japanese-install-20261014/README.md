# 日本語Windows OCR機能の導入失敗

[Actions38503900381](https://github.com/n624-dev/takupoke-win/actions/runs/38503900381)、
source `5bc03ac6032759960966458b936e4854af27e4a2`。
公式OS機能`Language.OCR~~~ja-JP~0.0.1.0`はNotPresent。
`Add-WindowsCapability -Online`が8分上限に達し、認識処理を開始せず終了した。
nativeモデル呼出し0、精度は未評価。資料はこの取得へ送らず、公開機能名だけを渡した。

元の導入stepログとreceiptを保持する。作業ディレクトリの清掃は成功した。
Windows利用者の日本語環境でも導入できないと一般化せず、日本語OCRの不合格には数えない。
同じ取得を繰り返さず、別実行で導入済み英語OCRの拡大入力を測定した。
