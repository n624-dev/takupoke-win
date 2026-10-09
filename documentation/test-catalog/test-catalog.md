# テスト一覧・対象検索・宣言整合性・CI接続

対応関係・宣言名・実行方法のSHA-256：
`74eaf9ebd6c2d636087b1853f56670f6c2bcea5394ef1d6630d9024f0c0e5186`

環境：Linux Python（隔離Gitリポジトリ）

```bash
python3 -B tests/test_test_catalog.py
```

## 変更時に確認するソース

- [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
- [scripts/test_catalog.py](../../scripts/test_catalog.py)
- [scripts/test_catalog_inventory.py](../../scripts/test_catalog_inventory.py)
- [tests/test-catalog.json](../../tests/test-catalog.json)
- [.github/workflows/test-tools.yml](../../.github/workflows/test-tools.yml)

## [tests/test_test_catalog.py](../../tests/test_test_catalog.py)

- `test_changed_runtime_reports_existing_tests_without_forcing_edits`
- `test_body_comments_and_line_changes_do_not_make_the_index_stale`
- `test_new_source_without_mapping_is_rejected`
- `test_unregistered_test_and_missing_registered_file_are_rejected`
- `test_deleting_a_registered_test_is_still_rejected`
- `test_document_only_changes_do_not_require_test_edits`
- `test_renamed_source_reports_corresponding_tests_without_test_edits`
- `test_new_corresponding_case_file_can_qualify_existing_runtime_changes`
- `test_stale_or_obsolete_generated_documents_are_rejected`
- `test_integration_case_can_cover_two_sources_without_duplicate_source_ownership`
- `test_search_finds_case_and_source_names`
- `test_inventory_distinguishes_declarations_from_theory_expansions`
- `test_renamed_case_and_changed_execution_command_require_index_update`
- `test_workflow_enforces_changed_code_against_event_base`
