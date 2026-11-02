using System.Reflection;
using Busylight;
var assembly = typeof(KuandoSDK).Assembly;
Console.WriteLine("SDK ASSEMBLY: "+assembly.FullName);
foreach (var t in assembly.GetExportedTypes().Where(t => t.FullName?.Contains("Busylight", StringComparison.OrdinalIgnoreCase) == true || t.Name.Contains("Priority") || t.Name.Contains("Event")).OrderBy(t=>t.FullName))
{
 Console.WriteLine("\nTYPE "+ t.FullName+" ["+(t.IsEnum?"enum":t.IsClass?"class":t.IsValueType?"struct":"other")+"]");
 if(t.IsEnum) foreach(var n in Enum.GetNames(t)) Console.WriteLine(" ENUM "+n+" = "+Convert.ToInt64(Enum.Parse(t,n)));
 foreach(var c in t.GetConstructors(BindingFlags.Public|BindingFlags.Instance)) Console.WriteLine(" CTOR "+c);
 foreach(var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine(" PROP "+p.PropertyType.FullName+" "+p.Name+" get="+p.CanRead+" set="+p.CanWrite);
 foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine(" FIELD "+f.FieldType.FullName+" "+f.Name);
 foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m=>!m.IsSpecialName)) Console.WriteLine(" METHOD "+m);
}