# Codex + Jev Skills 1.0.0

Release nội bộ từ source và hashes trong `release.json`. Gói gồm .NET 10 runtime files, bốn skill gọi tường minh, rubric/policy/fake fixture, smoke và installer theo project. Đây là artifact local; không có NuGet/global tool/public registry publication.

## Thay đổi

- Tách `JevReview.Core` khỏi CLI/MCP host. Typed ReviewRequest thay parser coupling; Core không tự đọc environment credential. DTO theo trách nhiệm, labels dùng chung với evaluator/verification.
- Git dùng literal pathspec và UTF-8 stdout/stderr: bracket paths không trộn diff, tên Unicode giữ đúng trên Windows.
- MCP guard kiểm tra workspace root/ancestors/segments và kiểm tra lại paths. Handshake validate initialize params, chờ initialized notification trước review tools.
- Xóa unused Job.Task và imports theo analyzer; terminal jobs chỉ giữ manifest, không giữ full verdict arrays.
- Thêm editorconfig và build-enforced unused code checks. Public DTO members phục vụ serialization được giữ nguyên.
- ZIP có allowlist, file hashes và ZIP checksum. Installer có preflight, integrity checks và WhatIf; giữ config/AGENTS/skill của team khi có collision.
- Các skill dùng công cụ Codex thực tế; jevify dùng MCP start/status/result cùng CLI inspect/verify và tách prediction khỏi evidence.

## Kiểm chứng và giới hạn

Correctness regressions tái hiện lỗi trước fix. Package integration kiểm tra ZIP extraction, Unicode/spaces, fresh Git repo, doctor/classify/inspect/verify, MCP subprocess và collisions/WhatIf/tamper rejection. Báo kết quả và OS thực tế của lần release trong tài liệu handoff; workflow CI khai báo Windows/Linux không tự chứng minh cả hai OS đã pass.

Không chạy live smoke hoặc benchmark chất lượng trong release mặc định. Fake fixture kiểm tra wiring, không phải gold. Chưa có keyword interception/highlighting, automatic reasoning settings, eval kernel hoặc NuGet tool. Terminal history cache, checkpoint/atomic persistence, tổng collection budget và diagnostics chi tiết là backlog. Path checks không thay thế OS isolation và không loại hết race đổi link.

Public distribution cần chủ repo quyết định license/quyền phát hành. Không sao chép source hoặc notice của upstream; tham khảo được ghi trong tài liệu architecture.
