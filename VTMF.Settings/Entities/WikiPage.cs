using System.ComponentModel.DataAnnotations;

namespace VTMF.Settings.Entities;

public class WikiPage
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(255, ErrorMessage = "Title must be 255 characters or fewer.")]
    public string Title { get; set; } = string.Empty;

    public string? Content { get; set; }

    public DateTime LastUpdated { get; set; }
}
