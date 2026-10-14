# Codex + Jev

Bộ skill và công cụ review refactor dành cho các team đang dùng Codex. Cài vào Git repo của bạn để điều tra thay đổi khó, điều phối công việc, thực hiện kế hoạch theo từng bước và review diff với báo cáo có bằng chứng.

**V1.0.0 · Gói ZIP nội bộ · Skill theo project + CLI/MCP · .NET 10**

[Lợi ích](#benefits) · [Cài đặt](#install) · [Sử dụng](#usage) · [Live mode](#live) · [Khắc phục lỗi](#troubleshooting) · [Build từ source](#development)

<a id="benefits"></a>

## Vì sao dùng trong project của bạn?

| Nhu cầu của team | Khả năng hiện có | Lợi ích khi áp dụng |
| --- | --- | --- |
| Những task khó cần điều tra trước khi sửa | `$ultrathink` kiểm tra source, caller, failure modes và phương án sửa | Giúp quyết định dựa trên bằng chứng và giữ phạm vi sửa phù hợp |
| Nhiều phần việc độc lập | `$orchestrate` phân công ownership, tổng hợp kết quả và kiểm tra tích hợp | Giúp chia việc rõ ràng và phát hiện kết quả mâu thuẫn giữa các phần |
| Migration/refactor gồm nhiều bước phụ thuộc | `$workflowz` ghi mục tiêu, tiến độ, đầu ra và checks của từng phase | Giúp team tiếp tục công việc từ trạng thái đã ghi và biết phần nào còn chưa hoàn tất |
| PR được mô tả là “chỉ refactor” | `$jevify` thu committed diff, phân loại theo rubric/policy, rồi xác minh các item flagged | Giúp tập trung review vào thay đổi rủi ro hoặc thiếu context theo tiêu chí thống nhất |
| Cần bàn giao và truy lại kết quả review | Manifest lưu SHAs, hashes, config/model; report tách prediction khỏi verification | Giúp reviewer biết đã review revision nào, kết luận nào có evidence và phần nào còn bỏ ngỏ |

CLI và MCP dùng cùng review core; bạn có thể chạy lệnh trong terminal hoặc để Codex gọi tool theo cùng quy trình. Demo offline giúp kiểm tra bộ cài và artifacts mà không cần TypeSafe API key.

Ví dụ: team đổi tên và tách logic ở nhiều file, nhưng một diff vô tình đổi điều kiện authorization. Policy có thể đưa path nhạy cảm vào danh sách cần đọc; reviewer tiếp tục kiểm tra implementation, caller và tests, rồi ghi finding với path/dòng/bằng chứng. Prediction `refactor_only` vẫn cần được đối chiếu theo quy trình của team.

Phù hợp khi team cần review refactor theo tiêu chí cố định và lưu lịch sử review. Với vài file nhỏ, review trực tiếp có thể đơn giản hơn. **Chưa có live benchmark chứng minh tiết kiệm thời gian, chi phí hoặc cải thiện độ chính xác.** Xem [lợi ích và cách đo](docs/comparison.md).

<a id="install"></a>

## Cài vào project của bạn

### Chuẩn bị

| Thành phần | Khi nào cần |
| --- | --- |
| Codex chạy local, hỗ trợ repo skills | Dùng bốn skill; hỗ trợ MCP nếu muốn gọi review tools trong Codex |
| [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Chạy CLI/MCP từ ZIP; không cần SDK để dùng gói đã build |
| [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell) (`pwsh`) | Chạy installer và các ví dụ bên dưới |
| [Git](https://git-scm.com/downloads/) và một Git repo có sẵn | Installer cần đường dẫn đúng Git root; review Git cần commits |

Mở **PowerShell 7**. Kiểm tra các công cụ có trong PATH:

```powershell
pwsh --version
git --version
dotnet --list-runtimes
```

Danh sách runtimes cần có `Microsoft.NETCore.App 10.0.x`. Project được review có thể dùng C#, JavaScript, Java hoặc ngôn ngữ khác; không cần nâng runtime của ứng dụng lên .NET 10.

### 1. Nhận gói ZIP và kiểm tra checksum

Nhận `codex-jev-1.0.0.zip` và `codex-jev-1.0.0.zip.sha256` từ maintainer/kênh nội bộ tin cậy. V1 hiện phát hành bằng ZIP; nếu bạn có source v1, xem [cách tạo gói](#development).

Thay **hai đường dẫn** đầu tiên bằng vị trí thật trên máy. `$jevProject` phải là Git root của project đích; dùng `git -C 'D:/Projects/MyApp' rev-parse --show-toplevel` để kiểm tra. Chạy các bước cài đặt trong cùng một cửa sổ PowerShell:

```powershell
$jevZip = 'D:/Downloads/codex-jev-1.0.0.zip'
$jevProject = 'D:/Projects/MyApp'
$jevPackage = Join-Path ([IO.Path]::GetTempPath()) ('codex-jev-' + [Guid]::NewGuid().ToString('N'))

$jevExpectedHash = ((Get-Content -LiteralPath ($jevZip + '.sha256') -Raw).Trim() -split '\s+')[0]
$jevActualHash = (Get-FileHash -LiteralPath $jevZip -Algorithm SHA256).Hash
if ($jevActualHash -ine $jevExpectedHash) { throw 'ZIP checksum mismatch; obtain the package again.' }

Expand-Archive -LiteralPath $jevZip -DestinationPath $jevPackage
```

Checksum giúp kiểm tra tính toàn vẹn; nguồn cung cấp gói xác định người phát hành. ZIP chứa `scripts/`, `runtime/`, `.agents/` và `release.json` ngay tại thư mục giải nén.

### 2. Cài vào Git root

Xem trước thao tác cài đặt, rồi chạy installer:

```powershell
pwsh -NoProfile -File (Join-Path $jevPackage 'scripts/Install-CodexJev.ps1') -ProjectPath $jevProject -WhatIf
pwsh -NoProfile -File (Join-Path $jevPackage 'scripts/Install-CodexJev.ps1') -ProjectPath $jevProject
if ($LASTEXITCODE -ne 0) { throw 'Installation failed; resolve the reported error before continuing.' }

Set-Location -LiteralPath $jevProject
```

Installer kiểm tra package hashes, Git root, linked paths và collisions trước khi ghi. Nếu đã có `.jev-review` hoặc một skill cùng tên, hãy xử lý theo [hướng dẫn cập nhật](#maintenance). Installer giữ nguyên `AGENTS.md`, `.gitignore` và config Codex của team.

Files chính sau khi cài:

```text
MyApp/
├── .agents/skills/
│   ├── ultrathink/
│   ├── orchestrate/
│   ├── workflowz/
│   └── jevify/
└── .jev-review/
    ├── runtime/                  # CLI, Core DLL và runtime metadata
    ├── rubric.json               # Tiêu chí phân loại
    ├── policy.json               # Quy tắc chọn item cần review
    ├── fake-responses.json       # Responses mẫu cho demo offline
    ├── smoke/items.json          # Bốn item demo
    ├── codex-mcp.toml            # Snippet MCP theo đường dẫn máy này
    ├── release.json
    ├── installation.json         # Danh sách files đã cài
    └── docs/
```

Giữ toàn bộ files trong `runtime/`; CLI cần cả Core DLL, `.deps.json` và `.runtimeconfig.json`.

### 3. Kết nối MCP với Codex

Từ Git root của project, xem snippet đã tạo:

```powershell
Get-Content -LiteralPath .jev-review/codex-mcp.toml
```

Tạo `.codex/config.toml` nếu chưa có, rồi thêm table từ snippet vào file này. Nếu config đã có nội dung, merge table `[mcp_servers.jev_review]` và giữ các settings khác; tránh khai báo cùng table hai lần. Snippet chứa absolute paths của máy bạn, chẳng hạn:

```toml
[mcp_servers.jev_review]
command = "dotnet"
args = ["D:/Projects/MyApp/.jev-review/runtime/jev-review.dll", "mcp", "--workspace", "D:/Projects/MyApp"]
cwd = "D:/Projects/MyApp"
```

Dùng snippet do installer tạo để có paths chính xác. Codex chỉ đọc project config trong repo được tin cậy; áp dụng trust theo chính sách công ty rồi mở lại phiên Codex tại project. Xem [config Codex](https://learn.chatgpt.com/docs/config-file/config-basic) và [MCP chính thức](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

Trong Codex CLI, kiểm tra `/skills` có bốn skill và `/mcp` có `jev_review`. Nếu đã cài Codex CLI, từ Git root có thể dùng:

```powershell
codex mcp list
```

MCP cung cấp `start_review`, `get_review_status`, `get_review_result`. Khi gọi `start_review` trực tiếp, truyền `rubric`, `policy`, `fakeResponses` trỏ vào `.jev-review/`; defaults của tool vẫn là paths của lab. Skill `$jevify` hướng dẫn dùng các assets đã cài. CLI vẫn chạy được độc lập nếu bạn chưa kết nối MCP.

### 4. Chạy demo offline

Vẫn chạy từ Git root của **project đích**:

```powershell
dotnet .jev-review/runtime/jev-review.dll doctor --rubric .jev-review/rubric.json
dotnet .jev-review/runtime/jev-review.dll classify --dataset .jev-review/smoke --rubric .jev-review/rubric.json --policy .jev-review/policy.json --fake-responses .jev-review/fake-responses.json --mode fake
```

Lệnh cuối in JSON chứa `runId`, `output`, `counts`, `models`. Output được tạo trong `runs/<timestamp>-<id>`, nên có thể chạy lại mà không cần đổi tên thư mục thủ công.

Kết quả demo dự kiến:

```json
{
  "total": 4,
  "classified": 4,
  "failedOrUnjudged": 0,
  "excludedWithReason": 0,
  "flagged": 3
}
```

Mở `report.md` trong thư mục `output` để xem báo cáo. **Fake dùng responses định trước, chỉ kiểm tra pipeline; không suy luận code hoặc chứng minh chất lượng Jev.** Demo không gọi TypeSafe API; việc dùng Codex vẫn theo tài khoản/client hiện có của bạn.

<a id="usage"></a>

## Dùng trong công việc hằng ngày

### Chọn skill theo mục tiêu

Gọi tường minh bằng `$tên-skill` trong prompt Codex. Bốn skill đặt `allow_implicit_invocation: false`. Cơ chế discovery/invocation theo [tài liệu skills Codex](https://learn.chatgpt.com/docs/build-skills).

| Skill | Khi nên dùng | Ví dụ prompt |
| --- | --- | --- |
| `$ultrathink` | Bug khó, quyết định kỹ thuật chưa rõ failure modes | `$ultrathink điều tra vì sao API từ chối đơn hàng rỗng; đề xuất, sửa và kiểm chứng phương án nhỏ nhất.` |
| `$orchestrate` | Các phần việc độc lập đủ lớn để chia ownership | `$orchestrate review riêng module API và persistence, tổng hợp findings có evidence và kiểm tra tích hợp.` |
| `$workflowz` | Migration/refactor có plan và nhiều bước phụ thuộc | `$workflowz thực hiện migration theo docs/plan.md, ghi tiến độ từng phase và chỉ hoàn tất sau checks.` |
| `$jevify` | Triage committed diff rồi xác minh item flagged | `$jevify review main...feature/refactor với config trong .jev-review; mục tiêu giữ business behavior. Dùng fake để kiểm tra setup, rồi xác minh flagged items và báo coverage.` |

Thay tên branch, module và file plan bằng nội dung tồn tại trong project. `ultrathink` dùng model hiện tại; `orchestrate`/`workflowz` chỉ chạy subagents khi host có tools và cho phép, nếu không thì thực hiện tuần tự.

### Review một Git range bằng CLI

Ví dụ dưới dùng cùng assets đã cài. Thay `main` và `feature/refactor` bằng refs hoặc commit SHAs thực tế:

```powershell
$jevBase = 'main'
$jevHead = 'feature/refactor'
$jevReviewArgs = @(
    '--git', '.', '--base', $jevBase, '--head', $jevHead, '--merge-base',
    '--task', 'Refactor without changing business behavior',
    '--rubric', '.jev-review/rubric.json',
    '--policy', '.jev-review/policy.json'
)
dotnet .jev-review/runtime/jev-review.dll classify @jevReviewArgs --fake-responses .jev-review/fake-responses.json --mode fake
```

`--merge-base` lấy diff từ điểm phân nhánh chung đến head; bỏ flag nếu cần diff trực tiếp hai commits. Manifest ghi resolved SHAs và semantics. **Chỉ committed changes nằm trong phạm vi**; staged, unstaged và untracked changes không được thu thập.

Fake trên Git ID mới trả `insufficient_context` và yêu cầu reviewer đọc tiếp. Muốn phân loại bằng Jev thật, dùng [live mode](#live). Team chốt rubric/policy trước khi classify; đổi tiêu chí hoặc input thì tạo run mới.

### Đọc kết quả và ghi bằng chứng

Đọc summary, `flagged.json` và `errors.jsonl` trước, rồi lấy source/caller/tests của item cần xác minh.

| Artifact | Dùng để làm gì |
| --- | --- |
| `manifest.json` | Đối chiếu counts, SHAs, hashes, config và actual models |
| `flagged.json` / `errors.jsonl` | Chọn item cần review và nhận biết item lỗi |
| `verdicts.jsonl` | Xem prediction và terminal status của từng item |
| `report.md` | Chia sẻ kết quả, verification và phần chưa review |
| `verification.json` | Lưu evidence của reviewer sau lệnh `verify` |

Luôn giữ `total = classified + failedOrUnjudged + excludedWithReason`. `flagged` chồng lấp các trạng thái, không cộng thêm vào total. Prediction `refactor_only` và exit code 0 không chứng minh PR an toàn.

Lấy output path từ summary và một ID từ `flagged.json`, rồi inspect:

```powershell
$jevRun = 'runs/THAY-BANG-RUN-THUC-TE'
$jevFlagged = Get-Content -LiteralPath (Join-Path $jevRun 'flagged.json') -Raw | ConvertFrom-Json
$jevItemId = $jevFlagged[0].id
dotnet .jev-review/runtime/jev-review.dll inspect --run $jevRun --id $jevItemId
```

Chỉ dùng ví dụ inspect khi danh sách flagged có item. Sau khi xác minh, cung cấp JSON array records qua `verify --run <run> --file <records.json>`. Confirmed/rejected cần evidence gồm path, line và description; confirmed còn cần `findingLabel`. `verify` thay toàn bộ records của run, nên hãy gộp records cũ trước khi cập nhật. Có thể nhờ `$jevify` thực hiện bước này; xem [format và ví dụ verification](docs/codex-v1-quickstart.md#6-review-git-và-ghi-evidence).

Exit codes: **0** = classification hoàn tất; **1** = còn unjudged/excluded; **2** = input/config lỗi; **130** = hủy. Kiểm tra coverage và evidence trước khi quyết định với PR.

<a id="live"></a>

## Bật Jev thật khi team sẵn sàng

Live classify gửi diff/context tới TypeSafe và có thể phát sinh chi phí. Chỉ dùng dữ liệu công ty cho phép gửi. Chuẩn bị Git scope/`$jevReviewArgs` như ví dụ ở trên **trước khi nhập key**. Nhập key kín trong host được phép:

```powershell
$jevSecureKey = Read-Host 'TypeSafe API key' -AsSecureString
try {
    $env:TYPESAFE_API_KEY = [System.Net.NetworkCredential]::new('', $jevSecureKey).Password
    dotnet .jev-review/runtime/jev-review.dll doctor --mode live --rubric .jev-review/rubric.json
    if ($LASTEXITCODE -ne 0) { throw 'Live prerequisites are missing.' }
    dotnet .jev-review/runtime/jev-review.dll classify @jevReviewArgs --mode live --model jev-latest
}
finally {
    Remove-Item Env:TYPESAFE_API_KEY -ErrorAction SilentlyContinue
}
```

`doctor` ở ví dụ chỉ kiểm tra local. `doctor --check-live` gửi một request tổng hợp có thể tính phí; đây là bước riêng khi bạn muốn kiểm tra API. Không đặt key trong arguments, Git, config hoặc chat; không chạy build/tests/mã PR chưa tin cậy trong host có key.

Live MCP cần thêm `--allow-live` vào `args` và `env_vars = ["TYPESAFE_API_KEY"]` vào table `mcp_servers.jev_review`; environment phải có key lúc khởi động Codex. Giữ giá trị key ngoài config. Xem [live setup](docs/codex-v1-quickstart.md#7-live-mode-do-team-chủ-động-bật) và [TypeSafe integration](docs/TypeSafe-SystemOne.md).

<a id="troubleshooting"></a>

## Khắc phục lỗi thường gặp

| Hiện tượng | Cách xử lý |
| --- | --- |
| `pwsh`, `git` hoặc `dotnet` không tìm thấy | Cài công cụ ở phần Chuẩn bị; mở terminal mới và kiểm tra PATH |
| CLI yêu cầu runtime .NET 10 | Kiểm tra `dotnet --list-runtimes`; cài `Microsoft.NETCore.App 10.0.x` đúng kiến trúc máy |
| `git_repository_root_required` | Đặt `-ProjectPath` bằng Git root do `git rev-parse --show-toplevel` trả về |
| `project_setup_already_exists` / `project_skill_already_exists` | Installer không overwrite; review bộ cài hiện tại theo installation manifest rồi cập nhật thủ công |
| `package_hash_mismatch` / checksum sai | Lấy lại ZIP và checksum từ nguồn phân phối tin cậy; không sửa manifest để bỏ qua lỗi |
| Path là symlink/junction hoặc tổ tiên có link | Dùng project/package ở đường dẫn vật lý; installer/MCP từ chối linked paths |
| `rubric`/fixture không tìm thấy | Chạy từ Git root; truyền paths `.jev-review/` như ví dụ, kể cả khi gọi MCP trực tiếp |
| Codex chưa thấy skill hoặc MCP | Kiểm tra đúng project, trust/config và tool PATH; mở lại phiên, xem `/skills`, `/mcp` hoặc `codex mcp list` |
| Git fake trả nhiều `insufficient_context` | Đây là response cho ID không có fixture; tiếp tục xác minh source hoặc chủ động dùng live |
| Run output đã có nội dung | Bỏ `--output` để dùng tên tự sinh, hoặc chọn thư mục mới/rỗng |
| `release_directory_not_empty` khi build gói | Chọn `-OutputDirectory` mới; giữ gói cũ để truy lại version/checksum |

Nếu cần báo lỗi, gửi version/commit, OS, command đã loại secrets, counts/error codes và reproduction tối thiểu bằng dữ liệu tổng hợp.

<a id="maintenance"></a>

## Dùng chung trong team, cập nhật và giới hạn

Giữ cùng ZIP/checksum, version rubric và policy để team dùng cùng cấu hình. Cách đơn giản cho pilot v1: mỗi developer cài đầy đủ cùng gói vào checkout chưa có bốn skills hoặc `.jev-review`; team chọn ignore các files cài cục bộ theo chính sách repo. Nếu muốn commit các skills và bộ runtime/assets vào repo, người clone dùng bộ đã có và cấu hình MCP theo bước 3, không chạy lại installer. Nếu chỉ commit skills, cài runtime/assets thủ công theo [quickstart](docs/codex-v1-quickstart.md#2-cài-vào-git-root-của-project); installer v1 không có chế độ cài riêng runtime.

Snippet MCP chứa absolute paths nên mỗi máy cần tạo lại; đặt file này và config có paths cục bộ vào ignore theo chính sách repo. CLI tạo review output dưới `runs/` của project đích; installer không tự thêm ignore rules.

Installer v1 không overwrite. Update bằng cách review/merge files của gói mới dựa trên `.jev-review/installation.json`. Rollback bằng cách gỡ table MCP đã thêm và đúng files thuộc manifest; giữ review runs cần cho lịch sử. Hướng dẫn chi tiết: [update/rollback](docs/codex-v1-quickstart.md#8-đọc-kết-quả-cập-nhật-và-rollback).

V1 đã kiểm tra offline pipeline và MCP stdio; kết nối UI Codex và chất lượng Jev live chưa được xác nhận trong lần bàn giao. MCP hiện phù hợp pilot theo phiên: giới hạn 4 jobs đang chạy nhưng terminal history chưa có giới hạn; hard kill chưa có checkpoint hoàn chỉnh. CLI process riêng phù hợp chạy nhiều lượt. Xem [release notes](docs/codex-v1-release-notes.md) và [bằng chứng kiểm tra](docs/v1-validation-2026-10-14.md).

<a id="development"></a>

## Build từ source và đóng góp

Phần này dành cho maintainer/developer có checkout **source v1 chứa `scripts/New-CodexRelease.ps1`**. Cần .NET SDK **10.0.303 hoặc patch mới hơn trong feature band 10.0.3xx**, theo `global.json`; runtime là đủ cho người nhận ZIP.

Từ Git root của source, tạo gói với output mới để chạy lại được:

```powershell
$jevReleaseOutput = Join-Path (Get-Location).Path ('runs/releases/local-' + [Guid]::NewGuid().ToString('N'))
pwsh -NoProfile -File scripts/New-CodexRelease.ps1 -Version 1.0.0 -OutputDirectory $jevReleaseOutput
```

Script mặc định restore → build/test Release → smoke fake → publish runtime → tạo ZIP và checksum. Không cần TypeSafe key. Lấy hai files trong `$jevReleaseOutput`, rồi áp dụng quy trình cài ở trên vào project đích.

Để phát triển từ source:

```powershell
dotnet restore JevReview.slnx
dotnet build JevReview.slnx --no-restore
dotnet test JevReview.slnx --no-restore
dotnet run --project src/JevReview.Cli --no-build -- classify --dataset datasets/smoke --mode fake
```

Default CI chạy offline. Live smoke/evaluation thực hiện riêng khi được phép. Đọc [CONTRIBUTING.md](CONTRIBUTING.md) trước khi sửa contracts hoặc phát hành.

## Tài liệu và nguồn tham khảo

- [Quickstart chi tiết](docs/codex-v1-quickstart.md) — cũng được cài tại `.jev-review/docs/` trong project đích.
- [CLI/lab reference](docs/cli-reference.md) — options, retry, resume, artifacts, Git/MCP và evaluator.
- [Kiến trúc](docs/architecture.md) · [release notes](docs/codex-v1-release-notes.md) · [validation](docs/v1-validation-2026-10-14.md).
- [Kế hoạch đánh giá A/B](docs/experiment-plan.md) — gold và baseline độc lập; fake fixtures không phải gold.
- [README oh-my-pi](https://github.com/can1357/oh-my-pi/blob/main/README.md) và [magic keywords](https://github.com/can1357/oh-my-pi/blob/main/docs/magic-keywords.md) là nguồn tham khảo cách trình bày và workflow.

Bản này dùng cơ chế skills/MCP của Codex. Các skill không tự đổi reasoning effort, không cung cấp keyword interception, highlighting hoặc eval kernel của oh-my-pi. Gói v1 hiện phân phối nội bộ; public package/registry và quyết định license xem [CONTRIBUTING.md](CONTRIBUTING.md).
