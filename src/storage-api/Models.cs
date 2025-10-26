namespace StorageApi;

public class StorageItem
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
