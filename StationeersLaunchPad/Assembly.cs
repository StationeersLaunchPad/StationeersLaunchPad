using Mono.Cecil;
using System.Reflection;

namespace StationeersLaunchPad
{
  public struct AssemblyInfo
  {
    public string Path;
    public string Name;
    public AssemblyDefinition Definition;
  }

  public struct LoadedAssembly
  {
    public AssemblyInfo Info;
    public Assembly Assembly;
  }
}