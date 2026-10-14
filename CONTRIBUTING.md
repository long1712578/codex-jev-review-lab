# Đóng góp cho Jev Review

Repo hiện là lab có CLI/MCP; [review cộng đồng](docs/community-review-2026-10-14.md) và [thiết kế đề xuất](docs/superpowers/specs/2026-10-14-community-distribution-design.md) xác định các mốc tiếp theo. Chủ repo cần chọn LICENSE trước khi phát hành gói cộng đồng; tài liệu này chưa cấp quyền sử dụng theo một license cụ thể.

## Bắt đầu

Cần Git và .NET SDK theo `global.json` (`10.0.303`, latest patch). Từ root repo:

```powershell
dotnet restore JevReview.slnx
dotnet build JevReview.slnx --no-restore
dotnet test JevReview.slnx --no-restore
dotnet run --project src/JevReview.Cli --no-build -- classify --dataset datasets/smoke --mode fake
```

Default checks offline, không cần API key. Hai tests Unix-only được skip trên Windows và chạy trong Linux CI; báo rõ nếu chỉ kiểm tra một OS. Build mới phải không warnings vì repo bật `TreatWarningsAsErrors`.

## Gửi thay đổi có thể review

Mỗi PR tập trung một lỗi hoặc một deliverable. Mô tả input gây lỗi, hành vi trước/sau, file/contract bị ảnh hưởng, bằng chứng kiểm chứng và giới hạn còn lại. Với bugfix, thêm regression tái hiện lỗi thật trước fix; tránh tests chỉ lặp lại implementation. Với thay đổi API/artifact format, thảo luận design và compatibility trước.

Ưu tiên hiện tại: Git literal pathspec, MCP linked-root guard và handshake, typed core request, rồi local tool packaging. Đừng thêm nhiều provider, database hoặc dashboard khi không có consumer cụ thể.

## Các invariant bắt buộc

- Freeze rubric trước bulk input; agent đọc summary/flagged/errors trước context chọn lọc.
- `total = classified + failedOrUnjudged + excludedWithReason`; flagged chồng lấp states.
- Không truncate âm thầm hoặc đổi API/schema error thành pass; item lỗi/hủy còn nhìn thấy được.
- Predictions, confidence/probabilities và fake responses không phải evidence hoặc gold.
- Không in credentials, request bodies, full datasets hoặc remote error bodies ra command output.
- CLI/MCP dùng chung core; MCP nhận input có cấu trúc, guard workspace, không nhận arbitrary shell.
- Git review dùng resolved commits và diff semantics đã ghi; working tree ngoài phạm vi.
- Live smoke/evaluation là thao tác riêng được phép; không đưa key vào default CI hoặc build/test PR không tin cậy.
- Gold ở ngoài inference workspace bằng ranh giới truy cập thật; giữ N/A cho metrics chưa đo được.

## Báo lỗi

Trong issue, ghi version/commit, OS, SDK, command/options đã loại secrets, counts/error codes và reproduction tối thiểu bằng dữ liệu tổng hợp. Không paste API keys hoặc diff riêng tư. Những phép đo chất lượng cần mô tả gold, baseline và phương pháp; fake fixtures không dùng làm bằng chứng chất lượng.

## Trước khi phát hành

Package installation cần smoke ở project trống trên Windows và Linux. README phải phân biệt tính năng hiện có, planned và measurements chưa chạy. Maintainer duyệt license, package identity/version, changelog và artifacts trước khi publish; contributor không cần credentials để chạy offline suite.
