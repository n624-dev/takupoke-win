# 配布パッケージ・ライセンス・公開情報

対応関係・宣言名・実行方法のSHA-256：
`c3e775ff909a10d1d005391d8467e2c8f9a52c48c9654a1a5987537d69df6686`

環境：Linux Python／Windows包装・導入検証

```bash
python3 -B tests/package-notices-tests.py
./scripts/test-development-package.ps1
```

## 変更時に確認するソース

- [.github/workflows/development-package.yml](../../.github/workflows/development-package.yml)
- [Directory.Build.props](../../Directory.Build.props)
- [Takupoke.slnx](../../Takupoke.slnx)
- [global.json](../../global.json)
- [scripts/build-development-package.ps1](../../scripts/build-development-package.ps1)
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
