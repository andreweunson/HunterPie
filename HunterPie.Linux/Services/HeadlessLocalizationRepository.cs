using HunterPie.Core.Client;
using HunterPie.Core.Client.Localization;
using HunterPie.Core.Client.Localization.Entity;
using HunterPie.Features.Languages.Repository;
using System.Reflection;
using System.Xml;

namespace HunterPie.Linux.Services;

/// <summary>
/// Same lookups as the WPF client's LocalizationRepository, but a missing language file is not fatal:
/// the Localization git submodule is optional for a headless run, and lookups then return the XPath itself.
/// Only en-us.xml is read, since nothing user-facing is translated in a console.
/// </summary>
internal sealed class HeadlessLocalizationRepository : ILocalizationRepository
{
    private readonly Lazy<XmlDocument?> _document = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public LocalizationData FindBy(string path)
    {
        XmlAttributeCollection? attributes = TryFind(path);

        if (attributes?["String"]?.Value is not { } stringValue)
            return CreateDefault(path);

        return new LocalizationData(
            String: stringValue,
            Description: attributes["Description"]?.Value ?? path,
            Affixation: Enum.TryParse(attributes["Affixation"]?.Value, out Affixation affixation) ? affixation : Affixation.Prefix,
            Order: short.TryParse(attributes["Order"]?.Value, out short order) ? order : (short)0
        );
    }

    public bool ExistsBy(string path) => TryFind(path)?["String"]?.Value is not null;

    public string FindStringBy(string path) => TryFind(path)?["String"]?.Value ?? path;

    public LocalizationData FindByEnum<T>(T value) where T : notnull
    {
        string name = value.ToString() ?? string.Empty;
        MemberInfo? member = value.GetType().GetMember(name).FirstOrDefault();
        LocalizationAttribute? attribute = member?.GetCustomAttribute<LocalizationAttribute>();

        return attribute is { } ? FindBy(attribute.XPath) : CreateDefault(name);
    }

    public IScopedLocalizationRepository WithScope(string scope) => new ScopedLocalizationRepository(scope, this);

    private XmlAttributeCollection? TryFind(string path)
    {
        if (_document.Value is not { } document)
            return null;

        try
        {
            return document.SelectSingleNode(path)?.Attributes;
        }
        catch (System.Xml.XPath.XPathException)
        {
            return null;
        }
    }

    private static LocalizationData CreateDefault(string path) => new(path, path, Affixation.Prefix, 0);

    private static XmlDocument? Load()
    {
        string path = Path.Combine(ClientInfo.LanguagesPath, "en-us.xml");

        if (!File.Exists(path))
            return null;

        var document = new XmlDocument();
        document.Load(path);

        return document;
    }
}
