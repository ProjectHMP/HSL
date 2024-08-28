using System;
using System.Xml;
using System.IO;

namespace HSL.Core
{
    internal class ServerSettings
    {

        private XmlDocument Document;

        private string _file;
        private object _saveLock = new object();

        public ServerSettings(string settingsFile) {
            _file = settingsFile;
            Document = new XmlDocument();
            RefreshDocument();
        }

        public void RefreshDocument()
        {
            if (File.Exists(_file))
            {
                Document.Load(_file);
            }
        }

        public XmlNodeList GetNodes(string name) => Document.DocumentElement.SelectNodes("resource");

        public T Get<T>(string name) => Get<T>(name, default(T));

        public T Get<T>(string name, T defaultValue) 
        {

            if(Document == null)
            {
                return defaultValue;
            }

            XmlNode node = Document.DocumentElement.SelectSingleNode(name);

            if(node == null || string.IsNullOrEmpty(node.InnerText))
            {
                return defaultValue;
            }

            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.UInt16:
                    if(UInt16.TryParse(node.InnerText, out UInt16 uint16))
                    {
                        return (T)((object)uint16);
                    }
                    break;
                case TypeCode.UInt32:
                    if(UInt32.TryParse(node.InnerText, out UInt32 uint32))
                    {
                        return (T)((object)uint32);
                    }
                    break;
                case TypeCode.UInt64:
                    if(UInt64.TryParse(node.InnerText, out UInt64 uint64))
                    {
                        return (T)((object)uint64);
                    }
                    break;
                case TypeCode.Int16:
                    if(Int16.TryParse(node.InnerText, out Int16 int16))
                    {
                        return (T)((object)int16);
                    }
                    break;
                case TypeCode.Int32:
                    if(Int32.TryParse(node.InnerText, out Int32 int32))
                    {
                        return (T)((object)int32);
                    }
                    break;
                case TypeCode.Int64:
                    if(Int64.TryParse(node.InnerText, out Int64 int64))
                    {
                        return (T)((object)int64);
                    }
                    break;
                case TypeCode.Single:
                    if(Single.TryParse(node.InnerText, out Single single))
                    {
                        return (T)((object)single);
                    }
                    break;
                case TypeCode.Double:
                    if(Double.TryParse(node.InnerText, out Double doub))
                    {
                        return (T)((object)doub);
                    }
                    break;
                case TypeCode.Boolean:
                    if(bool.TryParse(node.InnerText, out bool b))
                    {
                        return (T)((object)b);
                    }
                    break;
                case TypeCode.String:
                    return (T) ((object)node.InnerText);
            }
            return default(T);
        }

        public void Set<T>(string name, T value)
        {

            if(Document == null)
            {
                return;
            }

            XmlNode node = Document.DocumentElement.SelectSingleNode(name);
            if(node == null)
            {
                node = Document.CreateNode("element", name, "");
                Document.DocumentElement.AppendChild(node);
            }

            switch(Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Single:
                case TypeCode.Double:
                    node.InnerText = value.ToString();
                    break;
                case TypeCode.Boolean:
                    node.InnerText = value.ToString().ToLower();
                    break;
                case TypeCode.String:
                    node.InnerText = value.ToString();
                    break;
            }

            lock(_saveLock)
            {
                Document.Save(_file);
            }
        }
    }
}
