# Releasing a new version

## Automatic (recommended): push a tag

1. Pick a version number, e.g. `1.2.3`.
2. Tag and push it:
   ```powershell
   git tag v1.2.3
   git push origin v1.2.3
   ```
3. That's it. `.github/workflows/release.yml` picks up the tag push and:
   - runs the full test suite,
   - publishes `LimitIO.Service`, `LimitIO.UI`, and `LimitIO.Installer.Helper` as self-contained, single-file `win-x64` binaries,
   - installs Inno Setup on the GitHub-hosted Windows runner and compiles `installer/LimitIO.iss` with the version baked in,
   - creates a GitHub Release for the tag with the compiled `LimitIO-Setup-1.2.3.exe` attached and auto-generated release notes from the commits since the last tag.

Watch it run under the repository's **Actions** tab. Once it finishes, the new release (and installer) appears on the **Releases** page automatically - nothing else to do.

## Manual fallback

If you need to build and publish a release yourself (no GitHub Actions access, or debugging a broken workflow), on a Windows machine with the .NET 8 SDK and [Inno Setup](https://jrsoftware.org/isinfo.php) installed:

```powershell
$version = "1.2.3"

dotnet publish src/LimitIO.Service/LimitIO.Service.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$version -o artifacts/service
dotnet publish src/LimitIO.UI/LimitIO.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$version -o artifacts/ui
dotnet publish src/LimitIO.Installer.Helper/LimitIO.Installer.Helper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$version -o artifacts/helper

& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\LimitIO.iss "/DAppVersion=$version"
```

This produces `installer\Output\LimitIO-Setup-1.2.3.exe`. To publish it:

- **Via the GitHub web UI**: go to the repository's Releases page -> "Draft a new release" -> choose or create the `v1.2.3` tag -> drag the `.exe` into the attachments area -> Publish.
- **Via the GitHub CLI**: `gh release create v1.2.3 installer/Output/LimitIO-Setup-1.2.3.exe --generate-notes`

## Versioning

There's no strict scheme enforced - `major.minor.patch` is a reasonable default (bump `patch` for fixes, `minor` for new features, `major` for breaking changes to the config/usage data format). The version number only needs to be unique per release; it flows into the installer filename, the `AppVersion` shown in Windows' "Add or remove programs", and the assembly version of the published binaries.
