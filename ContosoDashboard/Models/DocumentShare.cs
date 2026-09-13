using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// Sharing target is exactly one of SharedWithUserId or SharedWithProjectId (validated in DocumentService)
public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int SharedByUserId { get; set; }

    public int? SharedWithUserId { get; set; }

    public int? SharedWithProjectId { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User? SharedWithUser { get; set; }

    [ForeignKey("SharedWithProjectId")]
    public virtual Project? SharedWithProject { get; set; }
}
