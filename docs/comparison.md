# Jev Review có ích ở đâu?

Đối chiếu ngày 14/10/2026. Đây là so sánh nhiệm vụ và thiết kế, chưa có benchmark chứng minh ưu thế về giá, tốc độ hoặc tỷ lệ lỗi.

| Công cụ | Nhiệm vụ chính | Cách kết hợp với Jev Review |
| --- | --- | --- |
| [oh-my-codex (OMX)](https://github.com/Yeachan-Heo/oh-my-codex) | Workflow và runtime quanh Codex: skills, hooks, agent teams, trạng thái phiên | Có thể điều phối bước review dùng CLI/MCP của Jev Review |
| [CodeGraph](https://github.com/colbymchenry/codegraph) | Index code, symbols, call paths và impact; chạy local | Dùng để lấy source/caller khi xác minh item flagged |
| Jev Review hiện tại | Phân loại diff refactor, policy escalation, báo cáo và evidence contract | Cung cấp bước triage có thể kiểm tra trong workflow lớn hơn |
| Reviewer/Codex đọc trực tiếp | Suy luận theo source, caller, tests và mục tiêu thay đổi | Baseline A/B và bước xác minh cuối |

Các chức năng OMX và CodeGraph trong bảng dựa trên README chính thức của hai project. CodeGraph công bố xử lý local không cần key; live Jev gửi state tới TypeSafe. Khi dùng CodeGraph làm context cho reviewer, việc classification bằng Jev vẫn có boundary dữ liệu riêng.

## Giá trị đã có trong repo

- **Đối soát đủ item:** lỗi API, schema lỗi, quá lớn và cancelled có terminal records hoặc được từ chối rõ; không coi là refactor an toàn.
- **Policy nhìn thấy được:** paths nhạy cảm, probability của chính nhãn và quality sampling do code/config quyết định.
- **Bằng chứng tách biệt:** prediction của model và findings reviewer xác minh nằm ở hai hợp đồng khác nhau; evidence có path/dòng/mô tả.
- **Khả năng truy lại:** manifest lưu input/rubric/config hashes, actual model và Git SHAs/semantics; không suy luận lại từ mutable branch khi verify.
- **Offline để đóng góp:** tests/fake fixtures kiểm tra pipeline không cần credentials; production API lỗi không biến thành pass.

Đây là lợi ích thiết kế, không phải tuyên bố các công cụ khác không có khả năng tương tự. V1 đã xử lý R1–R4 của [review ban đầu](community-review-2026-10-14.md) và có ZIP/installer cho pilot nội bộ; các giới hạn còn lại được ghi trong [release notes](codex-v1-release-notes.md).

## Giá trị cần đo

Giả thuyết: với nhiều item rõ ràng và tỷ lệ escalation thấp, Jev có thể giảm lượng context reviewer phải đọc. Nếu phần lớn item thiếu caller/context hoặc tất cả bị flagged, thêm classification có thể tăng thời gian và chi phí.

Benchmark cần giữ cùng input và reviewer configuration giữa A (review trực tiếp) và B (triage + review). Báo recall/false negatives, precision, escalation, coverage, wall-clock toàn pipeline, retries, tokens và chi phí của cả Jev lẫn reviewer. Gold ở môi trường evaluator tách biệt; fake chỉ đo wiring. Thiếu dữ liệu đo thì ghi N/A. Quy trình ở [experiment-plan.md](experiment-plan.md).

## Khi nào chọn?

Chọn Jev Review khi bạn cần review hàng loạt thay đổi refactor theo rubric/policy cố định, cần audit artifacts và sẵn sàng xác minh phần flagged. Với vài file nhỏ, review trực tiếp có thể đơn giản hơn. Nếu bạn cần điều phối agent/workflow, dùng OMX; nếu cần hiểu call graph, dùng CodeGraph. Jev Review có thể là một bước trong hệ thống đó.

Hướng đóng góp khác biệt nên tập trung vào .NET integration, package installation đáng tin cậy, evidence contract và benchmark công khai; không thêm keywords hoặc nhiều agents chỉ để giống công cụ khác.
