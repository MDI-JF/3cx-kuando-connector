using System.Reflection;
using System.Xml.Linq;

var packageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".nuget", "packages", "com.plenom.busylightsdk", "4.0.7");
Console.WriteLine("PACKAGE DIR: " + packageDir);
if (!Directory.Exists(packageDir)) throw new DirectoryNotFoundException(packageDir);
foreach (var file in Directory.GetFiles(packageDir, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(packageDir, file);
    Console.WriteLine("FILE: " + relative);
    if (relative.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
    {
        var doc = XDocument.Load(file);
        var md = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        foreach (var e in md?.Elements() ?? [])
            if (new[]{"license","licenseUrl","copyright","authors","owners","projectUrl","repository","description"}.Contains(e.Name.LocalName))
                Console.WriteLine("  META: " + e.Name.LocalName + "=" + e.ToString(SaveOptions.DisableFormatting));
    }
    if (Path.GetFileName(file).Contains("LICENSE", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(file).Contains("COPYING", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(file).Contains("NOTICE", StringComparison.OrdinalIgnoreCase))
    {
        if (new FileInfo(file).Length < 150000)
            Console.WriteLine("  LICENSE CONTENT:\n" + File.ReadAllText(file));
    }
}
var sdkDll = Directory.GetFiles(packageDir, "BusylightSDK.dll", SearchOption.AllDirectories)
  .OrderBy(p => p.Contains("netcoreapp3.1") ? 0 : 1).First();
var assy = Assembly.LoadFrom(sdkDll);
Console.WriteLine("SDK: " + assy.FullName);
foreach(var att in assy.GetCustomAttributesData())
  if(att.AttributeType.Name.Contains("Company")||att.AttributeType.Name.Contains("Copyright")||att.AttributeType.Name.Contains("Description"))
    Console.WriteLine("ATTRIBUTE: " + att);
var type = assy.GetType("Busylight.KuandoSDK")!;
foreach(var ctor in type.GetConstructors())
{
  Console.WriteLine("CONSTRUCTOR: " + ctor);
  foreach(var p in ctor.GetParameters()) Console.WriteLine(
    "  PARAM: " + p.Name + " type=" + p.ParameterType + " optional="+p.IsOptional);
}
Console.WriteLine("PROPERTIES: " + string.Join(", ",type.GetProperties().Select(p=>p.Name)));
