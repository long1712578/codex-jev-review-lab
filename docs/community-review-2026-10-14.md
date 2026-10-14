# Review kỹ thuật Jev Review — 14/10/2026

## Kết luận

Repo có nền tảng tốt cho một công cụ triage: đầu ra có kiểu, validate response, policy xác định bằng code, retry có giới hạn, đối soát item, và tách prediction khỏi verification. Tuy nhiên, chưa nên phát hành như một công cụ cài vào mọi project. Cần sửa Git pathspec và kiểm tra workspace MCP trước, sau đó đóng gói và ổn định hợp đồng mở rộng.

Review dựa trên checkout tại commit `b6d188b`, gồm thay đổi có sẵn trong `tests/JevReview.Tests/CommandTests.cs`. Thay đổi có sẵn đó được giữ nguyên. Không có `.codegraph/` tại repo root nên không gọi CodeGraph hoặc tự index. Không chạy live API hoặc benchmark chất lượng.

## Findings có bằng chứng

### R1 — P1: Git pathspec trộn diff của nhiều file vào một item

Nguồn: [GitCollector.cs](../src/JevReview.Cli/Collection/GitCollector.cs), dòng 25. Tên file thu từ `--name-only -z` được đưa trực tiếp sau `--`. `--` ngăn parsing option nhưng Git vẫn diễn giải pathspec.

Tái hiện trên Windows bằng repo tổng hợp có ba file `[ab].cs`, `a.cs`, `Đơn hàng.cs`, cùng thay đổi giữa hai commits. `GitCollector.CollectAsync` trả item `[ab].cs` có **2** header `diff --git`; hai item còn lại có 1. Vì vậy diff của `a.cs` xuất hiện trong hai item, và evidence/policy theo path có thể gắn sai file.

Sửa đề xuất: bật `--literal-pathspecs` khi gọi Git; giữ `ProcessStartInfo.ArgumentList`, `--no-ext-diff`, `--no-textconv`, SHA đã resolve và output NUL. Regression phải kiểm tra mỗi item chỉ chứa diff của đúng file, gồm bracket, khoảng trắng, Unicode và pathspec magic trên hệ điều hành cho phép tên đó. Tham khảo [Git global options](https://git-scm.com/docs/git).

### R2 — P1: Workspace root là junction/symlink không bị MCP chặn

Nguồn: [McpServer.cs](../src/JevReview.Cli/Mcp/McpServer.cs), dòng 8 và 164. `SafePath` kiểm tra các segment nằm dưới root, bỏ qua chính root và các ancestor của root.

Tái hiện: tạo hai thư mục tổng hợp `mcp-workspace`, `mcp-outside`, cùng một junction `mcp-root-junction` trỏ vào `mcp-outside`. Khởi tạo MCP với junction làm workspace; `start_review` đọc `items.json` thành công, job `completed`, và manifest được ghi qua link vào target. Đây là lỗ hổng so với hợp đồng từ chối linked path segments. Điều kiện là workspace cấu hình chứa link; chưa chứng minh một request từ xa tự thay đổi được workspace cấu hình.

Sửa đề xuất: kiểm tra root và ancestor hiện hữu lúc khởi tạo; dùng chung một workspace path guard cho tất cả đường đọc/ghi; kiểm tra lại sát thao tác I/O. Kiểm tra path bằng chuỗi/reparse attributes không tạo ra OS sandbox và không giải quyết tuyệt đối việc link bị thay giữa kiểm tra và mở file. Với PR không tin cậy vẫn cần môi trường cô lập như [experiment plan](experiment-plan.md).

### R3 — P2: MCP bỏ qua validation và trạng thái khởi tạo

Nguồn: [McpServer.cs](../src/JevReview.Cli/Mcp/McpServer.cs), dòng 34 và 41. Mọi notification thiếu ID bị bỏ qua, trong khi `initialize` đặt `initialized = true` mà không đọc `params`. Probe xác nhận server nhận `initialize` thiếu params và phục vụ `tools/list` trước `notifications/initialized`.

Sửa đề xuất: validate các trường bắt buộc, thương lượng protocol version, xử lý notification khởi tạo và shutdown; trả mã protocol đúng. Giữ protocol đã công bố `2025-06-18` trong lần sửa này, không tự đổi sang version khác. Regression chạy qua stdio subprocess, gồm request malformed và disconnect khi job đang chạy. Căn cứ: [MCP lifecycle](https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle).

Phân biệt: thiếu params là input contract không hợp lệ. Việc server trả lời một request đến trước notification là điểm guard cần thiết kế rõ; không coi riêng việc đó là vi phạm một MUST yêu cầu server từ chối request trong specification.

### R4 — P2: Bản publish chưa tự dùng được từ project mới

Nguồn: [ReviewService.cs](../src/JevReview.Cli/ReviewService.cs), dòng 21 và 23; [JevReview.Cli.csproj](../src/JevReview.Cli/JevReview.Cli.csproj). Rubric và fixture mặc định nằm tương đối với CWD; project chưa có `PackAsTool` hay gói tài nguyên đi kèm.

Tái hiện: `doctor` từ thư mục trống trả exit 2. DLL publish chạy được từ thư mục khác khi truyền absolute rubric/fixture paths; smoke vẫn đủ 4 item, flagged 3. Hướng dẫn dùng ngay ở [installation.md](installation.md). Đây là khoảng trống phân phối, chưa phải một `.NET tool` đã publish.

### R5 — P2: Bộ nhớ của MCP tăng theo số job đã hoàn tất

Nguồn: [McpServer.cs](../src/JevReview.Cli/Mcp/McpServer.cs), dòng 9, 65, 116 và 201. Giới hạn 4 jobs chỉ đếm jobs `running`; dictionary không xóa jobs terminal và mỗi job giữ `ReviewRun`/verdicts. Kết luận từ source: bộ nhớ giữ lại tăng theo lịch sử job. Chưa chạy stress test hoặc đo mức RAM.

Sửa đề xuất: giới hạn cache terminal, evict bản in-memory sau khi persistence thành công và đọc artifacts khi cần. Không xóa lịch sử trên đĩa ngầm. Test hơn giới hạn cache, đọc lại job đã evict, và bảo toàn trạng thái lỗi/hủy.

## Khoảng trống kiến trúc và phát hành

| Điểm | Hệ quả | Hướng xử lý |
| --- | --- | --- |
| `ReviewService.StartAsync(CommandOptions, ...)` | Core nhận kiểu parser CLI; MCP dựng lại chuỗi args | Request có kiểu và validation dùng chung |
| Runner gọi `JevJudgeClient.BuildRequest` để tính budget | Mở rộng provider phải phụ thuộc payload Jev | Request builder/budget adapter tại biên provider |
| `Models.cs` chứa review, fake, verification, evaluation | Mọi consumer phụ thuộc một tập DTO rộng | Tách theo trách nhiệm khi tách core; giữ wire contract |
| `ReviewService.LoadRubric` phụ thuộc `Evaluator.Labels` | Domain review phụ thuộc evaluator | Định nghĩa nhãn trong core |
| Thu toàn bộ items/diffs trước byte check | Budget mỗi item chưa giới hạn tổng RAM/collection | Tổng budget rõ ràng, đọc có giới hạn, oversized vẫn có record |
| Chỉ ghi artifacts cuối run; manifest ghi trực tiếp | Kill cứng có thể mất tiến độ hoặc để file dở | Checkpoint và atomic completion marker |
| Không có schema version của artifacts | DTO mới có thể phá resume/read của bản cũ | Version và migration/reject có error code |
| Chưa có LICENSE, CONTRIBUTING hay release workflow | Người khác thiếu hợp đồng sử dụng và đóng góp | CONTRIBUTING được bổ sung; chủ repo chọn license trước phát hành |

Không thêm plugin loader động, database, web dashboard hoặc nhiều provider chỉ để gọi là mở rộng. Đầu tiên cần một đường cài đặt và một provider đáng tin cậy.

## Kiểm chứng trong phiên này

| Kiểm tra | Kết quả |
| --- | --- |
| `dotnet build JevReview.slnx --no-restore` | Pass, 0 warnings, 0 errors |
| `dotnet test JevReview.slnx --no-restore` | 48 passed, 2 Unix-only skipped, 0 failed |
| Smoke fake | total 4, classified 4, failed 0, excluded 0, flagged 3 |
| Release publish + chạy DLL ở CWD khác với paths tường minh | Pass, cùng counts smoke |
| Compile nguyên ví dụ C# trong tài liệu TypeSafe | Pass, 0 warnings, 0 errors; không chạy live |
| Git bracket/Unicode probe | Bracket lỗi; Unicode và tên có khoảng trắng giữ đúng |
| MCP linked root qua protocol | Accepted; completed; ghi qua link |
| Cache chứa dòng `null` | Bị từ chối với exit 2, không escape exception |

Probe là chương trình chẩn đoán dùng dữ liệu tổng hợp trong `runs/community-audit-probe`; kết quả ở `runs/community-audit-evidence/evidence.json`. Hai thư mục nằm ngoài source phát hành và bị Git ignore. Probe không phải benchmark AI. Các findings R1–R5 chưa được sửa trong đợt review/thiết kế này.

## Quyết định đề xuất

Chọn hướng **core dùng chung + .NET tool theo project + MCP adapter + skill**. Xem [thiết kế và roadmap](superpowers/specs/2026-10-14-community-distribution-design.md). Đợt triển khai đầu sửa R1–R3 bằng regression, rồi tách biên core và làm local package installation. Live evaluation là một hành động riêng cần được cho phép và có gold độc lập.
