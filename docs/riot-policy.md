# Riot 政策与公开发布门禁

## 身份声明

本工具不是 Riot Games 官方产品，未获得 Riot Games 背书，也不代表 Riot Games、其员工或相关方的观点。Riot Games、League of Legends 及相关名称、标识和商标归其各自权利人所有。

仓库维护者没有在本文件中声称产品已经登记。LCU 属于 unsupported/private 客户端接口，没有兼容性或稳定性承诺，任何客户端更新都可能使部分或全部功能失效。

## 公开分发前必须完成

维护者准备公开上传、发布或分发二进制时，必须：

1. 在 Riot Developer Portal 登记该产品，并确认登记状态适用于计划中的分发方式。
2. 阅读并遵守发布当时有效的 Riot Games 开发者政策、产品要求、商标和数据使用限制；仓库中的旧说明不能替代当前政策。
3. 重新执行测试、manifest 验证、隐私扫描和人工检查，不发布凭据、玩家原始响应或本机路径。
4. 只有完成上述确认后，才将发布环境中的 `RIOT_PRODUCT_REGISTRATION_CONFIRMED` 设为精确的小写 `true`，并以 `scripts/publish.ps1 -PublicRelease` 构建待发布文件。

缺少该变量或值不精确匹配 `true` 时，公开发布模式必须失败。该变量只是维护者确认门禁，不会自动登记产品，也不能证明政策合规。

## 当前 CI 行为

当前 Windows GitHub Actions 工作流只运行测试、单文件构建、manifest 提取和安全扫描。它不上传 GitHub Actions artifact，也不创建 GitHub Release，因此不会公开分发生成的 EXE。未来新增任何 artifact、Release 或其他公开上传步骤时，必须先执行公开发布模式，并把 `RIOT_PRODUCT_REGISTRATION_CONFIRMED=true` 作为必要门禁；不得用默认值绕过。
