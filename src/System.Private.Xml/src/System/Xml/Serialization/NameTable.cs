// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if XMLSERIALIZERGENERATOR
namespace Microsoft.XmlSerializer.Generator
#else
namespace System.Xml.Serialization
#endif
{
    using System.Xml;
    using System;
    using System.Collections;
    using System.Collections.Generic;

    internal class NameKey
    {
        private string _ns;
        private string _name;
        private string _fullName;

        internal NameKey(string name, string ns, string fullName)
        {
            _name = name;
            _ns = ns;
            _fullName = fullName;
        }

        public override bool Equals(object other)
        {
            if (!(other is NameKey)) return false;
            NameKey key = (NameKey)other;
            return _name == key._name && _ns == key._ns && _fullName == key._fullName;
        }

        public override int GetHashCode()
        {
            return (_ns == null ? "<null>".GetHashCode() : _ns.GetHashCode()) ^ (_name == null ? 0 : _name.GetHashCode() ^ (_fullName == null ? 0 : _fullName.GetHashCode()));
        }
    }
    internal interface INameScope
    {
        object this[string name, string ns, string fullName] { get; set; }
    }
    internal class NameTable : INameScope
    {
        private readonly Dictionary<NameKey, object> _table = new Dictionary<NameKey, object>();

        internal void Add(XmlQualifiedName qname, object value)
        {
            Add(qname.Name, qname.Namespace, qname.Name, value);
        }

        internal void Add(string name, string ns, string fullName, object value)
        {
            NameKey key = new NameKey(name, ns, fullName);
            _table.Add(key, value);
        }

        internal object this[XmlQualifiedName qname]
        {
            get
            {
                object obj;
                return _table.TryGetValue(new NameKey(qname.Name, qname.Namespace, qname.Name), out obj) ? obj : null;
            }
            set
            {
                _table[new NameKey(qname.Name, qname.Namespace, qname.Name)] = value;
            }
        }
        internal object this[string name, string ns, string fullName]
        {
            get
            {
                object obj;
                return _table.TryGetValue(new NameKey(name, ns, fullName), out obj) ? obj : null;
            }
            set
            {
                _table[new NameKey(name, ns, fullName)] = value;
            }
        }
        object INameScope.this[string name, string ns, string fullName]
        {
            get
            {
                object obj;
                _table.TryGetValue(new NameKey(name, ns, fullName), out obj);
                return obj;
            }
            set
            {
                _table[new NameKey(name, ns, fullName)] = value;
            }
        }

        internal ICollection Values
        {
            get { return _table.Values; }
        }

        internal Array ToArray(Type type)
        {
            Array a = Array.CreateInstance(type, _table.Count);
            ((ICollection)_table.Values).CopyTo(a, 0);
            return a;
        }
    }
}

