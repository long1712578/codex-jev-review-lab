namespace JevReview;

public static class DatasetCollector
{
    public static ReviewItem[] Load(string path)
    {
        try
        {
            var file = Directory.Exists(path) ? System.IO.Path.Combine(path, "items.json") : path;
            var items = file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? JsonFiles.ReadLines<ReviewItem>(file) : JsonFiles.Read<ReviewItem[]>(file);
            if (items.Length == 0 || items.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) ||
                items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new ArgumentException("empty_or_duplicate_item_ids");
            return items;
        }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("invalid_dataset_schema"); }
    }
}
