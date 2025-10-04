namespace AcornDB.Models
{
    /// <summary>
    /// INutment: optional identity interface for nutments that bring their own IDs.
    /// </summary>
    public interface INutment<TKey>
    {
        TKey Id { get; set; }
    }
}
