using System.Reflection;

namespace BuildingBlocks.Domain.Enumerations;

public abstract class Enumeration : IEquatable<Enumeration>
{
    public int Id { get; }
    public string Name { get; }

    protected Enumeration(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public static T? FromValue<T>(int value) where T : Enumeration =>
        GetAll<T>().FirstOrDefault(e => e.Id == value);

    public static T? FromName<T>(string name) where T : Enumeration =>
        GetAll<T>().FirstOrDefault(e => e.Name == name);

    public static IEnumerable<T> GetAll<T>() where T : Enumeration =>
        typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(T))
            .Select(f => (T)f.GetValue(null)!);

    public bool Equals(Enumeration? other) =>
        other is not null && GetType() == other.GetType() && Id == other.Id;

    public override bool Equals(object? obj) => obj is Enumeration e && Equals(e);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => Name;
}
