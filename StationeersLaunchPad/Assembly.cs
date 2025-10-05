using Mono.Cecil;
using System;
using System.Reflection;

namespace StationeersLaunchPad
{
  public enum AssemblyType
  {
    Unknown = 0,
    Mod,
    Game,
    System,
    Unity,
  }

  public struct AssemblyInfo
  {
    public string Path;
    public string Name => this.NameDefinition.Name;

    public AssemblyDefinition Definition;
    public AssemblyNameDefinition NameDefinition => this.Definition.Name;

    public string PublicKeyToken => this.NameDefinition.HasPublicKey ? BitConverter.ToString(this.NameDefinition.PublicKeyToken) : "";

    public AssemblyType Type()
    {
      if (this.IsModAssembly())
        return AssemblyType.Mod;

      if (this.IsGameAssembly())
        return AssemblyType.Game;

      if (this.IsUnityAssembly())
        return AssemblyType.Unity;

      if (this.IsSystemAssembly())
        return AssemblyType.System;

      return AssemblyType.Unknown;
    }

    public bool IsModAssembly() => !this.Path.StartsWith(LaunchPadPaths.GameRootPath);

    // Anything that is not system or unity assembly will be considered a game assembly
    public bool IsGameAssembly() => !this.IsSystemAssembly() && !this.IsUnityAssembly();

    // System assemblies are marked with public key tokens, we can use that to determine if it is a system assembly or not
    public bool IsSystemAssembly() => this.PublicKeyToken switch
    {
      "b77a5c561934e089" => true, // mscorlib, System.*
      "0738eb9f132ed756" => true, // Mono.*
      "50cebf1cceb9d05e" => true, // Mono.Cecil.*
      "b03f5f7f11d50a3a" => true, // System.Core, System.Data, etc.
      "7cec85d7bea7798e" => true, // .NET Core
      "cc7b13ffcd2ddd51" => true, // netstandard, WindowsBase, PresentationCore, etc.
      "31bf3856ad364e35" => true, // Microsoft.*
      "30ad4fe6b2a6aeed" => true, // Newtonsoft.Json
      _ => false,
    };

    // Unity assemblies unfortunately dont have public keys so we will have to stick with using the name
    public bool IsUnityAssembly() => this.Name.StartsWith("Unity", StringComparison.InvariantCultureIgnoreCase);
  }

  public struct LoadedAssembly
  {
    public string Name => this.Info.Name;
    public string Path => this.Info.Path;

    public AssemblyInfo Info;
    public AssemblyType Type => this.Info.Type();
    public AssemblyDefinition Definition => this.Info.Definition;
    public AssemblyNameDefinition NameDefinition => this.Info.NameDefinition;
    public Assembly Assembly;
  }
}