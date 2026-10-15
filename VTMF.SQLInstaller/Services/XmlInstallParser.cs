using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VTMF.SQLInstaller.Entities;

namespace VTMF.SQLInstaller.Services;

public class XmlInstallParser
{
    private readonly InstallConfig _config;

    public XmlInstallParser(InstallConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Parses the install.xml file and returns an Install object.
    /// </summary>
    public static Install Parse(string xmlFilePath)
    {
        //_logger.LogInformation("Parsing install configuration from: {XmlFile}", xmlFilePath);

        if (!File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException($"Install configuration file not found: {xmlFilePath}");
        }

        var doc = XDocument.Load(xmlFilePath);
        var installElement = doc.Root;

        if (installElement == null || installElement.Name != "install")
        {
            throw new InvalidOperationException("Invalid install.xml format. Root element must be <install>.");
        }

        var install = new Install();

        foreach (var setElement in installElement.Elements("set"))
        {
            var setName = setElement.Attribute("name")?.Value
              ?? throw new InvalidOperationException("Set element must have a 'name' attribute.");
            var isOptionalStr = setElement.Attribute("optional")?.Value ?? "false";
            bool.TryParse(isOptionalStr, out var isOptional);

            var installSet = new InstallSet(setName, isOptional);

            foreach (var itemElement in setElement.Elements("item"))
            {
                var name = itemElement.Attribute("name")?.Value
               ?? throw new InvalidOperationException("Item element must have a 'name' attribute.");

                var check = itemElement.Attribute("check")?.Value
                          ?? throw new InvalidOperationException($"Item '{name}' must have a 'check' attribute.");

                var isFolderStr = itemElement.Attribute("isfolder")?.Value ?? "false";
                var isFolder = bool.Parse(isFolderStr);

                var type = itemElement.Attribute("type")?.Value
             ?? throw new InvalidOperationException($"Item '{name}' must have a 'type' attribute.");

                var source = itemElement.Attribute("source")?.Value
              ?? throw new InvalidOperationException($"Item '{name}' must have a 'source' attribute.");

                var installItem = new InstallItem(name, check, isFolder, type, source);

                foreach (var specElement in itemElement.Elements("spec"))
                {
                    var specName = specElement.Attribute("name")?.Value
                   ?? throw new InvalidOperationException($"Spec element in item '{name}' must have a 'name' attribute.");
                    var specValue = specElement.Attribute("value")?.Value
                                  ?? throw new InvalidOperationException($"Spec element '{specName}' in item '{name}' must have a 'value' attribute.");
                    var installItemSpec = new InstallItemSpec(specName, specValue);
                
                    installItem.Spec.Add(installItemSpec);

                    //_logger.LogDebug("Added spec: {SpecName}={SpecValue} to item: {ItemName}",
                    // specName, specValue, name);
                }

                
                installSet.Items.Add(installItem);

                //_logger.LogDebug("Added item: {ItemName} (type: {Type}, check: {Check}, isFolder: {IsFolder})",
                // name, type, check, isFolder);
            }

            install.Sets.Add(installSet);
            //_logger.LogInformation("Added set: {SetName} with {ItemCount} items", setName, installSet.Items.Count);
        }

        //_logger.LogInformation("Successfully parsed {SetCount} installation sets", install.Sets.Count);
        return install;
    }

    
}
