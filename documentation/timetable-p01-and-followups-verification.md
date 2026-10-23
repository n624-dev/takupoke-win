# 通常時間割P01とiOS追加issueの調査

## W16: 利用者の通常時間割P01報告

2026-10-23、利用者から「winの通常時間割が厳格な解析でP01になる」と報告された。直近公開版は0.1.0-dev.10455 / e001729。報告時点で利用者の実際の版・PDF・P01発生枝・OSは未確認。公開済み可視性28件・全Integration215件の成功は、この資料の通過証拠にはならない。

対処を決める前に観測を記録した。直前の可視性変更は同色文字・背景/描画状態が未確認の場合をP01で拒否する。過去のW15はテスト呼出し引数不足によるコンパイル失敗であり、この利用者資料の実行時P01とは異なる。既存PDFの採否・各拒否条件を照合し、不可視文字の検証を外すだけの回避は行わない。

## W17: 復旧処理テストのコンパイル失敗（初回）

2026-10-23、Debian 13 / .NET SDK 10.0.402。作業ブランチ codex/fix-timetable-p01-and-issues-60-63（基点 fb83d74 / 公開コード e001729）の未コミット変更に対し、`dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --configuration Release --filter FullyQualifiedName~RetentionRecoveryTests` を実行した。テスト実行前に AppViewModel.cs:261 が CS0165（ローカル変数 results が未割り当て）でコンパイル失敗した。Core / Infrastructure はビルド済み。ログは作業環境の /tmp/followup-win-retention-first.log。既存の CS8602 / xUnit2031 警告も出力されたが、今回の停止理由は CS0165。修正方針の決定前にこの観測を保存する。

公開コード e001729 の UpdateSharedAsync と W15 の引数不足によるコンパイル失敗記録を照合した。今回は共通 RunAsync の型付き例外処理を追加する際、共有更新内部の try にも同じ catch を挿入し、結果を割り当てず処理を継続できる経路を作った。内部 catch を取り除いて元の例外伝播を維持し、共通 RunAsync だけで復旧案内を表示する。共有更新の結果・APIエラー・キャンセル検証は緩めない。修正と記録を隔離ブランチへ push してから再試行する。
