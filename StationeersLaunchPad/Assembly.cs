using BepInEx;
using Mono.Cecil;
using System;
using System.Reflection;

namespace StationeersLaunchPad
{
  public struct AssemblyInfo
  {
    public string Path;
    public string Name => this.NameDefinition.Name;

    public AssemblyDefinition Definition;
    public AssemblyNameDefinition NameDefinition => this.Definition.Name;
  }

  public struct LoadedAssembly
  {
    public string Name => this.Info.Name;
    public string Path => this.Info.Path;

    public AssemblyInfo Info;
    public AssemblyDefinition Definition => this.Info.Definition;
    public AssemblyNameDefinition NameDefinition => this.Info.NameDefinition;
    public Assembly Assembly;
  }
}