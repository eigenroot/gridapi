namespace Grid.Api.Data;

/// <summary>Base for every table: a time-ordered UUID key and UTC timestamps set on save.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
