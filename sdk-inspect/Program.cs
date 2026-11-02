using System.Reflection;
using System.Runtime.Loader;
var candidates = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
    .Where(p => Path.GetFileName(p).Contains("busy", StringComparison.OrdinalIgnoreCase)
             || Path.GetFileName(p).Contains("plenom", StringComparison.OrdinalIgnoreCase)
             || Path.GetFileName(p).Contains("kuando", StringComparison.OrdinalIgnoreCase)).ToList();
foreach (var path in candidates)
{
    Console.WriteLine("ASSEMBLY FILE " + path);
    Assembly assembly;
    try { assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path); }
    catch(Exception e) { Console.WriteLine("LOAD ERROR " + e); continue; }
    Console.WriteLine("SDK ASSEMBLY: "+assembly.FullName);
    Type[] types;
    try { types = assembly.GetExportedTypes(); }
    catch(ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); foreach(var e in ex.LoaderExceptions) Console.WriteLine("TYPE ERROR "+e); }
    foreach (var t in types.OrderBy(t=>t.FullName))
    {
        Console.WriteLine("\nTYPE "+ t.FullName+" ["+(t.IsEnum?"enum":t.IsClass?"class":t.IsValueType?"struct":"other")+"]");
        if(t.IsEnum) foreach(var n in Enum.GetNames(t)) Console.WriteLine(" ENUM "+n+" = "+Convert.ToInt64(Enum.Parse(t,n)));
        foreach(var c in t.GetConstructors(BindingFlags.Public|BindingFlags.Instance)) Console.WriteLine(" CTOR "+c);
        foreach(var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine(" PROP "+p.PropertyType.FullName+" "+p.Name+" get="+p.CanRead+" set="+p.CanWrite);
        foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine(" FIELD "+f.FieldType.FullName+" "+f.Name);
        foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m=>!m.IsSpecialName)) Console.WriteLine(" METHOD "+m);
    }
}
if(candidates.Count == 0) { Console.Error.WriteLine("No SDK candidate DLL found"); Environment.ExitCode=1; }
