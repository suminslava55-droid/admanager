namespace AdManager.Models;

public sealed class ReviewResult
{
    public ReviewSummary Summary { get; set; } = new();
    public List<ReviewIssue> Issues { get; set; } = new();
}
