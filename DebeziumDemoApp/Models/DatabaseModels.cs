using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DebeziumDemoApp.Models;

[Table("products")]
public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int Stock { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("categories")]
public class Category
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string OrderNumber { get; set; } = string.Empty;

    public int? CustomerId { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = "pending";

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}