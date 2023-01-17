﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Services;
using EnvDTE;
using EnvDTE80;


namespace CleanArchitecture.CodeGenerator.Models
{
    public static class IntellisenseParser
    {
        private static readonly string DefaultModuleName = "";
        private const string ModuleNameAttributeName = "TypeScriptModule";
        private static readonly Regex IsNumber = new Regex("^[0-9a-fx]+[ul]{0,2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static Project _project;

        //internal static class Ext
        //{
        //    public const string TypeScript = ".d.ts";
        //}

        internal static IEnumerable<IntellisenseObject> ProcessFile(ProjectItem item, HashSet<CodeClass> underProcess = null)
        {
            if (item.FileCodeModel == null || item.ContainingProject == null)
            {
                return null;
            }

            _project = item.ContainingProject;

            var list = new List<IntellisenseObject>();

            if (underProcess == null)
            {
                underProcess = new HashSet<CodeClass>();
            }

            foreach (CodeElement element in item.FileCodeModel.CodeElements)
            {
                if (element.Kind == vsCMElement.vsCMElementNamespace)
                {
                    var cn = (CodeNamespace)element;

                    foreach (CodeElement member in cn.Members)
                    {
                        if (ShouldProcess(member))
                        {
                            ProcessElement(member, list, underProcess);
                        }
                    }
                }
                else if (ShouldProcess(element))
                {
                    ProcessElement(element, list, underProcess);
                }
            }

            return new HashSet<IntellisenseObject>(list);
        }
        private static void ProcessElement(CodeElement element, List<IntellisenseObject> list, HashSet<CodeClass> underProcess)
        {
            if (element.Kind == vsCMElement.vsCMElementEnum)
            {
                ProcessEnum((CodeEnum)element, list);
            }
            else if (element.Kind == vsCMElement.vsCMElementClass)
            {
                var cc = (CodeClass)element;

                // Don't re-generate the intellisense.
                if (list.Any(x => x.Name == GetClassName(cc) && x.Namespace == GetNamespace(cc)))
                {
                    return;
                }

                // Collect inherit classes.
                CodeClass baseClass = null;

                try
                {
                    // To recuse from throwing from a weird case
                    // where user inherit class from struct and save. As such inheritance is disallowed.
                    baseClass = cc.Bases.Cast<CodeClass>()
                                  .FirstOrDefault(c => c.FullName != "System.Object");
                }
                catch { /* Silently continue. */ }
				var baseClasses = new string[] { "AuditableSoftDeleteEntity", "AuditableEntity", "IAuditTrial", "IEntity", "ISoftDelete", "BaseAuditableEntity", "BaseEntity" };
				if (baseClass!=null && baseClasses.Contains(GetClassName(baseClass)))
				{
					ProcessClass(cc, baseClass, list, underProcess);
				}
                var references = new HashSet<string>();
                try
                {
                    // Process Inheritence.
                    if (baseClass != null && !underProcess.Contains(baseClass) && !HasIntellisense(baseClass.ProjectItem, references))
                    {
                        list.Last().UpdateReferences(references);
                        underProcess.Add(baseClass);
                        list.AddRange(ProcessFile(baseClass.ProjectItem, underProcess));
                    }
                }
                catch
                {

                }
            }
        }

        private static bool ShouldProcess(CodeElement member)
        {
            return
                    member.Kind == vsCMElement.vsCMElementClass
                    || member.Kind == vsCMElement.vsCMElementEnum;
        }

        private static void ProcessEnum(CodeEnum element, List<IntellisenseObject> list)
        {
            var data = new IntellisenseObject
            {
                Name = element.Name,
                IsEnum = element.Kind == vsCMElement.vsCMElementEnum,
                FullName = element.FullName,
                Namespace = GetNamespace(element),
                Summary = GetSummary(element)
            };

            foreach (CodeVariable codeEnum in element.Members.OfType<CodeVariable>())
            {
                var prop = new IntellisenseProperty
                {
                    Name = codeEnum.Name,
                    Summary = GetSummary(codeEnum),
                    InitExpression = GetInitializer(codeEnum.InitExpression)
                };

                data.Properties.Add(prop);
            }

            if (data.Properties.Count > 0)
            {
                list.Add(data);
            }
        }

        private static void ProcessClass(CodeClass cc, CodeClass baseClass, List<IntellisenseObject> list, HashSet<CodeClass> underProcess)
        {
            string baseNs = null;
            string baseClassName = null;
            var ns = GetNamespace(cc);
            var className = GetClassName(cc);
            var references = new HashSet<string>();
            IList<IntellisenseProperty> properties = GetProperties(cc.Members, new HashSet<string>(), references).ToList();

            foreach (CodeElement member in cc.Members)
            {
                if (ShouldProcess(member))
                {
                    ProcessElement(member, list, underProcess);
                }
            }

            if (baseClass != null)
            {
                baseClassName = GetClassName(baseClass);
                baseNs = GetNamespace(baseClass);
            }

            var intellisenseObject = new IntellisenseObject(properties.ToList(), references)
            {
                Namespace = ns,
                Name = className,
                BaseNamespace = baseNs,
                BaseName = baseClassName,
                FullName = cc.FullName,
                Summary = GetSummary(cc)
            };

            list.Add(intellisenseObject);
        }

        private static IEnumerable<IntellisenseProperty> GetProperties(CodeElements props, HashSet<string> traversedTypes, HashSet<string> references = null)
        {
			Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
			return from p in props.OfType<CodeProperty>()
                   where !p.Attributes.Cast<CodeAttribute>().Any(HasIgnoreAttribute)
                   where vsCMAccess.vsCMAccessPublic == p.Access && p.Getter != null && !p.Getter.IsShared && IsPublic(p.Getter)
                   select new IntellisenseProperty
                   {
                       Name = GetName(p),
                       Type = GetType(p.Parent, p.Type, traversedTypes, references),
                       Summary = GetSummary(p)
                   };
        }

        private static bool HasIgnoreAttribute(CodeAttribute attribute)
        {
            return attribute.FullName == "System.Runtime.Serialization.IgnoreDataMemberAttribute" ||
                   attribute.FullName == "Newtonsoft.Json.JsonIgnoreAttribute" ||
                   attribute.FullName == "System.Web.Script.Serialization.ScriptIgnoreAttribute";
        }

        private static bool IsPublic(CodeFunction cf)
        {
            vsCMElement fun = cf.Kind;

            var retVal = false;
            try
            {
                retVal = cf.Access == vsCMAccess.vsCMAccessPublic;
            }
            catch (COMException)
            {
                var cp = cf.Parent as CodeProperty;
                if (cp != null)
                {
                    retVal = cp.Access == vsCMAccess.vsCMAccessPublic;
                }

            }
            return retVal;
        }

        private static string GetClassName(CodeClass cc)
        {
            return GetDataContractName(cc, "Name") ?? cc.Name;
        }

        private static string GetNamespace(CodeClass cc)
        {
            return GetDataContractName(cc, "Namespace") ?? GetNamespace(cc.Attributes);
        }

        private static string GetDataContractName(CodeClass cc, string attrName)
        {
            IEnumerable<CodeAttribute> dataContractAttribute = cc.Attributes.Cast<CodeAttribute>().Where(a => a.Name == "DataContract");
            return GetDataContractNameInner(dataContractAttribute, attrName);
        }

        private static string GetNamespace(CodeEnum cc)
        {
            return GetDataContractName(cc, "Namespace") ?? GetNamespace(cc.Attributes);
        }
        private static string GetDataContractName(CodeEnum cc, string attrName)
        {
            IEnumerable<CodeAttribute> dataContractAttribute = cc.Attributes.Cast<CodeAttribute>().Where(a => a.Name == "DataContract");
            return GetDataContractNameInner(dataContractAttribute, attrName);
        }
        private static string GetDataContractNameInner(IEnumerable<CodeAttribute> dataContractAttribute, string attrName)
        {
            if (!dataContractAttribute.Any())
            {
                return null;
            }

            string name = null;
            var keyValues = dataContractAttribute.First().Children.OfType<CodeAttributeArgument>()
                           .ToDictionary(a => a.Name, a => (a.Value ?? "").Trim('\"', '\''));

            if (keyValues.ContainsKey(attrName))
            {
                name = keyValues[attrName];
            }

            return name;
        }

        private static string GetNamespace(CodeElements attrs)
        {
            if (attrs == null)
            {
                return DefaultModuleName;
            }

            IEnumerable<string> namespaceFromAttr = from a in attrs.Cast<CodeAttribute2>()
                                                    where a.Name.EndsWith(ModuleNameAttributeName, StringComparison.OrdinalIgnoreCase)
                                                    from arg in a.Arguments.Cast<CodeAttributeArgument>()
                                                    let v = (arg.Value ?? "").Trim('\"')
                                                    where !string.IsNullOrWhiteSpace(v)
                                                    select v;

            return namespaceFromAttr.FirstOrDefault() ?? DefaultModuleName;
        }

        private static IntellisenseType GetType(CodeClass rootElement, CodeTypeRef codeTypeRef, HashSet<string> traversedTypes, HashSet<string> references)
        {
            var isArray = codeTypeRef.TypeKind == vsCMTypeRef.vsCMTypeRefArray;
            var isCollection = codeTypeRef.AsString.StartsWith("System.Collections", StringComparison.Ordinal);
            var isDictionary = false;

            CodeTypeRef effectiveTypeRef = codeTypeRef;
            if (isArray && codeTypeRef.ElementType != null)
            {
                effectiveTypeRef = effectiveTypeRef.ElementType;
            }
            else if (isCollection)
            {
                effectiveTypeRef = TryToGuessGenericArgument(rootElement, effectiveTypeRef);
            }

            if (isCollection)
            {
                isDictionary = codeTypeRef.AsString.StartsWith("System.Collections.Generic.Dictionary", StringComparison.Ordinal)
                            || codeTypeRef.AsString.StartsWith("System.Collections.Generic.IDictionary", StringComparison.Ordinal);
            }

            var typeName = effectiveTypeRef.AsFullName;

            try
            {

                var codeClass = effectiveTypeRef.CodeType as CodeClass2;
                var codeEnum = effectiveTypeRef.CodeType as CodeEnum;
                var isPrimitive = IsPrimitive(effectiveTypeRef);

                var result = new IntellisenseType
                {
                    IsArray = !isDictionary && (isArray || isCollection),
                    IsDictionary = isDictionary,
                    CodeName = effectiveTypeRef.AsString,
                    ClientSideReferenceName =
                        effectiveTypeRef.TypeKind == vsCMTypeRef.vsCMTypeRefCodeType &&
                        effectiveTypeRef.CodeType.InfoLocation == vsCMInfoLocation.vsCMInfoLocationProject
                        ?
                            (codeClass != null && HasIntellisense(codeClass.ProjectItem, references) ? (GetNamespace(codeClass) + "." + Utility.CamelCaseClassName(GetClassName(codeClass))) : null) ??
                            (codeEnum != null && HasIntellisense(codeEnum.ProjectItem, references) ? (GetNamespace(codeEnum) + "." + Utility.CamelCaseClassName(codeEnum.Name)) : null)
                        : null
                };

                if (!isPrimitive && codeClass != null && !traversedTypes.Contains(effectiveTypeRef.CodeType.FullName) && !isCollection)
                {
                    traversedTypes.Add(effectiveTypeRef.CodeType.FullName);
                    result.Shape = GetProperties(effectiveTypeRef.CodeType.Members, traversedTypes, references).ToList();
                    traversedTypes.Remove(effectiveTypeRef.CodeType.FullName);
                }

                return result;
            }
            catch (InvalidCastException)
            {
                VSHelpers.WriteOnOutputWindow(string.Format("ERROR - Cannot find definition for {0}", typeName));
                throw new ArgumentException(string.Format("Cannot find definition of {0}", typeName));
            }
        }

        private static CodeTypeRef TryToGuessGenericArgument(CodeClass rootElement, CodeTypeRef codeTypeRef)
        {
			Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
			var codeTypeRef2 = codeTypeRef as CodeTypeRef2;
            if (codeTypeRef2 == null || !codeTypeRef2.IsGeneric)
            {
                return codeTypeRef;
            }

            // There is no way to extract generic parameter as CodeTypeRef or something similar
            // (see http://social.msdn.microsoft.com/Forums/vstudio/en-US/09504bdc-2b81-405a-a2f7-158fb721ee90/envdte-envdte80-codetyperef2-and-generic-types?forum=vsx)
            // but we can make it work at least for some simple case with the following heuristic:
            //  1) get the argument's local name by parsing the type reference's full text
            //  2) if it's a known primitive (i.e. string, int, etc.), return that
            //  3) otherwise, guess that it's a type from the same namespace and same project,
            //     and use the project CodeModel to retrieve it by full name
            //  4) if CodeModel returns null - well, bad luck, don't have any more guesses
            var typeNameAsInCode = codeTypeRef2.AsString.Split('<', '>').ElementAtOrDefault(1) ?? "";
            CodeModel projCodeModel;

            try
            {
                projCodeModel = rootElement.ProjectItem.ContainingProject.CodeModel;
            }
            catch (COMException)
            {
                projCodeModel = _project.CodeModel;
            }

            CodeType codeType = projCodeModel.CodeTypeFromFullName(TryToGuessFullName(typeNameAsInCode));

            if (codeType != null)
            {
                return projCodeModel.CreateCodeTypeRef(codeType);
            }

            return codeTypeRef;
        }

        private static readonly Dictionary<string, Type> _knownPrimitiveTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase) {
            { "string", typeof( string ) },
            { "int", typeof( int ) },
            { "long", typeof( long ) },
            { "short", typeof( short ) },
            { "byte", typeof( byte ) },
            { "uint", typeof( uint ) },
            { "ulong", typeof( ulong ) },
            { "ushort", typeof( ushort ) },
            { "sbyte", typeof( sbyte ) },
            { "float", typeof( float ) },
            { "double", typeof( double ) },
            { "decimal", typeof( decimal ) },
        };

        private static string TryToGuessFullName(string typeName)
        {
            if (_knownPrimitiveTypes.TryGetValue(typeName, out Type primitiveType))
            {
                return primitiveType.FullName;
            }

            return typeName;
        }

        private static bool IsPrimitive(CodeTypeRef codeTypeRef)
        {
            if (codeTypeRef.TypeKind != vsCMTypeRef.vsCMTypeRefOther && codeTypeRef.TypeKind != vsCMTypeRef.vsCMTypeRefCodeType)
            {
                return true;
            }

            if (codeTypeRef.AsString.EndsWith("DateTime", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool HasIntellisense(ProjectItem projectItem, HashSet<string> references)
        {
            for (short i = 0; i < projectItem.FileCount; i++)
            {
                var fileName = GenerationService.GenerateFileName(projectItem.FileNames[i]);

                if (File.Exists(fileName))
                {
                    references.Add(fileName);
                    return true;
                }
            }

            return false;
        }

        // Maps attribute name to array of attribute properties to get resultant name from
        private static readonly IReadOnlyDictionary<string, string[]> nameAttributes = new Dictionary<string, string[]>
        {
            { "DataMember", new [] { "Name" } },
            { "JsonProperty", new [] { "", "PropertyName" } }
        };

        private static string GetName(CodeProperty property)
        {
            foreach (CodeAttribute attr in property.Attributes)
            {
                var className = Path.GetExtension(attr.Name);

                if (string.IsNullOrEmpty(className))
                {
                    className = attr.Name;
                }

                if (!nameAttributes.TryGetValue(className, out var argumentNames))
                {
                    continue;
                }

                CodeAttributeArgument value = attr.Children.OfType<CodeAttributeArgument>().FirstOrDefault(a => argumentNames.Contains(a.Name));

                if (value == null)
                {
                    break;
                }

                // Strip the leading & trailing quotes
                return value.Value.Trim('@', '\'', '"');
            }

            return property.Name.Trim('@');
        }

        // External items throw an exception from the DocComment getter
        private static string GetSummary(CodeProperty property) { return property.InfoLocation != vsCMInfoLocation.vsCMInfoLocationProject ? null : GetSummary(property.InfoLocation, property.DocComment, property.Comment, property.FullName); }

        private static string GetSummary(CodeClass property) { return GetSummary(property.InfoLocation, property.DocComment, property.Comment, property.FullName); }

        private static string GetSummary(CodeEnum property) { return GetSummary(property.InfoLocation, property.DocComment, property.Comment, property.FullName); }

        private static string GetSummary(CodeVariable property) { return GetSummary(property.InfoLocation, property.DocComment, property.Comment, property.FullName); }

        private static string GetSummary(vsCMInfoLocation location, string xmlComment, string inlineComment, string fullName)
        {
            if (location != vsCMInfoLocation.vsCMInfoLocationProject || (string.IsNullOrWhiteSpace(xmlComment) && string.IsNullOrWhiteSpace(inlineComment)))
            {
                return null;
            }

            try
            {
                var summary = "";
                if (!string.IsNullOrWhiteSpace(xmlComment))
                {
                    summary = XElement.Parse(xmlComment)
                               .Descendants("summary")
                               .Select(x => x.Value)
                               .FirstOrDefault();
                }
                if (!string.IsNullOrEmpty(summary))
                {
                    return summary.Trim();
                }

                if (!string.IsNullOrWhiteSpace(inlineComment))
                {
                    return inlineComment.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private static string GetInitializer(object initExpression)
        {
            if (initExpression != null)
            {
                var initializer = initExpression.ToString();
                if (IsNumber.IsMatch(initializer))
                {
                    return initializer;
                }
            }
            return null;
        }
    }
}
