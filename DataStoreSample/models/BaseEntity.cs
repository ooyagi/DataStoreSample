namespace MyMemoryStore;

public abstract class BaseEntity
{
    public string Id { get; set; }

    protected BaseEntity()
    {
        Id = CreateId();
    }

    abstract public void Update<T>(T other) where T : BaseEntity;

    private string CreateId() => Guid.NewGuid().ToString();
}

public abstract record BaseView(string Id);
