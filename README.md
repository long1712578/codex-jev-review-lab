# Codex + Jev Review Lab

Lab .NET để review PR được mô tả là refactor: CLI thu thập diff, Jev phân loại, policy chọn item cần đọc sâu, Codex xác minh và lưu báo cáo. Triển khai theo [kế hoạch gốc](codex-jev-review-lab-plan.md).

Đã có review core dùng chung, CLI fake/live, policy, retry/timeout/cancellation, resume, evaluator, Git collector, MCP stdio và các skill Codex. **Fake chỉ kiểm tra pipeline.** Smoke Jev thật và benchmark A/B thật còn cần API key, baseline và gold độc lập.

Review ban đầu (14/10/2026): [findings và kiểm chứng](docs/community-review-2026-10-14.md), [roadmap cộng đồng](docs/superpowers/specs/2026-10-14-community-distribution-design.md), [setup bằng DLL](docs/installation.md), [lợi ích và so sánh](docs/comparison.md), [TypeSafe System One cho .NET](docs/TypeSafe-SystemOne.md), [đóng góp](CONTRIBUTING.md). R1–R4 đã được xử lý trong v1; terminal cache/persistence và .NET tool package còn trong backlog.

## V1 cho project công ty

[Quickstart cài đặt và sử dụng](docs/codex-v1-quickstart.md), [release notes](docs/codex-v1-release-notes.md), [kiến trúc và nguồn tham khảo](docs/architecture.md), [plan triển khai](docs/superpowers/plans/2026-10-14-codex-jev-v1.md).

```powershell
pwsh -NoProfile -File scripts/New-CodexRelease.ps1
```

Gói ZIP dưới `runs/releases/1.0.0` có runtime, bốn skill `$ultrathink`, `$orchestrate`, `$workflowz`, `$jevify`, config và installer theo project. Installer hỗ trợ `-WhatIf` và từ chối overwrite. Gọi skill tường minh; bản này không chèn chỉ dẫn ẩn hoặc tự đổi reasoning effort. Core .NET 10 đã tách khỏi CLI/MCP; artifact JSON và commands hiện có được giữ tương thích. Chưa publish NuGet hoặc chạy live benchmark.

## Cài đặt và demo

Yêu cầu .NET SDK **10.0.303** hoặc patch mới hơn cùng feature band `10.0.3xx`, Git; PowerShell nếu dùng `.ps1`. `global.json` pin SDK, ứng dụng dùng .NET 10. Chạy từ gốc repo:

```powershell
dotnet restore JevReview.slnx
dotnet build JevReview.slnx --no-restore
dotnet test JevReview.slnx --no-restore
dotnet run --project src/JevReview.Cli --no-build -- doctor
dotnet run --project src/JevReview.Cli --no-build -- classify --dataset datasets/smoke --mode fake
```

Lệnh cuối in JSON summary nhỏ gồm output path/counts/models. Mở `report.md` tại đường dẫn đó. Smoke có **4 item, mỗi nhãn một case**; kết quả fake dự kiến: classified 4, failed 0, excluded 0, flagged 3. Không cần key.

Wrapper PowerShell sau bước restore:

```powershell
.\scripts\jev-review.ps1 doctor
.\scripts\demo.ps1
.\scripts\demo.ps1 -Dataset datasets/dev
.\scripts\jev-review.ps1 classify --dataset datasets/dev --mode fake --policy configs/policy.json --output runs/dev-01
.\scripts\jev-review.ps1 inspect --run runs/dev-01 --id dev-006
```

`jev-review` là tên app; không cần cài global tool. Dùng wrapper hoặc `dotnet run --project src/JevReview.Cli --no-build -- <command>`. Lệnh dotnet trực tiếp dùng được trên Windows/Linux. Mỗi output directory phải mới hoặc rỗng.

## Dữ liệu và fake

`datasets/dev/items.json`: 20 case. `datasets/holdout/items.json`: 20 case khác nhóm thay đổi. Input chỉ có `id`, `task`, `path`, `diff`, `context`. Parser từ chối ID trùng và field gold như `expectedLabel`; thiếu task/path/diff được giữ dưới `excluded`.

Fake đọc response định trước theo ID từ `fixtures/fake/dev-responses.json`, **không suy luận từ code**. Item lạ nhận `insufficient_context`. Vì vậy fake trên holdout/Git mới chuyển review toàn bộ; sửa nội dung case dev vẫn dùng response mock cùng ID. Không dùng mock làm gold hoặc bằng chứng chất lượng Jev.

```powershell
.\scripts\jev-review.ps1 classify --dataset datasets/smoke --mode fake --fake-responses fixtures/fake/failure-responses.json --output runs/failure-01
```

Demo lỗi: case đầu retry 429 hai lần rồi thành công, hai case khác thành `unjudged`, vẫn có trong flagged/errors/counts. Exit code 1 phản ánh các item chưa được phân loại.

## Chạy Jev thật

Đặt `TYPESAFE_API_KEY` trong môi trường local; không đưa key vào argument, Git, fixture hoặc chat. PowerShell nhập kín:

```powershell
$secureKey = Read-Host 'TypeSafe API key' -AsSecureString
$env:TYPESAFE_API_KEY = [System.Net.NetworkCredential]::new('', $secureKey).Password
.\scripts\jev-review.ps1 doctor
.\scripts\jev-review.ps1 doctor --check-live
.\scripts\jev-review.ps1 classify --dataset datasets/smoke --mode live --model jev-latest --output runs/live-smoke-01
Remove-Item Env:TYPESAFE_API_KEY
```

`doctor` mặc định không gọi mạng, không in key. **`--check-live` gửi một request tổng hợp có thể tính phí**, không tải dataset. Live classify chỉ dùng dữ liệu được phép gửi tới dịch vụ.

Client dùng `POST https://api.typesafe.ai/v1/systemone`, Bearer auth, `state` và một question `choice` mỗi item, với criteria từ rubric, theo [TypeSafe API reference](https://docs.typesafe.ai/api). Actual model từ response lưu riêng với requested model. HTTP 401/422 và response sai schema không retry; timeout/lỗi kết nối/408/429/5xx retry có giới hạn, exponential backoff và `Retry-After` (chờ tối đa 30 giây mỗi lần). Không echo payload/key/HTTP error body.

## Rubric, policy và options

Rubric: `rubrics/refactor-review.v1.json`. Đổi tiêu chí cần đổi phiên bản và tạo run mới.

| Nhãn | Ý nghĩa |
| --- | --- |
| `refactor_only` | Giữ hành vi quan sát được |
| `behavior_change` | Thay kết quả, điều kiện hoặc side effect |
| `mixed` | Có refactor riêng và behavior change riêng |
| `insufficient_context` | Thiếu bằng chứng cần thiết |

Policy mặc định tương đương `configs/policy.json`: review nhãn rủi ro/thiếu context, `refactor_only` có xác suất **của chính nhãn đó** dưới 0.8, paths chứa authorization/authentication/transaction, và mẫu ổn định 10% từ nhóm còn lại bằng hash ID. `confidence` không thay thế probability.

| Option classify | Mặc định / công dụng |
| --- | --- |
| `--dataset <path>` | Directory chứa `items.json`, hoặc file `.json`/`.jsonl` |
| `--mode fake\|live` | fake |
| `--rubric <file>` | rubrics/refactor-review.v1.json |
| `--policy <file>` | Nếu bỏ qua dùng defaults |
| `--model <name>` | fake-v1 hoặc jev-latest theo mode |
| `--output <directory>` | Tự tạo dưới runs/ |
| `--concurrency <1..32>` | 1; mỗi item một HTTP request |
| `--timeout-seconds <1..600>` | 30 mỗi attempt |
| `--max-retries <0..5>` | 2, tối đa 3 attempts |
| `--max-input-bytes <number>` | 24000 byte UTF-8 của request JSON đầy đủ, gồm rubric |
| `--fake-responses <file>` | fixtures/fake/dev-responses.json; chỉ đọc khi fake |
| `--resume <run-directory>` | Dùng lại prediction thành công khi cache key khớp |

Byte budget là giới hạn bảo thủ cục bộ, **không phải token counter** của Jev. Vượt giới hạn → `excluded/input_too_large` và flagged, không truncate hoặc chia mất context. Nếu tăng budget, tự đối chiếu giới hạn token dịch vụ. Ctrl+C ghi item chưa chạy thành `unjudged/cancelled` và xuất artifacts trước khi thoát.

Exit codes: 0 thành công; 1 có unjudged/excluded; 2 cấu hình/input không hợp lệ; 130 cancellation. Exit 0 không chứng minh PR an toàn.

## Artifacts và xác minh

| File | Nội dung |
| --- | --- |
| manifest.json | ID, UTC timestamps, hashes, config không có key, actual models, nguồn/counts |
| verdicts.jsonl | Một terminal verdict mỗi item; prediction riêng |
| flagged.json | ID/path/status/reasons, gồm lỗi và excluded |
| errors.jsonl | Item unjudged và mã lỗi |
| report.md | Counts, predictions, verification và coverage |
| verification.json | Chỉ có sau verify, lưu records của reviewer |

Đối soát: `total = classified + failedOrUnjudged + excludedWithReason`. Flagged chồng lấp các trạng thái, không cộng vào total. Schema: `schemas/verdict.schema.json`. Raw input giữ ở dataset/repo gốc, không copy vào run.

Sau khi đọc source/caller/test, ghi JSON array theo `evaluation/verification.example.json`. Confirmed/rejected cần evidence có path, dòng dương và mô tả; confirmed cần findingLabel. Unresolved nêu context thiếu. CLI chỉ kiểm tra cấu trúc, không tự chứng minh evidence đúng:

```powershell
.\scripts\jev-review.ps1 verify --run runs/dev-01 --file evaluation/verification.example.json
```

File ví dụ dùng bằng chứng fixture; paths C# đó không tồn tại trong repo lab. Verify thay thế toàn bộ records của run, cập nhật report và thống kê coverage; muốn bổ sung hãy gộp records trước. Model predictions luôn tách khỏi verified findings.

## Resume

```powershell
.\scripts\jev-review.ps1 classify --dataset datasets/dev --mode fake --output runs/dev-02 --resume runs/dev-01
```

Chỉ reuse classified có input/rubric/config hash khớp. Config gồm model/mode, policy, timeout/retry/concurrency và hash fixture fake. Item lỗi chạy lại. Run mới ghi reused=true, attempts/latency bằng 0; không sao chép verification cũ.

Live resume cần **pin version model**, từ chối alias jev-latest/jev-preview và đòi actual model cũ khớp model pin. Resume đọc run đã ghi đầy đủ, kể cả run hủy; chưa checkpoint khi process bị kill cứng trước khi ghi report.

## Git range

```powershell
.\scripts\jev-review.ps1 classify --git 'D:\Projects\MyApp' --base main --head feature/refactor --merge-base --task 'Refactor validation without changing business behavior' --mode fake
```

Có `--merge-base`: diff merge-base(base, head) → head. Không có: diff trực tiếp hai commits. Manifest lưu baseSha/headSha/diffBaseSha, semantics và repo. Collector giữ filenames có khoảng trắng, lấy 3 dòng context, tắt external diff/textconv và rename detection; rename có thể thành delete/add. Binary/mode-only diff cần context và review thủ công.

Chỉ đọc commits: staged/unstaged/untracked ngoài phạm vi và được ghi rõ trong manifest. Codex lấy implementation/caller khi xác minh. Không build/test PR không tin cậy trong môi trường giữ key.

## Skill Codex

Skill tại `.agents/skills/jev-triage/SKILL.md`. Mở Codex ở repo; nếu skill chưa xuất hiện hãy khởi động lại Codex. Sau build:

```text
$jev-triage Review datasets/dev in fake mode. Verify flagged changes and report evidence, rejected predictions and unresolved items.
```

Skill chốt rubric trước bulk input, đầu tiên chỉ đọc summary/flagged/errors, rồi lấy source các item được chọn và nhập verification. Không đọc gold khi inference; fake không chứng minh Jev đoán đúng. Discovery theo [tài liệu skills Codex](https://learn.chatgpt.com/docs/build-skills).

## MCP stdio

```powershell
dotnet publish src/JevReview.Cli -c Release --no-restore -p:UseAppHost=false -o runs/publish
dotnet runs/publish/jev-review.dll mcp --workspace 'D:\Projects\AGENT-1\codex-jev-review-lab'
```

MCP đọc/ghi JSON-RPC từng dòng qua stdio, protocol 2025-06-18, initialize/ping/tools/list/tools/call. Stdout chỉ có protocol messages. Ví dụ cấu hình client, sửa absolute paths cho máy bạn:

```toml
[mcp_servers.jev_review]
command = "dotnet"
args = ["D:/Projects/AGENT-1/codex-jev-review-lab/runs/publish/jev-review.dll", "mcp", "--workspace", "D:/Projects/AGENT-1/codex-jev-review-lab"]
```

Fake không cần key. Live MCP cần thêm `--allow-live` vào args và `env_vars = ["TYPESAFE_API_KEY"]` vào table cấu hình để forward key từ môi trường Codex, không đặt giá trị key trong args. Xem [cấu hình MCP chính thức](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

| Tool | Input / output |
| --- | --- |
| start_review | dataset hoặc git+base/head/task, rubric/mode/policy/options → runId ngay |
| get_review_status | runId → running/completed/failed/cancelled, counts khi đã có |
| get_review_result | runId → manifest, flagged, errors, reportPath; không có raw diff |

Tối đa 4 jobs đang chạy; concurrency mỗi job mặc định 1. Restart mất jobs đang chạy; run có manifest có thể đọc lại. Completed nghĩa đã xuất artifacts, vẫn cần kiểm tra counts. Paths giới hạn trong workspace, từ chối symlink/junction; không nhận shell tự do. Adapter dùng cùng service với CLI; test giao thức dựa trên [MCP specification](https://modelcontextprotocol.io/specification/2025-06-18/server/tools). Chưa kiểm thử kết nối UI Codex thực tế.

## Evaluation A/B

Đọc [experiment-plan.md](docs/experiment-plan.md) trước. Người đánh giá tự kiểm tra gold **ngoài workspace inference**. `evaluation/gold.example.json` chỉ mô tả format, không chứa đáp án dev/holdout. Không lấy fake fixture làm gold.

Sau khi đóng băng output inference, chạy evaluator riêng:

```powershell
.\scripts\evaluate.ps1 -Run runs/live-holdout-01 -Gold 'D:\PrivateEvaluation\holdout-gold.json' -Output runs/eval-01
.\scripts\evaluate.ps1 -Run runs/live-holdout-01 -Gold 'D:\PrivateEvaluation\holdout-gold.json' -Baseline 'D:\PrivateEvaluation\baseline-verdicts.jsonl' -Output runs/eval-ab-01
```

Gold record: id, expectedLabel, requiresReview, reason. RequiresReview xác định positive trước experiment. Baseline A dùng cùng verdict contract/input hash; không lấy B rồi đổi tên thành A. Thiếu prediction vẫn tính missing trong denominator. Thiếu baseline thì A là N/A.

Output metrics.json/evaluation.md: recall/precision, escalation, coverage/error rate, confusion matrix gồm unjudged/excluded/missing, false negative IDs, p50/p95 latency item gồm retry/backoff. Mẫu số 0 là null/N/A. Token là N/A nếu đo không đầy đủ; chi phí, thời gian/token Codex chưa instrument cũng là N/A. Correctness của finding cuối cần đối chiếu verification riêng.

## Trạng thái và giới hạn

M1/M3 có offline demo/dev/policy; M2 có live adapter nhưng smoke thật chờ key; M4 có skill/verification contract, cần phiên Codex mới; M5 có 20 holdout/evaluator, chưa benchmark thật; M6 có Git/MCP, chưa test client UI thật. CI chạy offline trên Windows/Linux, không dùng secrets.

Việc tiếp theo: durable checkpoint; metrics từng HTTP attempt/toàn pipeline; rate-limit tuning; môi trường agent review cô lập; working-tree collector nếu cần. Pilot 40 case không chứng minh chất lượng production. Lab không tự sửa/merge code.
