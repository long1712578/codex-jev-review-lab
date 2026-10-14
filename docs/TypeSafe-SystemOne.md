# TypeSafe System One trong .NET — tài liệu dùng một file

Cập nhật 14/10/2026. Tổng hợp nội dung người dùng cung cấp và đối chiếu tài liệu chính thức. File này nằm thật trong repo; có thể mở hoặc tải/copy toàn bộ file, không cần ghép các message.

## 1. System One và vai trò của Jev

System One đưa ra các phán đoán hẹp bằng đầu ra có kiểu. Jev là mô hình của TypeSafe cho lớp quyết định đó. Với review code, dùng nó để phân loại và định tuyến; reviewer vẫn đọc implementation/caller/tests để giải thích và xác minh. Không yêu cầu Jev sinh code hoặc viết lý do cho finding. Xác suất có ý nghĩa về độ không chắc chắn, không bảo đảm một câu trả lời cụ thể đúng. Căn cứ: [System One](https://docs.typesafe.ai/concepts/system-one).

## 2. State

State chứa dữ liệu và context; question chứa điều cần đánh giá. Có thể dùng chuỗi, JSON object hoặc array. Object phù hợp khi cần gắn tên cho các phần dữ liệu liên quan. Jev hiện nhận văn bản; không truyền image/audio/video. Căn cứ: [State](https://docs.typesafe.ai/concepts/state).

State review đề xuất:

```json
{
  "task": "Refactor without changing business behavior",
  "path": "src/Orders/OrderValidator.cs",
  "diff": "-if (quantity > 10)\n+if (quantity >= 10)",
  "context": "This condition selects the discount boundary. Caller/test context is not supplied."
}
```

Context phải là dữ kiện thật hoặc ghi rõ chưa có. Với review xuyên file, thiếu implementation/caller thì giữ `insufficient_context` và lấy thêm bằng chứng. Không đưa gold labels vào state.

## 3. Primitives

| Primitive | Dùng khi | Output |
| --- | --- | --- |
| Choice | Chọn nhãn từ tập cố định không có thứ tự | `choice`, `probabilities`, `confidence` |
| Score | Đánh giá theo các mức có thứ tự | `score`, `legend`, `probabilities`, `confidence` |
| Noul | Câu hỏi yes/no cụ thể | `noul` từ 0 đến 1, là xác suất yes |

Mỗi question có ID, `type`, `instructions`; Choice/Score cần `criteria`, Noul có criteria tùy chọn. ID dùng để map answer trong code, không thay thế một instructions đầy đủ. Nhiều questions trong một request cùng nhìn một state và được đánh giá riêng; code kết hợp kết quả theo policy. Căn cứ: [Primitives](https://docs.typesafe.ai/primitives).

Độc lập trong cách đánh giá không đồng nghĩa các tín hiệu độc lập thống kê. Không nhân các probability có tương quan để suy ra một xác suất “code an toàn”. Cũng không tự suy ra gửi nhiều câu luôn rẻ hơn với workload cụ thể; đo latency/usage/billing thực tế.

Repo này đang dùng **Choice bốn nhãn**. Noul/Score chưa có DTO/parser/policy trong Jev Review; muốn bổ sung cần một thay đổi contract có tests và benchmark.

## 4. Probability và confidence

Probability của nhãn là một phần phân phối. Confidence là thống kê tóm tắt phân phối, không đồng nhất với probability được chọn. Noul hiện không trả confidence riêng. Policy repo dùng probability của `refactor_only` và paths nhạy cảm, không lấy confidence thay probability. Ngưỡng `.8` là policy default cần hiệu chỉnh bằng dữ liệu dev; không phải chứng nhận đúng. Căn cứ: [Confidence](https://docs.typesafe.ai/confidence).

## 5. Sửa hiểu nhầm về SDK .NET trong bản trao đổi

Đoạn `dotnet add package TypeSafe.SDK`, `using TypeSafe.SDK`, `new Choice`, `new Noul` trong cuộc trao đổi chưa phải một ví dụ đã compile/đối chiếu đúng API package hiện hành. Không dùng nguyên đoạn đó làm quickstart đáng tin cậy.

NuGet hiện có [TypeSafeAI.Sdk](https://www.nuget.org/packages/TypeSafeAI.Sdk), version quan sát `0.1.0-alpha.2`, tự ghi rõ là **SDK cộng đồng, không liên kết hay được TypeSafe bảo chứng**. Trang package dùng namespace `TypeSafeAI`, các kiểu `ChoiceQuestion`, `NoulQuestion`, `ScoreQuestion`; tên cũ `TypeSafe.Sdk` có hướng dẫn migration. Vì vậy không kết luận “.NET SDK không tồn tại”, cũng không gọi SDK cộng đồng là official.

Jev Review hiện dùng BCL `HttpClient` và `System.Text.Json`, không phụ thuộc SDK đó. Đây là lựa chọn giữ adapter nhỏ; nếu đổi sang SDK sau này cần đánh giá retries/timeouts/redaction để tránh hai tầng retry nhân số lần gọi. [Index tài liệu TypeSafe](https://docs.typesafe.ai/llms.txt) liệt kê SDK Python/JavaScript; API HTTP là nền tảng dùng cho adapter .NET trong repo.

## 6. Quickstart HTTP bằng .NET

API hiện dùng `POST https://api.typesafe.ai/v1/systemone`, Bearer auth, `state`, `model`, `questions`; answers trả dưới cùng question ID. Schema chi tiết ở [API reference](https://docs.typesafe.ai/api). Ví dụ dưới dùng Choice và chỉ in label/probability đã validate. **Chạy đoạn này gửi một request live, có thể tính phí.** Nó minh họa một call; chưa phải runner production có retry/checkpoint của repo.

Trong console project .NET 10, đặt key trong environment qua cơ chế secret local, rồi dùng:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var key = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
if (string.IsNullOrWhiteSpace(key) || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
    throw new InvalidOperationException("missing_or_invalid_api_key");

var labels = new Dictionary<string, string>
{
    ["refactor_only"] = "Only structure changes; observable behavior is preserved.",
    ["behavior_change"] = "Observable outcomes, conditions or side effects change.",
    ["mixed"] = "Separate refactoring and behavior-changing edits are both present.",
    ["insufficient_context"] = "Necessary implementation or caller context is missing."
};
var payload = JsonSerializer.Serialize(new
{
    model = "jev-latest",
    state = new
    {
        task = "Refactor without changing business behavior",
        path = "OrderValidator.cs",
        diff = "-if (quantity > 10)\n+if (quantity >= 10)",
        context = "This comparison selects the discount boundary."
    },
    questions = new Dictionary<string, object>
    {
        ["review"] = new
        {
            type = "choice",
            instructions = "Classify this change against observable behavior. Choose insufficient_context when necessary evidence is absent.",
            criteria = labels
        }
    }
});

using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var request = new HttpRequestMessage(HttpMethod.Post,
    "https://api.typesafe.ai/v1/systemone");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
try
{
    using var response = await http.SendAsync(request,
        HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"typesafe_http_{(int)response.StatusCode}");
        Environment.ExitCode = 1;
        return;
    }
    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
    using var buffer = new MemoryStream();
    var bytes = new byte[8192];
    int read;
    while ((read = await stream.ReadAsync(bytes, timeout.Token)) != 0)
    {
        if (buffer.Length + read > 1_048_576)
            throw new InvalidOperationException("invalid_response");
        buffer.Write(bytes, 0, read);
    }
    using var json = JsonDocument.Parse(buffer.ToArray());
    var answers = json.RootElement.GetProperty("answers");
    if (answers.EnumerateObject().Count() != 1)
        throw new InvalidOperationException("invalid_response");
    var answer = answers.GetProperty("review");
    var label = answer.GetProperty("choice").GetString();
    var probabilities = answer.GetProperty("probabilities").EnumerateObject()
        .ToDictionary(p => p.Name, p => p.Value.GetDouble());
    var confidence = answer.GetProperty("confidence").GetDouble();
    if (answer.GetProperty("type").GetString() != "choice" || label is null ||
        !labels.ContainsKey(label) || probabilities.Count != labels.Count ||
        labels.Keys.Any(name => !probabilities.ContainsKey(name)) ||
        probabilities.Values.Any(p => !double.IsFinite(p) || p < 0 || p > 1) ||
        Math.Abs(probabilities.Values.Sum() - 1) > .001 ||
        !double.IsFinite(confidence) || confidence < 0 || confidence > 1 ||
        probabilities[label] + .000001 < probabilities.Values.Max())
        throw new InvalidOperationException("invalid_response");
    Console.WriteLine($"Prediction: {label}; probability: {probabilities[label]:F3}");
    Console.WriteLine("Reviewer verification is still required.");
}
catch (Exception error) when (error is HttpRequestException or IOException or
    OperationCanceledException or JsonException or InvalidOperationException or
    KeyNotFoundException or ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine("typesafe_call_failed_or_invalid_response");
    Environment.ExitCode = 1;
}
```

Ví dụ C# trên đã được trích nguyên văn và compile bằng .NET SDK 10.0.303: 0 warnings, 0 errors. Chưa chạy request live; compile không chứng minh dịch vụ trả label nào hoặc hoạt động với credential của bạn.

Không log key, payload, response body hoặc exception message. Trong production, dùng vòng đời HttpClient ổn định/host DI, retries có giới hạn cho lỗi tạm thời, cancellation, input budget và typed DTO; adapter/runner hiện có trong repo đã được offline test cho các phần này. Retry cho authentication/schema errors không giúp sửa dữ liệu sai.

## 7. Từ classification đến finding

1. Chốt task/rubric trước bulk input.
2. Thu diff bằng code và ghi input hash/Git SHAs.
3. Jev trả prediction có kiểu; validate đáp án và distribution.
4. Policy bằng code chọn behavior change, mixed, thiếu context, low probability, paths nhạy cảm và mẫu quality.
5. Reviewer đọc source/callers/tests để xác nhận, bác bỏ hoặc ghi unresolved.
6. Report giữ counts và phạm vi chưa review; exit 0 không chứng minh PR an toàn.

Mọi item lỗi, quá lớn hoặc cancelled vẫn cần record/coverage. Fake fixtures chỉ kiểm thử pipeline. Chỉ so sánh chất lượng sau khi có baseline và gold độc lập ngoài inference workspace.

## 8. Đường dùng trong repo

- [Cài đặt và setup project riêng](installation.md): lệnh hiện có đã kiểm tra offline.
- [Review kỹ thuật](community-review-2026-10-14.md): lỗi tái hiện và hạn chế release.
- [Thiết kế và roadmap](superpowers/specs/2026-10-14-community-distribution-design.md): core/tool/MCP mục tiêu, trạng thái đề xuất.
- [So sánh lợi ích](comparison.md): cách kết hợp OMX/CodeGraph và những tuyên bố cần benchmark.
- [Quy trình evaluation](experiment-plan.md): ranh giới gold, baseline và metrics.
