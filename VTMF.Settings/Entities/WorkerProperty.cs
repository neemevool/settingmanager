using System.ComponentModel.DataAnnotations;

namespace VTMF.Settings.Entities;

public class WorkerProperty
{
    [Required(ErrorMessage = "Worker code is required.")]
    [StringLength(127, ErrorMessage = "Worker code must be 127 characters or fewer.")]
    public string WorkerCode { get; set; } = string.Empty;

    [StringLength(50)]
    public string? PrimaryStock { get; set; }

    [StringLength(200)]
    public string? Stocks { get; set; }

    [StringLength(50)]
    public string? ProcessTag { get; set; }

    [StringLength(500)]
    public string? ProcessNames { get; set; }

    [StringLength(50)]
    public string? PrimaryDepartment { get; set; }

    [StringLength(50)]
    public string? ProcessGroup { get; set; }
}
