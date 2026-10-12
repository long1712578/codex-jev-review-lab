using System.Net.Http.Headers;
using System.Text;

namespace JevReview;
public sealed class JevJudgeClient(HttpClient http, string apiKey, RunOptions options) : IJudgeClient
{
    public async Task<JudgeResult> JudgeAsync(ReviewItem item, ReviewRubric rubric, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ArgumentException("invalid_credential_format");
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(BuildRequest(item, rubric, options.Model), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (retryAfter == null && response.Headers.RetryAfter?.Date is { } date) retryAfter = date - DateTimeOffset.UtcNow;
                throw new JudgeException($"http_{status}", status == 408 || status == 429 || status >= 500, retryAfter);
            }
            // Bound response memory independently of the input budget.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, cancellationToken)) != 0)
            {
                if (buffer.Length + read > 1_048_576) throw new JudgeException("invalid_response");
                buffer.Write(bytes, 0, read);
            }
            return JevResponseParser.Parse(Encoding.UTF8.GetString(buffer.ToArray()), item.Id, rubric);
        }
        catch (Exception e) when (e is HttpRequestException or IOException) { throw new JudgeException("network_error", true); }
    }

    public static string BuildRequest(ReviewItem item, ReviewRubric rubric, string model) => JsonFiles.Serialize(new
    {
        model,
        state = new { item.Task, item.Path, item.Diff, item.Context },
        questions = new Dictionary<string, object>
        {
            [item.Id] = new { type = "choice", instructions = rubric.Instructions, criteria = rubric.Labels }
        }
    });
}
