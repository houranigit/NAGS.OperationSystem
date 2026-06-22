namespace BuildingBlocks.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Root directory for stored files. Must be outside the served/executable path.</summary>
    public string RootPath { get; set; } = "App_Data/storage";
}
