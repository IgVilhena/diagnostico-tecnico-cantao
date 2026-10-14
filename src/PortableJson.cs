using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Diagnostico
{
    // Usa o leitor/escritor JSON do .NET cliente, sem depender de ASP.NET/System.Web.
    // Mantém dicionários e ArrayList das OS antigas e os valores padrão dos construtores.
    sealed class PortableJsonSerializer
    {
        public string Serialize(object value)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false))
                {
                    writer.WriteStartElement("root");
                    WriteValue(writer, value, 0);
                    writer.WriteEndElement();
                    writer.Flush();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public T Deserialize<T>(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = int.MaxValue, MaxArrayLength = int.MaxValue };
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas))
            {
                var document = new XmlDocument { XmlResolver = null };
                document.Load(reader);
                return (T)ConvertValue(ReadValue(document.DocumentElement), typeof(T));
            }
        }

        static object ReadValue(XmlElement element)
        {
            switch (element.GetAttribute("type"))
            {
                case "null": return null;
                case "boolean": return XmlConvert.ToBoolean(element.InnerText);
                case "number":
                    int i; long l; decimal d;
                    string n = element.InnerText;
                    if (int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
                    if (long.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                    if (decimal.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                    return double.Parse(n, CultureInfo.InvariantCulture);
                case "array":
                    var array = new ArrayList();
                    foreach (XmlElement child in element.ChildNodes) array.Add(ReadValue(child));
                    return array;
                case "object":
                    var map = new Dictionary<string, object>();
                    foreach (XmlElement child in element.ChildNodes)
                        map[child.HasAttribute("item") ? child.GetAttribute("item") : child.LocalName] = ReadValue(child);
                    return map;
                default: return element.InnerText;
            }
        }

        static object ConvertValue(object value, Type type)
        {
            if (value == null) return null;
            if (type.IsInstanceOfType(value)) return value;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type);
                foreach (object item in (IEnumerable)value) list.Add(ConvertValue(item, type.GetGenericArguments()[0]));
                return list;
            }
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                object result = Activator.CreateInstance(type);
                foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    object item;
                    if (Persist(property) && map.TryGetValue(property.Name, out item))
                        property.SetValue(result, ConvertValue(item, property.PropertyType), null);
                }
                return result;
            }
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        static bool Persist(PropertyInfo property)
        {
            return property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                && !Attribute.IsDefined(property, typeof(IgnoreDataMemberAttribute));
        }

        static void Member(XmlDictionaryWriter writer, string name, object value, int depth)
        {
            // O mapeamento "item" permite chaves com espaços, acentos e pontuação.
            writer.WriteStartElement("a", "item", "item");
            writer.WriteAttributeString("item", name);
            WriteValue(writer, value, depth + 1);
            writer.WriteEndElement();
        }

        static void WriteValue(XmlDictionaryWriter writer, object value, int depth)
        {
            if (depth > 64) throw new SerializationException("JSON excedeu 64 níveis.");
            if (value == null) { writer.WriteAttributeString("type", "null"); return; }
            if (value is string || value is char)
            {
                writer.WriteAttributeString("type", "string"); writer.WriteString(value.ToString()); return;
            }
            if (value is bool)
            {
                writer.WriteAttributeString("type", "boolean"); writer.WriteString((bool)value ? "true" : "false"); return;
            }
            var dict = value as IDictionary;
            if (dict != null)
            {
                writer.WriteAttributeString("type", "object");
                foreach (DictionaryEntry entry in dict) Member(writer, (string)entry.Key, entry.Value, depth);
                return;
            }
            var items = value as IEnumerable;
            if (items != null)
            {
                writer.WriteAttributeString("type", "array");
                foreach (object item in items)
                {
                    writer.WriteStartElement("item"); WriteValue(writer, item, depth + 1); writer.WriteEndElement();
                }
                return;
            }
            if (value.GetType().IsPrimitive || value is decimal)
            {
                writer.WriteAttributeString("type", "number"); writer.WriteString(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
            writer.WriteAttributeString("type", "object");
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (Persist(property)) Member(writer, property.Name, property.GetValue(value, null), depth);
        }
    }
}
