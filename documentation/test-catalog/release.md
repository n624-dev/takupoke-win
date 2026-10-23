# 配布パッケージ・ライセンス・公開情報

対応関係・宣言名・実行方法のSHA-256：
`a17fd731a855899b962f5a514b7f490a75cbc52beccdab1e0428d54855a93419`

環境：Linux Python／Windows包装・導入検証

```bash
python3 -B tests/package-notices-tests.py
python3 -B tests/test_store_release.py
./scripts/test-store-package.ps1 -Version 1.0.0
./scripts/test-store-installation.ps1 -Version 1.0.0
./scripts/test-development-package.ps1
```

## 変更時に確認するソース

- [.github/workflows/development-package.yml](../../.github/workflows/development-package.yml)
- [.github/workflows/microsoft-store.yml](../../.github/workflows/microsoft-store.yml)
- [Directory.Build.props](../../Directory.Build.props)
- [Takupoke.slnx](../../Takupoke.slnx)
- [global.json](../../global.json)
- [scripts/build-development-package.ps1](../../scripts/build-development-package.ps1)
- [scripts/build-store-package.ps1](../../scripts/build-store-package.ps1)
- [scripts/store-version.ps1](../../scripts/store-version.ps1)
- [scripts/test-store-package.ps1](../../scripts/test-store-package.ps1)
- [scripts/test-store-installation.ps1](../../scripts/test-store-installation.ps1)
- [scripts/check-public-tree.py](../../scripts/check-public-tree.py)
- [scripts/collect-package-notices.py](../../scripts/collect-package-notices.py)
- [scripts/publish-development-release.ps1](../../scripts/publish-development-release.ps1)
- [scripts/test-development-package.ps1](../../scripts/test-development-package.ps1)
- [src/Takupoke.Core/Takupoke.Core.csproj](../../src/Takupoke.Core/Takupoke.Core.csproj)
- [src/Takupoke.Infrastructure/Takupoke.Infrastructure.csproj](../../src/Takupoke.Infrastructure/Takupoke.Infrastructure.csproj)
- [tests/Takupoke.Core.Tests/Takupoke.Core.Tests.csproj](../../tests/Takupoke.Core.Tests/Takupoke.Core.Tests.csproj)

## [tests/package-notices-tests.py](../../tests/package-notices-tests.py)

- `test_preserves_package_copyright_and_original_notice_with_expression`
- `test_non_nuget_icon_license_and_pinned_source_are_included_verbatim`
- `test_non_nuget_icon_notice_requires_license_and_provenance`
- `test_non_nuget_icon_notice_requires_pinned_revision`
- `test_refuses_license_path_outside_package`
- `test_unknown_license_requires_explicit_text`
- `test_file_license_is_included_verbatim`
- `test_excludes_verified_build_only_tools`
- `test_refuses_build_tools_with_runtime_assets`
- `test_refuses_build_tool_copied_outside_manifest`
- `test_build_only_exclusion_requires_manifest`
- `test_sdk_reference_package_uses_its_official_legacy_license`
- `test_legacy_license_url_does_not_waive_missing_license`

## [tests/test_store_release.py](../../tests/test_store_release.py)

- `test_stable_versions_keep_the_store_revision_zero`
- `test_development_ambiguous_and_out_of_range_versions_are_rejected`
- `test_both_package_architectures_are_inspected`
- `test_windows_runtime_file_names_are_case_insensitive`
- `test_wrong_binary_architecture_cannot_pass_as_x64`
- `test_missing_compiled_ui_or_notices_are_rejected`
- `test_wrong_store_version_is_rejected`
- `test_machine_installed_dotnet_cannot_hide_a_framework_dependent_package`
- `test_certificate_trust_is_never_changed_on_a_developer_computer`
