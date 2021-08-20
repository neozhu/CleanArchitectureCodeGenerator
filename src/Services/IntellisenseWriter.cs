using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;



namespace CleanArchitecture.CodeGenerator.Services
{
    internal static class IntellisenseWriter
    {
        private static readonly Regex _whitespaceTrimmer = new Regex(@"^\s+|\s+$|\s*[\r\n]+\s*", RegexOptions.Compiled);

        public static string WriteTypeScript(IEnumerable<IntellisenseObject> objects)
        {
            var sb = new StringBuilder();

            foreach (IGrouping<string, IntellisenseObject> ns in objects.GroupBy(o => o.Namespace))
            {
                
                    sb.AppendFormat("declare module {0} {{\r\n", ns.Key);
               

                foreach (IntellisenseObject io in ns)
                {
                    if (!string.IsNullOrEmpty(io.Summary))
                    {
                        sb.AppendLine("\t/** " + _whitespaceTrimmer.Replace(io.Summary, "") + " */");
                    }

                    if (io.IsEnum)
                    {
                       
                            sb.AppendLine("\tconst enum " + Utility.CamelCaseClassName(io.Name) + " {");

                            foreach (IntellisenseProperty p in io.Properties)
                            {
                                WriteTypeScriptComment(p, sb);

                                if (p.InitExpression != null)
                                {
                                    sb.AppendLine("\t\t" + Utility.CamelCaseEnumValue(p.Name) + " = " + CleanEnumInitValue(p.InitExpression) + ",");
                                }
                                else
                                {
                                    sb.AppendLine("\t\t" + Utility.CamelCaseEnumValue(p.Name) + ",");
                                }
                            }

                            sb.AppendLine("\t}");
                        
                    }
                    else
                    {
                        var type =  "\tinterface ";
                        sb.Append(type).Append(Utility.CamelCaseClassName(io.Name)).Append(" ");

                        if (!string.IsNullOrEmpty(io.BaseName))
                        {
                            sb.Append("extends ");

                            if (!string.IsNullOrEmpty(io.BaseNamespace) && io.BaseNamespace != io.Namespace)
                            {
                                sb.Append(io.BaseNamespace).Append(".");
                            }

                            sb.Append(Utility.CamelCaseClassName(io.BaseName)).Append(" ");
                        }

                        WriteTSInterfaceDefinition(sb, "\t", io.Properties);
                        sb.AppendLine();
                    }
                }

               
                    sb.AppendLine("}");
               
            }

            return sb.ToString();
        }

        private static string CleanEnumInitValue(string value)
        {
            value = value.TrimEnd('u', 'U', 'l', 'L'); //uint ulong long
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var trimedValue = value.TrimStart('0'); // prevent numbers to be parsed as octal in js.
            if (trimedValue.Length > 0)
            {
                return trimedValue;
            }

            return "0";
        }


        private static void WriteTypeScriptComment(IntellisenseProperty p, StringBuilder sb)
        {
            if (string.IsNullOrEmpty(p.Summary))
            {
                return;
            }

            sb.AppendLine("\t\t/** " + _whitespaceTrimmer.Replace(p.Summary, "") + " */");
        }

        private static void WriteTSInterfaceDefinition(StringBuilder sb, string prefix,
            IEnumerable<IntellisenseProperty> props)
        {
            sb.AppendLine("{");

            foreach (IntellisenseProperty p in props)
            {
                WriteTypeScriptComment(p, sb);
                sb.AppendFormat("{0}\t{1}: ", prefix, Utility.CamelCasePropertyName(p.NameWithOption));

                if (p.Type.IsKnownType)
                {
                    sb.Append(p.Type.TypeScriptName);
                }
                else
                {
                    if (p.Type.Shape == null)
                    {
                        sb.Append("any");
                    }
                    else
                    {
                        WriteTSInterfaceDefinition(sb, prefix + "\t", p.Type.Shape);
                    }
                }
                if (p.Type.IsArray)
                {
                    sb.Append("[]");
                }

                sb.AppendLine(";");
            }

            sb.Append(prefix).Append("}");
        }
    }
}
