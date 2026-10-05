# EasyOCR日本語・同一実画素170行の比較（2026-10-05）

独立架空の無ラベル時間割1画像から取得した同じ170行を、EasyOCR japanese_g2の一律設定で認識しました。時限40個はすべて一致しましたが、曜日5個と本文11項目に誤読が残りました。実Strictは40件を構成できたものの、その候補には原文と異なる本文が含まれ、正式復旧は成立していません。

|処理|本文の生文字＋診断位置一致|見出し一致|3値一致のセル|
|---|---:|---:|---:|
|今回EasyOCR|109/120|45/50|34/40|
|先行PARSeq100|120/120|39/50|40/40|

同一RGB・順序・原画素領域の170SHAを照合しています。EasyOCRは年度・学期・表題、学年・クラス、時限40個が一致し、月曜日→月曜H、火曜日→火健H、水曜日→水曜口、木曜日→木曜口、金曜日→余健Hを返しました。本文では試師31→試師3]など1の末尾誤読が反復しました。3値は局所診断で、クラス・曜日・時限と結び付いた完全tupleではありません。モデル別に正しい行を選んだり、日・数字・末尾を修復したりしていません。

実3b5b23bcのStrict→Builder→Rules/Engine/Validator5/正式変換へホスト再生しました。Strictは40件を返し、独立literal oracleとの比較では誤ったStrict候補1件です。Builderはクラス・日付・時限見出しの原文契約で拒否しました。Recoveryの正式候補0、誤ったV5正式候補0、実採用0、実行エラー0、negative分母0です。完全tupleは0 assessed /40 unavailableで、0/40正解率ではありません。Strictの誤った候補をconfidenceNoneや後段拒否によって隠しません。

罫線を除いた原画素のcoverageは満たしますが、主wireの箱は元の占有行pixel-support ROIです。予測word/character箱ではなく、文字数による箱分割もありません。公式custom_meanのsequence confidenceは未校正の診断値に留めています。実誤読の試師31→試師3]でも0.9886825を返したため、高いscoreは正しい原文の証明になりません。calibrated transcription confidenceやproduction acquisition契約は未成立で、productionAcquisitionEligible=falseです。nativeのsourceAdoptionEligible=trueは領域・出力取得状況だけを示します。

公式EasyOCR363afb184047ce452e436f4224f3098422df872e、japanese_g2 weight17,321,277 bytesのSHA、全2214公開charset、CPU torch2.5.1+cpu/torchvision0.20.1+cpuを事前固定しました。detector=False/download_enabled=False/quantize=False、greedy/ignore_char空/contrast_ths=0、batch1/workers0を全行に適用しました。contrast retry0とquantizeFalseは事前に宣言した一律設定で、結果を見て変更していません。公式RGB→gray/aspect resize/BICUBIC64高さ/right replicate paddingを保ち、正規化tensorのSHA・shape・範囲を保存しました。wordbeam辞書は使わず、greedyが参照しないことをsourceで確認しています。実170forward/170API返却/170serialization、空転記0・エラー0・detector0です。

最初のCI37253684409はpip checkでsetuptools不足により停止しました。画像準備・モデル取得・推論はいずれも0で、品質は未評価です。setuptools75.8.0をSHA固定して追加し、同じ設定・モデル・入力の運用再試行37254147617を1回だけ実行しました。元recipeと修正recipe、両full logを保持しています。再試行はUbuntu24.04/AMD EPYC7763、elapsed5.82秒、sampled own-worker RSS422,850,560 bytesです。モデルロードを含み、他の処理とは環境が異なるため速度や端末minimumRAMを一般化しません。CPU torchを共有作業環境に導入していません。

入力は3740×800の[既存独立架空source](../windows-unlabeled-ocr-development-20261005/fictional-source/)で、消費済み開発データです。EasyOCRとPARSeqはgray・resize・padding等も異なり、同一RGBでの差をネットワークだけの因果とは断定しません。未使用holdout、実PDFReader/WindowsRender、結合・並記・verifiedblank/negative全体品質は今回未評価です。Windowsネイティブ/.NETモデル導入やAI合格を主張しません。生ログはzlib+base64から元bytesへ復元できます。学校資料、画像、PDF、フォント、モデルweights、画像base64は同梱していません。source/upstream/LICENSEは公式Apache2.0原文を保持しています。
