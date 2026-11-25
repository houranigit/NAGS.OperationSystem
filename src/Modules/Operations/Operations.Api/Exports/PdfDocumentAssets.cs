using System.Reflection;
using PdfSharp.Fonts;

namespace Operations.Api.Exports;

internal static class PdfDocumentAssets
{
    public const string FontFamily = "Open Sans";

    private static readonly object FontResolverLock = new();

    public static void EnsureFontResolver()
    {
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new OpenSansFontResolver();
        }
    }

    public static Stream OpenEmbeddedResource(string resourceName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded PDF resource '{resourceName}' was not found.");

    private sealed class OpenSansFontResolver : IFontResolver
    {
        private const string RegularFace = "OperationsOpenSansRegular";
        private const string BoldFace = "OperationsOpenSansBold";

        private static readonly Lazy<byte[]> RegularFont = new(() => LoadFont("Operations.Api.Fonts.OpenSans-Regular.ttf"));
        private static readonly Lazy<byte[]> BoldFont = new(() => LoadFont("Operations.Api.Fonts.OpenSans-Bold.ttf"));

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace, mustSimulateBold: false, mustSimulateItalic: isItalic);

        public byte[]? GetFont(string faceName) => faceName switch
        {
            RegularFace => RegularFont.Value,
            BoldFace => BoldFont.Value,
            _ => null
        };

        private static byte[] LoadFont(string resourceName)
        {
            using var stream = OpenEmbeddedResource(resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
