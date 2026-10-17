# Codex + Jev Skills v1 — hướng dẫn dùng trong project công ty

Gói ZIP nội bộ cung cấp bốn skill gọi tường minh, CLI và MCP trên cùng review core. Máy chạy tool cần .NET 10 runtime; máy build cần SDK 10.0.303 theo `global.json`. Git cần cho review commit. Installer cần PowerShell 7; ứng dụng đang được review có thể dùng ngôn ngữ hoặc .NET version khác.

## 1. Tạo hoặc nhận gói release

Từ checkout source:

```powershell
pwsh -NoProfile -File scripts/New-CodexRelease.ps1
```

Mặc định script restore, build/test Release và smoke fake, rồi tạo `runs/releases/1.0.0/codex-jev-1.0.0.zip` cùng `.sha256`. Output directory phải mới/rỗng. `-OutputDirectory` chọn đường dẫn khác. `-SkipChecks` chỉ dùng trong package integration tests hoặc khi đã chạy checks tương đương; cần build đúng Configuration trước đó. Không dùng switch này để thay đổi kết luận về các checks đã fail.

So sánh SHA-256 với file checksum nhận từ nguồn phân phối tin cậy, rồi giải nén. Checksum kiểm tra tính toàn vẹn, không chứng minh danh tính người phát hành. Giữ toàn bộ runtime files, gồm DLL CLI, Core, `.deps.json` và `.runtimeconfig.json`.

## 2. Cài vào Git root của project

Ví dụ thay paths bằng thư mục thật trên máy:

```powershell
Expand-Archive -LiteralPath 'D:/Downloads/codex-jev-1.0.0.zip' -DestinationPath 'D:/Tools/codex-jev-1.0.0'
pwsh -NoProfile -File 'D:/Tools/codex-jev-1.0.0/scripts/Install-CodexJev.ps1' -ProjectPath 'D:/Projects/MyApp' -WhatIf
pwsh -NoProfile -File 'D:/Tools/codex-jev-1.0.0/scripts/Install-CodexJev.ps1' -ProjectPath 'D:/Projects/MyApp'
```

Installer mặc định lấy PackageRoot là thư mục cha của `scripts`; dùng `-PackageRoot` khi cần chỉ rõ. Nó kiểm tra hashes, Git root, linked paths và collisions trước khi ghi. Có `.jev-review` hoặc một skill cùng tên thì dừng để bạn review/merge thủ công. Nó giữ nguyên AGENTS.md và config Codex. Files được cài nằm trong `.jev-review/installation.json`.

Không có PowerShell 7: copy bốn thư mục `.agents/skills` từ ZIP; copy toàn bộ `runtime` vào `.jev-review/runtime`; rubric thành `.jev-review/rubric.json`, policy thành `policy.json`, fake fixture thành `fake-responses.json`, smoke dataset thành `.jev-review/smoke/items.json`. Kiểm tra collisions trước khi copy. Viết snippet MCP theo bước tiếp theo với paths đúng của máy. Cách thủ công cần giữ manifest release và tự ghi lại files đã tạo để rollback.

## 3. Kết nối MCP với Codex

Mở `.jev-review/codex-mcp.toml` do installer tạo. Merge table này vào `.codex/config.toml` của project hoặc config đang dùng; giữ các table cũ. Snippet dùng absolute paths theo máy. Project config phải được Codex tin cậy. Xem [MCP chính thức](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

Mở Codex trong project. Kiểm tra `/skills` có bốn skill và `/mcp` có `jev_review`; CLI có thể dùng `codex mcp list`. Nếu skill chưa hiện, khởi động lại Codex. Skill metadata đặt `allow_implicit_invocation: false`, nên hãy gọi bằng `$tên-skill`. [Codex skill discovery và invocation](https://learn.chatgpt.com/docs/build-skills) là cơ chế sử dụng; đây không phải bare-word matcher của oh-my-pi.

MCP có `start_review`, `get_review_status`, `get_review_result`. `inspect` và `verify` dùng CLI. MCP mode fake là mặc định; muốn chạy live cần setup riêng bên dưới.

## 4. Demo offline trước khi review thật

Chạy từ project đã cài:

```powershell
dotnet .jev-review/runtime/jev-review.dll doctor --rubric .jev-review/rubric.json
dotnet .jev-review/runtime/jev-review.dll classify --dataset .jev-review/smoke --rubric .jev-review/rubric.json --policy .jev-review/policy.json --fake-responses .jev-review/fake-responses.json --mode fake
```

Smoke có counts dự kiến: total 4, classified 4, failedOrUnjudged 0, excludedWithReason 0, flagged 3. Summary in đường dẫn output; đọc `manifest.json`, `flagged.json`, `errors.jsonl` và `report.md`. Fake chỉ dùng responses định trước; không suy luận code hay đo chất lượng Jev.

## 5. Gọi bốn skill

```text
$ultrathink điều tra failure modes của thay đổi API này và kiểm chứng phương án sửa nhỏ nhất.
$orchestrate review các module độc lập, giao ownership rõ và tổng hợp findings có evidence.
$workflowz thực hiện migration theo docs/plan.md, ghi tiến độ từng phase và chỉ hoàn tất sau khi checks pass.
$jevify review committed range main...feature/refactor; mục tiêu là refactor không đổi business behavior. Dùng fake để kiểm tra setup, đọc summary/flagged/errors trước rồi xác minh các item cần review.
```

`ultrathink` không đổi model/reasoning effort. `orchestrate`/`workflowz` dùng subagent chỉ khi client có tools và chính sách cho phép; nếu thiếu thì chạy tuần tự. Skill không cung cấp oh-my-pi `eval`, `agent()` hoặc `workpool()`. Những lời gọi này không thể vượt sandbox/approval của host.

## 6. Review Git và ghi evidence

```powershell
dotnet .jev-review/runtime/jev-review.dll classify --git . --base main --head feature/refactor --merge-base --task 'Refactor without changing business behavior' --rubric .jev-review/rubric.json --policy .jev-review/policy.json --fake-responses .jev-review/fake-responses.json --mode fake
```

Chỉ committed range được thu; staged/unstaged/untracked bị loại. Manifest ghi `baseSha`, `headSha`, `diffBaseSha` và semantics. `--merge-base` tương ứng review từ divergence với base; bỏ flag khi cần endpoint diff. Fake trên Git ID chưa có fixture trả `insufficient_context`; mọi item đó vẫn cần reviewer đọc.

Với output folder và ID lấy từ summary/flagged, gọi:

```powershell
dotnet .jev-review/runtime/jev-review.dll inspect --run runs/<run-id> --id <item-id>
dotnet .jev-review/runtime/jev-review.dll verify --run runs/<run-id> --file verification-records.json
```

Trong file records là JSON array. Một record unresolved hợp lệ:

```json
[{"id":"<item-id>","status":"unresolved","summary":"Missing implementation of the replaced call.","evidence":[]}]
```

Confirmed/rejected cần evidence gồm path, line >= 1 và description từ code/tests thực sự đã đọc; confirmed còn cần findingLabel hợp lệ. Khi cập nhật, giữ các records cũ: `verify` thay toàn bộ verification array đã cung cấp. Không dùng probability làm evidence. Đọc Git source ở SHAs trong manifest và dùng CodeGraph trước nếu repo có index. Input đổi thì tạo run mới.

## 7. Live mode do team chủ động bật

Chỉ dùng dữ liệu được phép gửi tới TypeSafe. Nhập key kín vào environment ở host được phép; không đưa key vào arguments, config, Git hoặc chat. CLI thêm `--mode live --model jev-latest`. `doctor --check-live` gửi request tổng hợp có thể tính phí, nên chỉ chạy khi được yêu cầu.

Live MCP cần thêm `--allow-live` vào args và `env_vars = ["TYPESAFE_API_KEY"]` trong table. Environment phải tồn tại lúc Codex khởi động. Không chạy build/tests/mã PR không tin cậy trong host có key. Default package checks, CI và smoke chỉ chạy offline; helper loại key khỏi các dotnet subprocess đó.

## 8. Đọc kết quả, cập nhật và rollback

Luôn kiểm tra `total = classified + failedOrUnjudged + excludedWithReason`; flagged chồng lấp terminal states. Exit 0 nghĩa pipeline hoàn tất, không chứng minh PR an toàn. Exit 1 còn unjudged/excluded, exit 2 là lỗi input/config, exit 130 là cancellation. Evidence và coverage của reviewer cần đọc riêng.

Giữ ZIP, checksum và release/installation manifests để pin nội dung. Để update, review gói mới và merge các files do installation manifest quản lý; installer v1 không overwrite. Để rollback, gỡ đúng MCP table đã thêm và chỉ files đã xác minh thuộc installation manifest; giữ review runs nếu còn cần lịch sử. Không xóa recursive một thư mục có files của team.

MCP v1 thích hợp pilot theo phiên: terminal-job dictionary chưa có giới hạn lịch sử. CLI process riêng phù hợp nhiều runs. Hard kill chưa có checkpoint/atomic completion toàn bộ; run thiếu artifacts là incomplete và cần chạy lại. Xem [release notes](codex-v1-release-notes.md).
