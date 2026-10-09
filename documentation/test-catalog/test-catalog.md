# テスト一覧・対象検索・更新強制・CI接続

対応ソース・テストのSHA-256：
`6598e9265d73e62c5d5da0be752c441a3de68b15ef262af35a3e3e18fa9b5dd1`

環境：Linux Python（隔離Gitリポジトリ）

```bash
python3 -B tests/test_test_catalog.py
```

## 変更時に確認するソース

- [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
- [scripts/test_catalog.py](../../scripts/test_catalog.py)
- [scripts/test_catalog_inventory.py](../../scripts/test_catalog_inventory.py)
- [tests/test-catalog.json](../../tests/test-catalog.json)

## [tests/test_test_catalog.py](../../tests/test_test_catalog.py)

- `test_changed_runtime_requires_corresponding_test_and_updated_document`（宣言行 62）
- `test_comments_and_formatting_do_not_qualify_as_a_test_change`（宣言行 73）
- `test_new_source_without_mapping_is_rejected`（宣言行 81）
- `test_unregistered_test_and_missing_registered_file_are_rejected`（宣言行 86）
- `test_deleting_tests_cannot_qualify_runtime_changes`（宣言行 95）
- `test_document_only_changes_do_not_require_test_edits`（宣言行 102）
- `test_renamed_source_keeps_the_previous_test_requirement`（宣言行 106）
- `test_new_corresponding_case_file_can_qualify_existing_runtime_changes`（宣言行 115）
- `test_stale_or_obsolete_generated_documents_are_rejected`（宣言行 124）
- `test_integration_case_can_cover_two_sources_without_duplicate_source_ownership`（宣言行 133）
- `test_search_finds_case_and_source_names`（宣言行 145）
- `test_inventory_distinguishes_declarations_from_theory_expansions`（宣言行 149）
- `test_workflow_enforces_changed_code_against_event_base`（宣言行 157）
