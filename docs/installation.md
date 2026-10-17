# Cài đặt và dùng Jev Review trong project của bạn

<<<<<<< HEAD
Hướng dẫn này dùng **code hiện có**, được kiểm tra ngày 14/10/2026 trên Windows với .NET SDK 10.0.303. Tool package, `init` và SDK core là [roadmap](superpowers/specs/2026-10-14-community-distribution-design.md), chưa phải tính năng đã phát hành. Lab cần .NET 10; project được review có thể dùng phiên bản .NET khác vì collection chỉ đọc Git commits.
=======
Hướng dẫn này mô tả cách build/publish DLL thủ công. V1 đã có Core và gói ZIP với installer/bốn skill; để cài vào project công ty, ưu tiên [quickstart v1](codex-v1-quickstart.md). `.NET tool`, `init` và public SDK package còn trong [roadmap](superpowers/specs/2026-10-14-community-distribution-design.md). Lab cần .NET 10; project được review có thể dùng phiên bản khác vì collection chỉ đọc Git commits.
>>>>>>> codex/refactor-v1

## 1. Build và kiểm tra offline

Cài Git và .NET SDK `10.0.303` hoặc patch mới hơn trong feature band `10.0.3xx`. Clone repo nếu chưa có, rồi chạy từ root lab:

```powershell
git clone https://github.com/long1712578/codex-jev-review-lab.git
Set-Location codex-jev-review-lab
dotnet restore JevReview.slnx
dotnet build JevReview.slnx --no-restore
dotnet test JevReview.slnx --no-restore
dotnet run --project src/JevReview.Cli --no-build -- doctor
dotnet run --project src/JevReview.Cli --no-build -- classify --dataset datasets/smoke --mode fake
```

Smoke expected: `total=4`, `classified=4`, `failedOrUnjudged=0`, `excludedWithReason=0`, `flagged=3`. API key không cần trong fake. Đường dẫn `output` in trong summary chứa `report.md`, `manifest.json`, `verdicts.jsonl`, `flagged.json`, `errors.jsonl`. Exit 0 chỉ nói pipeline chạy xong; reviewer vẫn xác minh.

## 2. Publish và dùng từ project khác

Tại root lab, publish một lần và giữ các đường dẫn:

```powershell
$jevLab = (Resolve-Path '.').Path
$jevPublish = Join-Path $jevLab 'runs/publish'
dotnet publish src/JevReview.Cli -c Release --no-restore -p:UseAppHost=false -o $jevPublish
$jevDll = Join-Path $jevPublish 'jev-review.dll'
$jevRubric = Join-Path $jevLab 'rubrics/refactor-review.v1.json'
$jevFake = Join-Path $jevLab 'fixtures/fake/dev-responses.json'
$jevPolicy = Join-Path $jevLab 'configs/policy.json'
```

Chọn Git root project của bạn và thay hai refs bằng commits/branches đang tồn tại:

```powershell
$targetRepo = 'D:\Projects\MyApp'
Set-Location -LiteralPath $targetRepo
dotnet $jevDll doctor --rubric $jevRubric
dotnet $jevDll classify --git $targetRepo --base main --head feature/refactor --merge-base --task 'Refactor without changing business behavior' --rubric $jevRubric --policy $jevPolicy --fake-responses $jevFake --mode fake
```

Chỉ review commits; staged, unstaged, untracked không nằm trong range. Manifest ghi resolved SHAs và merge-base semantics. Mỗi output directory phải mới/rỗng. `--output` có thể chọn vị trí riêng; nếu không chọn, artifacts nằm dưới `runs` của CWD.

<<<<<<< HEAD
Fake với Git item mới trả `insufficient_context`; đó là cách kiểm tra wiring, không phải review chất lượng. Bản hiện tại có [lỗi bracket pathspec R1](community-review-2026-10-14.md); range có tên file chứa metacharacter cần đọc diff thủ công cho tới khi regression/fix hoàn tất.
=======
Fake với Git item mới trả `insufficient_context`; đó là cách kiểm tra wiring, không phải review chất lượng. [R1 trong review ban đầu](community-review-2026-10-14.md) đã được sửa bằng literal pathspec và regression cho bracket/Unicode/spaces.
>>>>>>> codex/refactor-v1

## 3. MCP cho project riêng

MCP hiện chỉ nhận đường dẫn nằm trong workspace được cấu hình. Đặt bản sao config ở `.jev-review` trong project của bạn; đây là thao tác setup thủ công, chưa có `init`. Không overwrite config cũ:

```powershell
$jevProjectConfig = Join-Path $targetRepo '.jev-review'
if (Test-Path -LiteralPath $jevProjectConfig) {
    throw 'Project config exists; review and merge it manually.'
}
New-Item -ItemType Directory -Path $jevProjectConfig | Out-Null
Copy-Item -LiteralPath $jevRubric -Destination (Join-Path $jevProjectConfig 'rubric.json')
Copy-Item -LiteralPath $jevPolicy -Destination (Join-Path $jevProjectConfig 'policy.json')
Copy-Item -LiteralPath $jevFake -Destination (Join-Path $jevProjectConfig 'fake-responses.json')
```

Thêm table sau vào config Codex sau khi sửa absolute paths theo máy. Config có thể đặt trong `~/.codex/config.toml` hoặc `.codex/config.toml` của project theo [tài liệu MCP chính thức](https://learn.chatgpt.com/docs/extend/mcp?surface=cli). Giữ các table khác của bạn:

```toml
[mcp_servers.jev_review]
command = "dotnet"
args = ["D:/Tools/codex-jev-review-lab/runs/publish/jev-review.dll", "mcp", "--workspace", "D:/Projects/MyApp"]
cwd = "D:/Projects/MyApp"
```

Request `start_review` phải chỉ rõ config project vì defaults hiện còn dùng layout lab:

```json
{
  "git": ".",
  "base": "main",
  "head": "feature/refactor",
  "mergeBase": true,
  "task": "Refactor without changing business behavior",
  "mode": "fake",
  "rubric": ".jev-review/rubric.json",
  "policy": ".jev-review/policy.json",
  "fakeResponses": ".jev-review/fake-responses.json"
}
```

<<<<<<< HEAD
`start_review` trả `runId`; gọi `get_review_status`, sau đó `get_review_result`. `completed` nghĩa artifacts đã được ghi, cần đọc counts/flagged/errors. `failed` hiện chưa có diagnostics chi tiết. Chọn workspace là thư mục thật: [R2](community-review-2026-10-14.md) chứng minh root junction chưa được chặn dù descendant links có guard.
=======
`start_review` trả `runId`; gọi `get_review_status`, sau đó `get_review_result`. `completed` nghĩa job hoàn tất ghi artifacts, cần đọc counts/flagged/errors. `failed` hiện chưa có diagnostics chi tiết. Workspace root/ancestors/segments có junction hoặc symlink hiện bị từ chối; [R2](community-review-2026-10-14.md) là bằng chứng lỗi trước fix.
>>>>>>> codex/refactor-v1

Kiểm tra `codex mcp list` và `/mcp` trong TUI. Cấu hình này được đối chiếu docs; chưa chạy phiên UI Codex thực tế trong đợt review. Protocol offline đã có tests và probes.

## 4. Skill

Trong lab, skill hiện có tại `.agents/skills/jev-triage/SKILL.md`, dùng `$jev-triage`. Codex khám phá `.agents/skills` theo repo; xem [Build skills](https://learn.chatgpt.com/docs/build-skills).

<<<<<<< HEAD
Skill hiện viết theo đường chạy từ root lab. **Chỉ copy SKILL.md sang project khác chưa đủ setup.** Dùng MCP đã cấu hình ở trên hoặc gọi DLL bằng absolute paths. Phiên bản skill phân phối không phụ thuộc lab nằm trong roadmap; không cần sửa user-global skill/config để thử bản hiện tại.
=======
Skill `jev-triage` giữ cách chạy từ root lab. **Chỉ copy SKILL.md sang project khác chưa đủ setup.** Gói v1 cung cấp `$jevify` dùng paths/runtime đã cài và MCP/CLI fallback; xem [quickstart](codex-v1-quickstart.md). Không cần sửa user-global skill/config.
>>>>>>> codex/refactor-v1

Trong Codex đang mở project, có thể yêu cầu:

```text
Review the committed range main...feature/refactor with the jev_review MCP tools in fake mode, using .jev-review/rubric.json, .jev-review/policy.json and .jev-review/fake-responses.json. Read summary, flagged IDs and errors first. Verify selected changes against resolved commits, callers and tests. Keep predictions separate from confirmed/rejected/unresolved evidence. Fake results only test the workflow.
```

## 5. Live mode — người dùng chủ động bật

Chỉ thực hiện phần này khi bạn cho phép gửi dữ liệu tới TypeSafe và có ngân sách. Đặt key bằng input kín trong phiên PowerShell; không ghi key vào repo/config hoặc command arguments:

```powershell
$jevSecureKey = Read-Host 'TypeSafe API key' -AsSecureString
$env:TYPESAFE_API_KEY = [System.Net.NetworkCredential]::new('', $jevSecureKey).Password
try {
    dotnet $jevDll doctor --rubric $jevRubric --mode live
    dotnet $jevDll doctor --rubric $jevRubric --check-live
}
finally {
    Remove-Item Env:TYPESAFE_API_KEY -ErrorAction SilentlyContinue
}
```

`doctor --mode live` kiểm tra local; `--check-live` gửi một synthetic request có thể tính phí. Live classify dùng `--mode live` trong phiên có key. Live MCP cần thêm `--allow-live` vào args và `env_vars = ["TYPESAFE_API_KEY"]` trong table; biến phải tồn tại trong môi trường khởi động Codex. Không build/test PR không tin cậy trong host giữ key.

## 6. Đọc kết quả, cập nhật và gỡ setup

Luôn kiểm tra `total = classified + failedOrUnjudged + excludedWithReason`; flagged là tập chồng lấp. Dùng `inspect --run <folder> --id <id>` lấy prediction riêng; dùng `verify` ghi records đã đọc source/tests. API/schema error, excluded và unresolved đều còn cần review. Chi tiết options/exit codes trong [README](../README.md).

Để cập nhật bản clone, review thay đổi rồi restore/build/test/publish lại; giữ manifest và version/commit nguồn cho các runs cũ. Live resume cần model pin theo README. Không chạy lệnh `dotnet tool install JevReview.Tool` ở thời điểm này: repo chưa đóng gói hay publish package đó.

Để gỡ cấu hình, xóa đúng table `mcp_servers.jev_review` bạn đã thêm. Config project và runs là file của project; chỉ gỡ khi không cần lịch sử và sau khi kiểm tra nội dung. Công cụ hiện không có thao tác uninstall tự động.
