param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z') {
    throw 'A stable version such as 1.0.0 is required. Development versions cannot be submitted to Microsoft Store.'
}
$components = $Version.Split('.')
foreach ($component in $components) {
    if ($component.Length -gt 5 -or [int]$component -gt 65535) { throw 'Each MSIX version component must be at most 65535.' }
}
if ([int]$components[0] -eq 0) { throw 'The first MSIX version component must be greater than zero.' }
return "$Version.0"
