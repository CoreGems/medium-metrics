namespace MediumMetrics.Models;

/// <summary>
/// One recorded title for a story at a point in time. Appended to title-history.csv only
/// when a story's title changes (or on first sighting), so the list reads as a change log.
/// </summary>
public sealed class TitleChange
{
    public DateTimeOffset CapturedAt { get; set; }
    public string Title { get; set; } = "";
}
