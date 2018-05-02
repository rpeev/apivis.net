using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ApiVis {
  public class Api {
    public static AppDomain AppDomain { get; private set; }
    public static IEnumerable<Assembly> AppDomainAssemblies { get; private set; }
    public static IEnumerable<Type> AppDomainTypes { get; private set; }

    static Api() {
      AppDomain = AppDomain.CurrentDomain;
      AppDomainAssemblies = AppDomain.
        GetAssemblies().
        OrderBy(a => a.FullName);
      AppDomainTypes = AppDomainAssemblies.
        Aggregate(new List<Type>(), (types, assembly) => {
          types.AddRange(assembly.GetTypes()); return types;
        }).
        Where(t => !t.IsDefined(typeof (CompilerGeneratedAttribute), false)).
        OrderBy(t => t.Assembly.FullName).
        ThenBy(t => t.Namespace).
        ThenBy(t => t.FullName);
    }

    public static bool IsStaticClass(Type t) {
      return t.IsAbstract && t.IsSealed;
    }

    public static bool IsExtensionClass(Type t) {
      return (!t.IsNested && !t.IsGenericType) &&
        IsStaticClass(t) &&
        t.IsDefined(typeof (ExtensionAttribute), false);
    }

    public Assembly Assembly { get; private set; }
    public IEnumerable<Type> AssemblyTypes { get; private set; }
    public IEnumerable<string> AssemblyNamespaces { get; private set; }

    public Api(Assembly assembly = null) {
      Assembly = assembly ?? (typeof (object)).Assembly;
      AssemblyTypes = Assembly.
        GetTypes().
        Where(t => !t.IsDefined(typeof (CompilerGeneratedAttribute), false)).
        OrderBy(t => t.Namespace).
        ThenBy(t => t.FullName);
      AssemblyNamespaces = AssemblyTypes.
        GroupBy(t => t.Namespace).
        Select(g => g.First().Namespace).
        OrderBy(ns => ns);
    }

    public Api(Type t) : this(t.Assembly) {}

    public string AssemblyStr(string indent = "") {
      var name = Assembly.FullName;
      var location = Assembly.Location;

      return $"{indent}{name}\n{indent}{location}";
    }

    public string AssemblyTypesStr(string indent = "") {
      var types = AssemblyNamespaces.
        Select(ns => AssemblyNamespaceTypesStr(ns, indent));

      return $"{String.Join("\n", types)}";
    }

    public string AssemblyNamespacesStr(string indent = "") {
      var namespaces = AssemblyNamespaces;

      return $"{indent}{String.Join($"\n{indent}", namespaces)}";
    }

    public IEnumerable<Type> AssemblyNamespaceTypes(string ns) {
      return AssemblyTypes.
        Where(t => ns == t.Namespace).
        OrderBy(t => t.FullName);
    }

    public string AssemblyNamespaceTypesStr(string ns, string indent = "") {
      var types = AssemblyNamespaceTypes(ns).
        Select(t => {
          var nest = NestingStr(t);
          var type = TypeStr(t);
          var desc = AssemblyTypeDescStr(t);
          var kind = KindStr(t);

          return $"{nest}{type}{desc}: {kind}";
        });

      return $"{indent}{ns}\n{indent}  {String.Join($"\n{indent}  ", types)}";
    }

    public string KindStr(Type t) {
      if (t.IsArray) {
        return "array";
      } else if (t.IsPointer) {
        return "pointer";
      } else if (t.IsPrimitive) {
        return "primitive";
      } else if (t.IsValueType) {
        if (t.IsEnum) {
          return "enum";
        } else {
          return "struct";
        }
      } else if (t.IsInterface) {
        return "interface";
      } else if (t.IsClass) {
        if (t.IsAbstract && t.IsSealed) {
          return "static class";
        } else if (t.IsAbstract) {
          return "abstract class";
        } else if (t.IsSealed) {
          return "final class";
        } else {
          return "class";
        }
      }

      return "unknown";
    }

    public string GenArgsStr(Type[] genArgs, bool namespaced = false) {
      var sb = new StringBuilder();

      for (var i = 0; i < genArgs.Length; i++) {
        var arg = genArgs[i];
        var constructed = !arg.IsGenericParameter;
        var nest = (constructed) ?
          NestingStr(arg, constructed && namespaced) :
          "";
        var type = TypeStr(arg, constructed && namespaced);

        if (i > 0) {
          sb.Append(", ");
        }
        sb.Append($"{nest}{type}");
      }

      return sb.ToString();
    }

    public string ArgsStr(ParameterInfo[] args, bool namespaced = false, string indent = "") {
      var sb = new StringBuilder();

      for (var i = 0; i < args.Length; i++) {
        var arg = args[i];
        var pass = ArgPassStr(arg);
        var name = arg.Name ?? $"arg{i + 1}";
        var val = ArgDefaultValueStr(arg);
        var nest = NestingStr(arg.ParameterType, namespaced);
        var type = TypeStr(arg.ParameterType, namespaced);
        var pad = (namespaced) ? $"\n{indent}  " : "";

        if (i > 0) {
          sb.Append((namespaced) ? "," : ", ");
        }
        sb.Append($"{pad}{pass}{name}{val}: {nest}{type}");
      }

      if (sb.Length > 0 && namespaced) {
        sb.Append($"\n{indent}");
      }

      return sb.ToString();
    }

    public string TypeStr(Type t, bool namespaced = false) {
      if (t.IsArray) {
        return $"{TypeStr(t.GetElementType(), namespaced)}[]";
      }
      if (t.IsPointer) {
        return $"{TypeStr(t.GetElementType(), namespaced)}*";
      }
      if (t.IsByRef) {
        return $"{TypeStr(t.GetElementType(), namespaced)}&";
      }
      if (!t.IsGenericType) {
        return (!t.IsNested && namespaced) ?
          $"{t.Namespace}.{t.Name}" :
          t.Name;
      }

      var posBacktick = t.Name.IndexOf('`');
      var nameNoBacktick = (posBacktick == -1) ?
        t.Name :
        t.Name.Substring(0, posBacktick);
      var name = (!t.IsNested && namespaced) ?
        $"{t.Namespace}.{nameNoBacktick}" :
        nameNoBacktick;
      var genArgs = GenArgsStr(t.GetGenericArguments(), namespaced);

      return $"{name}<{genArgs}>";
    }

    public string MethodNameStr(MethodBase meth, bool namespaced = false) {
      if (!meth.IsGenericMethod) {
        return meth.Name;
      }

      var name = meth.Name;
      var genArgs = GenArgsStr(meth.GetGenericArguments(), namespaced);

      return $"{name}<{genArgs}>";
    }

    public string AssemblyTypeDescStr(Type t) {
      var desc = new List<string>();

      if (!t.IsVisible) {
        desc.Add("hidden");
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string NestedTypeDescStr(Type t) {
      var desc = new List<string>();

      if (t.IsNestedFamily) {
        desc.Add("family");
      } else if (t.IsNestedFamORAssem) {
        desc.Add("family|assembly");
      } else if (t.IsNestedAssembly) {
        desc.Add("assembly");
      } else if (t.IsNestedFamANDAssem) {
        desc.Add("assembly&family");
      } else if (t.IsNestedPrivate) {
        desc.Add("private");
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string ArgPassStr(ParameterInfo arg) {
      if (arg.ParameterType.IsByRef) {
        return (arg.IsOut) ? "out " : "ref ";
      }

      return "";
    }

    public string ArgDefaultValueStr(ParameterInfo arg) {
      if (!arg.HasDefaultValue) {
        return "";
      }

      var val = arg.RawDefaultValue;

      if (val == null) {
        return " = Null";
      }

      return (val is string) ?
        $" = \"{val}\"" :
        $" = {val}";
    }

    public string MethodVisibilityStr(MethodBase meth) {
      if (meth.IsFamily) {
        return "family";
      } else if (meth.IsFamilyOrAssembly) {
        return "family|assembly";
      } else if (meth.IsAssembly) {
        return "assembly";
      } else if (meth.IsFamilyAndAssembly) {
        return "assembly&family";
      } else if (meth.IsPrivate) {
        return "private";
      }

      return "";
    }

    public string MethodDescStr(MethodBase meth) {
      var desc = new List<string>();
      var visibility = MethodVisibilityStr(meth);

      if (!String.IsNullOrEmpty(visibility)) {
        desc.Add(visibility);
      }

      if (meth.IsVirtual) {
        if (meth.IsFinal) {
          desc.Add("overriding");
        } else {
          if (meth.IsAbstract) {
            desc.Add("abstract");
          } else {
            desc.Add("overridable");
          }
        }
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string EventDescStr(EventInfo ev) {
      var desc = new List<string>();
      var addVisibility = MethodVisibilityStr(ev.AddMethod);
      var removeVisibility = MethodVisibilityStr(ev.RemoveMethod);

      if (String.IsNullOrEmpty(addVisibility)) {
        desc.Add("add");
      } else {
        desc.Add($"{addVisibility}_add");
      }

      if (String.IsNullOrEmpty(removeVisibility)) {
        desc.Add("remove");
      } else {
        desc.Add($"{removeVisibility}_remove");
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string PropertyDescStr(PropertyInfo prop) {
      var desc = new List<string>();
      var getVisibility = MethodVisibilityStr(prop.GetMethod);
      var setMethod = prop.SetMethod;

      if (String.IsNullOrEmpty(getVisibility)) {
        desc.Add("get");
      } else {
        desc.Add($"{getVisibility}_get");
      }

      if (setMethod != null) {
        var setVisibility = MethodVisibilityStr(setMethod);

        if (String.IsNullOrEmpty(setVisibility)) {
          desc.Add("set");
        } else {
          desc.Add($"{setVisibility}_set");
        }
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string FieldDescStr(FieldInfo field) {
      var desc = new List<string>();

      if (field.IsFamily) {
        desc.Add("family");
      } else if (field.IsFamilyOrAssembly) {
        desc.Add("family|assembly");
      } else if (field.IsAssembly) {
        desc.Add("assembly");
      } else if (field.IsFamilyAndAssembly) {
        desc.Add("assembly&family");
      } else if (field.IsPrivate) {
        desc.Add("private");
      } 

      if (field.IsInitOnly) {
        desc.Add("readonly");
      } else if (field.IsLiteral) {
        desc.Add("literal");
      }

      if (field.IsSpecialName) {
        desc.Add("special");
      }

      return (desc.Count > 0) ?
        $"{{{String.Join(" ", desc)}}}" :
        "";
    }

    public string NestedTypeStr(Type t, bool namespaced = false, string indent = "") {
      var nest = NestingStr(t, namespaced);
      var type = TypeStr(t, namespaced);
      var desc = NestedTypeDescStr(t);
      var kind = KindStr(t);

      return $"{nest}{type}{desc}: {kind}";
    }

    public string ConstructorStr(ConstructorInfo ctor, bool namespaced = false, string indent = "") {
      var name = MethodNameStr(ctor, namespaced);
      var desc = MethodDescStr(ctor);
      var args = ArgsStr(ctor.GetParameters(), namespaced, indent);
      var type = TypeStr(typeof (void), namespaced);

      return $"{name}{desc}({args}): {type}";
    }

    public string MethodStr(MethodInfo meth, bool namespaced = false, string indent = "") {
      var name = MethodNameStr(meth, namespaced);
      var desc = MethodDescStr(meth);
      var args = ArgsStr(meth.GetParameters(), namespaced, indent);
      var type = TypeStr(meth.ReturnType, namespaced);

      return $"{name}{desc}({args}): {type}";
    }

    public string ExtensionMethodStr(MethodInfo meth, bool namespaced = false, string indent = "") {
      var name = MethodNameStr(meth, namespaced);
      var desc = MethodDescStr(meth);
      var args = ArgsStr(meth.GetParameters().Skip(1).ToArray(), namespaced, indent);
      var type = TypeStr(meth.ReturnType, namespaced);

      return $"{name}{desc}({args}): {type}";
    }

    public string EventStr(EventInfo ev, bool namespaced = false, string indent = "") {
      var name = ev.Name;
      var desc = EventDescStr(ev);
      var type = TypeStr(ev.EventHandlerType, namespaced);

      return $"{name}{desc}: {type}";
    }

    public string PropertyStr(PropertyInfo prop, bool namespaced = false, string indent = "") {
      var name = prop.Name;
      var desc = PropertyDescStr(prop);
      var indexArgs = prop.GetIndexParameters();
      var args = (indexArgs.Length > 0) ?
        $"[{ArgsStr(indexArgs, namespaced, indent)}]" :
        "";
      var type = TypeStr(prop.PropertyType, namespaced);

      return $"{name}{desc}{args}: {type}";
    }

    public string FieldStr(FieldInfo field, bool namespaced = false, string indent = "") {
      var name = field.Name;
      var desc = FieldDescStr(field);
      var type = TypeStr(field.FieldType, namespaced);

      return $"{name}{desc}: {type}";
    }

    public string MemberStr(MemberInfo m, bool namespaced = false, string indent = "") {
      switch (m.MemberType) {
      case MemberTypes.NestedType:
        return NestedTypeStr((Type) m, namespaced, indent);
      case MemberTypes.Constructor:
        return ConstructorStr((ConstructorInfo) m, namespaced, indent);
      case MemberTypes.Method:
        return MethodStr((MethodInfo) m, namespaced, indent);
      case MemberTypes.Event:
        return EventStr((EventInfo) m, namespaced, indent);
      case MemberTypes.Property:
        return PropertyStr((PropertyInfo) m, namespaced, indent);
      case MemberTypes.Field:
        return FieldStr((FieldInfo) m, namespaced, indent);
      default:
        return $"{m}";
      }
    }

    public IEnumerable<MemberInfo> StaticMembers(Type t) {
      var flags = BindingFlags.DeclaredOnly |
        BindingFlags.Static |
        BindingFlags.NonPublic |
        BindingFlags.Public;

      return t.GetMembers(flags).
        Where(m => !(
          m.MemberType == MemberTypes.NestedType &&
          m.IsDefined(typeof (CompilerGeneratedAttribute), false)
        )).
        OrderBy(m => m.Name);
    }

    public string StaticMembersStr(Type t, bool namespaced = false, string indent = "") {
      var members = StaticMembers(t).
        Select(m => MemberStr(m, namespaced, indent));

      return $"{indent}{String.Join($"\n{indent}", members)}";
    }

    public IEnumerable<MethodInfo> ExtensionMethods(Type tExtension) {
      return StaticMembers(tExtension).
        Where(m =>
          m.MemberType == MemberTypes.Method &&
          m.IsDefined(typeof (ExtensionAttribute), false) &&
          ((MethodInfo) m).GetParameters().Length > 0
        ).
        Select(m => (MethodInfo) m);
    }

    public IEnumerable<MethodInfo> ExtensionMethods(Type tExtension, Type tTarget) {
      return ExtensionMethods(tExtension).
        Where(meth => {
          var tExtMethThis = meth.GetParameters()[0].ParameterType;

          return CanExtend(tExtMethThis, tTarget);
        });
    }

    public bool CanExtend(Type tExtMethThis, Type tTarget) {
      if (tExtMethThis.IsAssignableFrom(tTarget)) {
        return true;
      } else if (
        (tExtMethThis.IsGenericType && tExtMethThis.ContainsGenericParameters) &&
        tTarget.IsGenericType
      ) {
        var tGenExtMethThis = tExtMethThis.GetGenericTypeDefinition();
        var tGenTarget = tTarget.GetGenericTypeDefinition();

        if (
          tGenExtMethThis == tGenTarget ||
          tGenTarget.GetInterfaces().
            Any(i =>
              i.IsGenericType &&
              tGenExtMethThis == i.GetGenericTypeDefinition()
            )
        ) {
          return true;
        } else {
          var b = tGenTarget;

          while ((b = b.BaseType) != null) {
            if (
              b.IsGenericType &&
              tGenExtMethThis == b.GetGenericTypeDefinition()
            ) {
              return true;
            }
          }
        }
      }

      return false;
    }

    public IEnumerable<MemberInfo> Members(Type t) {
      var flags = BindingFlags.DeclaredOnly |
        BindingFlags.Instance |
        BindingFlags.NonPublic |
        BindingFlags.Public;

      return t.GetMembers(flags).
        Where(m => !(
          m.MemberType == MemberTypes.NestedType &&
          m.IsDefined(typeof (CompilerGeneratedAttribute), false)
        )).
        OrderBy(m => m.Name);
    }

    public string MembersStr(Type t, bool namespaced = false, string indent = "") {
      var members = Members(t).
        Select(m => MemberStr(m, namespaced, indent));

      return $"{indent}{String.Join($"\n{indent}", members)}";
    }

    public IEnumerable<Type> Nesting(Type t) {
      var types = new List<Type>();

      while (t.IsNested && (t = t.DeclaringType) != null) {
        types.Insert(0, t);
      }

      return types;
    }

    public string NestingStr(Type t, bool namespaced = false) {
      var nesting = Nesting(t).
        Select(_t => TypeStr(_t, namespaced));

      return (nesting.Any()) ?
        $"{String.Join("+", nesting)}+" :
        "";
    }

    public IEnumerable<Type> Bases(Type t) {
      var types = new List<Type>();

      while ((t = t.BaseType) != null) {
        types.Insert(0, t);
      }

      return types;
    }

    public IEnumerable<Type> Interfaces(Type t) {
      return t.GetInterfaces().
        OrderBy(i => i.Namespace).
        ThenBy(i => i.FullName);
    }

    public IEnumerable<Type> Extensions(Type tTarget) {
      return AppDomainTypes.
        Where(tExtension =>
          IsExtensionClass(tExtension) &&
          ExtensionMethods(tExtension).
            Any(meth => {
              var tExtMethThis = meth.GetParameters()[0].ParameterType;

              return CanExtend(tExtMethThis, tTarget);
            })
        ).
        OrderBy(tExtension => tExtension.Namespace).
        ThenBy(tExtension => tExtension.FullName);
    }

    public string ChainStr(Type t, bool namespaced = false, string indent = "") {
      void AppendType(Type _t, bool _namespaced, string _indent, string _ws, StringBuilder _sb) {
        var kind = KindStr(_t);
        var nest = NestingStr(_t, _namespaced);
        var type = TypeStr(_t, _namespaced);

        if (_sb.Length > 0) {
          _sb.Append("\n");
        }
        _sb.Append($"{_indent}{_ws}[{kind} {nest}{type}]");
      }

      var bases = (!t.IsInterface) ? Bases(t) : Interfaces(t);
      var sb = new StringBuilder();
      var ws = "";

      foreach (var b in bases) {
        AppendType(b, namespaced, indent, ws, sb);
        ws += "  ";
      }

      if (!t.IsInterface) {
        var ifaces = Interfaces(t);

        foreach (var i in ifaces) {
          AppendType(i, namespaced, indent, ws, sb);
        }
        if (ifaces.Any()) {
          ws += "  ";
        }
      }

      AppendType(t, namespaced, indent, ws, sb);
      ws += "  ";

      foreach (var e in Extensions(t)) {
        var kind = "extension";
        var type = TypeStr(e, true);
        var assembly = Path.GetFileName(e.Assembly.Location);

        if (sb.Length > 0) {
          sb.Append("\n");
        }
        sb.Append($"{indent}{ws}[{kind} {type}] ({assembly})");
      }

      return sb.ToString();
    }

    public string StaticApiStr(Type t, bool namespaced = false, string indent = "") {
      void AppendApi(Type _t, bool _namespaced, string _indent, string _ws, StringBuilder _sb) {
        var kind = KindStr(_t);
        var nest = NestingStr(_t, _namespaced);
        var type = TypeStr(_t, _namespaced);
        var members = StaticMembersStr(_t, _namespaced, $"{_indent}{_ws}  ");

        if (_sb.Length > 0) {
          _sb.Append("\n");
        }
        _sb.Append($"{_indent}{_ws}[{kind} {nest}{type} (static API)]\n{members}");
      }

      var bases = (!t.IsInterface) ? Bases(t) : Interfaces(t);
      var sb = new StringBuilder();
      var ws = "";

      foreach (var b in bases) {
        AppendApi(b, namespaced, indent, ws, sb);
        ws += "  ";
      }

      AppendApi(t, namespaced, indent, ws, sb);

      return sb.ToString();
    }

    public string ApiStr(Type t, bool namespaced = false, string indent = "") {
      void AppendApi(Type _t, bool _namespaced, string _indent, string _ws, StringBuilder _sb) {
        var kind = KindStr(_t);
        var nest = NestingStr(_t, _namespaced);
        var type = TypeStr(_t, _namespaced);
        var members = MembersStr(_t, _namespaced, $"{_indent}{_ws}  ");
        var note = (_t.IsInterface) ? "" : " (instance API)";

        if (_sb.Length > 0) {
          _sb.Append("\n");
        }
        _sb.Append($"{_indent}{_ws}[{kind} {nest}{type}{note}]\n{members}");
      }

      var bases = (!t.IsInterface) ? Bases(t) : Interfaces(t);
      var sb = new StringBuilder();
      var ws = "";

      foreach (var b in bases) {
        AppendApi(b, namespaced, indent, ws, sb);
        ws += "  ";
      }

      if (!t.IsInterface) {
        var ifaces = Interfaces(t);

        foreach (var i in ifaces) {
          AppendApi(i, namespaced, indent, ws, sb);
        }
        if (ifaces.Any()) {
          ws += "  ";
        }
      }

      AppendApi(t, namespaced, indent, ws, sb);
      ws += "  ";

      foreach (var e in Extensions(t)) {
        var kind = "extension";
        var type = TypeStr(e, true);
        var assembly = Path.GetFileName(e.Assembly.Location);
        var methods = ExtensionMethods(e, t).
          Select(meth => $"{indent}{ws}  {ExtensionMethodStr(meth)}");

        if (sb.Length > 0) {
          sb.Append("\n");
        }
        sb.Append($"{indent}{ws}[{kind} {type}] ({assembly})\n{String.Join("\n", methods)}");
      }

      return sb.ToString();
    }
  }
}
