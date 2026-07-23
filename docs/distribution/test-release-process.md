# 测试版发布流程

Snaploom 使用 `.github/workflows/release.yml` 从 `vX.Y.Z` 标签生成 Windows x64 与 macOS Apple Silicon 测试版。当前 macOS 包固定使用 ad hoc 签名；Windows 安装包不带代码签名。

## 发布前条件

1. 标签必须指向默认分支 `main` 可达的提交。
2. CI 必须已通过，性能与多设备兼容性 Issue `#18` 必须由维护者验收并以 `completed` 原因关闭；`not planned` 或其他关闭原因不能通过发布门禁。
3. 仓库中不能已有相同标签的草稿或公开 Release。
4. 面向仓库外用户发布前，仓库必须可公开访问。应用不会保存 GitHub 凭据，私有仓库的匿名 Releases API 无法用于“检查更新”。

满足条件后，由维护者创建并推送带注释的版本标签：

```bash
git switch main
git pull --ff-only
git tag -a v0.1.0 -m "Snaploom 0.1.0 test release"
git push origin v0.1.0
```

流水线会在两个平台重新执行完整无头测试，生成并验证安装包及 SHA256。发布作业只上传 EXE、DMG 和两个对应校验文件；发布页明确标记为 prerelease，并包含 SmartScreen、ad hoc 与 Gatekeeper 风险说明。

## 失败与重试

平台构建、测试或安装包验证失败时不会创建 Release。上传阶段先创建不可见草稿，四个制品全部存在且非空后才公开；上传或核对失败最多留下草稿，不会形成看似完整的公开 Release。

流水线不使用 `--clobber`。同标签已存在草稿或公开 Release 时，后续运行会停止，避免重复或覆盖错误版本。维护者应先检查失败日志和草稿内容；确认草稿确实属于失败的同一次发布后，手动删除草稿，再重新运行原标签工作流。不得移动或复用已经公开发布过的标签。

## Developer ID 升级路径

`scripts/build-macos-dmg.sh` 已将认证发布设计为严格模式：必须同时提供 `Developer ID Application` 身份、App Store Connect API Key 文件、Key ID 和 Issuer ID，并强制执行公证和 stapling；任何凭据或公证步骤失败都会停止，不能自动降级为 ad hoc。

取得证书后，把证书、私钥和公证凭据写入 GitHub Secrets，在受保护的发布环境中临时导入钥匙串并调用：

```bash
./scripts/build-macos-dmg.sh \
  --version 1.0.0 \
  --developer-id "Developer ID Application: Example (TEAMID)" \
  --notarize
```

升级流水线时必须同时把验证步骤切换为 `--expected-signing developer-id`，并通过 `codesign`、`stapler` 和 `spctl` 验证。未完成这些步骤前，发布页必须继续使用“ad hoc 测试版”描述。
