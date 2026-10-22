# 公開後に追加された Windows issue の検証

## 対象と試行記録

初回公開: 0.1.0-dev.10454 / main 008b108ffc65738150aea76d3044e82f4cc743a2。
追加 issue #6、#7、#9、#10 を実装し、#4、#5、#8、#11–14 は公開済み修正との対応を再確認する。

### W15: PDF 可視性テスト初回のコンパイル失敗

- ソース: 008b108 を基点とする未コミット差分、`codex/fix-post-release-issues-6-7-9-10`。
- 実行: 2026-10-22 15:46 UTC、Linux Debian 13 / .NET SDK 10.0.402、`dotnet test tests/Takupoke.Integration.Tests/Takupoke.Integration.Tests.csproj --filter FullyQualifiedName~PdfVisibilityTests`。
- 段階: コンパイル。テストは未実行。
- 観測: PdfVisibilityTests.cs の AnalysisMatches 呼出し2箇所が CS7036。必須 schoolYear 引数が無い。既存警告 CS8602 / xUnit2031 も表示された。
- ログ: 作業環境 `/tmp/post-release-pdf-visibility-first.log`。PDFの採否・描画結果についてはまだ判断できない。

W15 対処前に既存 MaterialCoordinatorTests の AnalysisMatches 呼出しと実装を確認した。
過去の change-row-exclusion-verification.md の共有fixture import不足は別のコンパイル失敗であり、今回の引数不足とは条件が異なる。
テストの入力年度2032を必須引数へ明示する。期待値・可視性条件・製品実装はこの修正では変更しない。
次の試行前に、この観測と修正を自動CI対象外の作業ブランチへpushする。
