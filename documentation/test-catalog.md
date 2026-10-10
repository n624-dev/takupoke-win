# テスト一覧と変更時の必須手順

この一覧はソースの宣言と対応表から生成する。成功件数や品質合格の記録ではない。
Theoryの入力展開・条件付きskip・CI実行件数は別の検証記録で確認する。
同じテストを複数分類から参照するため、分類別宣言数の合計は実行件数ではない。
対応付けは変更時に確認すべきテストを示し、各ファイルの全動作を検証済みとは保証しない。

1. 編集前に対象ファイル名・機能・ケース名で下記のsearchを実行する。
2. 新仕様・未検出の不具合にはテストを更新する。既存ケースで十分なら不要な編集をしない。
3. 該当ケース・十分性の理由・実行環境・結果を検証記録へ残す。
4. 対応関係・宣言名・実行方法が変わったらwriteし、check --baseで対象を確認する。
   未登録コード・未登録テスト・古い一覧はCIを失敗させる。
   テスト本文の編集は一律に要求しない。十分性は変更内容と実行結果から確認する。
   一覧の再生成はテスト実行の代わりにならない。

```bash
python3 scripts/test_catalog.py search 曜日
python3 scripts/test_catalog.py write
python3 scripts/test_catalog.py check --base <編集前のコミット>
```

CIはpushのbefore、PRのbase、手動実行ではHEADの親を比較する。
ファイルの削除や移動でも以前の対応テストを検査する。
取得・解析・保存をまたぐテストは、関連する複数の対象から参照する。

| 対象 | 宣言数 | 検証する環境 |
|---|---:|---|
| [時間割変更・曜日・行除外・原文保持](test-catalog/xlsx.md) | 26 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [原本取得・更新・保存・監視](test-catalog/material.md) | 18 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [暗号化DB・原子保存・設定・年度期限](test-catalog/storage.md) | 21 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [API・年度別行事・更新失敗](test-catalog/api.md) | 22 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [OIDC・実認証・取消](test-catalog/auth.md) | 5 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [リンク・検索・名称対応](test-catalog/links.md) | 8 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [通知・差分・実配信](test-catalog/notifications.md) | 16 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [時間割・授業名・時刻・日付](test-catalog/timetable.md) | 23 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [Strict PDF・文字・罫線・試験・返却](test-catalog/pdf.md) | 13 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [固定iOS参照・独立期待値](test-catalog/parity.md) | 8 | Linux／Windows .NET（WinUI操作はWindows限定） |
| [WinUI画面・行選択・表示領域・実操作・再起動](test-catalog/app-ui.md) | 7 | Windows WinUI x64。Linuxクロスビルドは操作成功に数えない |
| [配布パッケージ・ライセンス・公開情報](test-catalog/release.md) | 13 | Linux Python／Windows包装・導入検証 |
| [テスト一覧・対象検索・宣言整合性・CI接続](test-catalog/test-catalog.md) | 14 | Linux Python（隔離Gitリポジトリ） |
