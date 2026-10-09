# 配布パッケージ・ライセンス・公開情報

対応ソース・テストのSHA-256：
`99d43847f9b66c7feb56bae12edd1232ca0825442569d632de683c4d77bb524a`

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

- `test_preserves_package_copyright_and_original_notice_with_expression`（宣言行 30）
- `test_non_nuget_icon_license_and_pinned_source_are_included_verbatim`（宣言行 51）
- `test_non_nuget_icon_notice_requires_license_and_provenance`（宣言行 62）
- `test_non_nuget_icon_notice_requires_pinned_revision`（宣言行 72）
- `test_refuses_license_path_outside_package`（宣言行 77）
- `test_unknown_license_requires_explicit_text`（宣言行 82）
- `test_file_license_is_included_verbatim`（宣言行 87）
- `test_excludes_verified_build_only_tools`（宣言行 106）
- `test_refuses_build_tools_with_runtime_assets`（宣言行 111）
- `test_refuses_build_tool_copied_outside_manifest`（宣言行 116）
- `test_build_only_exclusion_requires_manifest`（宣言行 123）
- `test_sdk_reference_package_uses_its_official_legacy_license`（宣言行 129）
- `test_legacy_license_url_does_not_waive_missing_license`（宣言行 137）
