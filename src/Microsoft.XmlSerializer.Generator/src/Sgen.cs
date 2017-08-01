﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Microsoft.XmlSerializer.Generator
{
    public class Sgen
    {
        public static int Main(string[] args)
        {
            Sgen sgen = new Sgen();
            return sgen.Run(args);
        }

        private int Run(string[] args)
        {
            string assembly = null;
            List<string> types = new List<string>();
            string codePath = null;
            var errs = new ArrayList();
            bool force = false;
            bool proxyOnly = false;
            bool caseSensitive = false;

            try
            {
                if(args.Length >0)
                {
                    if(args.Where(s => s.ToLower().Contains("casesensitive")).Count() > 0)
                    {
                        caseSensitive = true;
                    }
                }

                for (int i = 0; i < args.Length; i++)
                {
                    bool argument = false;
                    string arg = args[i];
                    string value = string.Empty;

                    if (arg.StartsWith("/") || arg.StartsWith("-"))
                    {
                        argument = true;
                        int colonPos = arg.IndexOf(":");
                        if (colonPos != -1)
                        {
                            value = arg.Substring(colonPos + 1).Trim();
                            arg = arg.Substring(0, colonPos).Trim();
                        }
                    }

                    if (ArgumentMatch(arg, "?") || ArgumentMatch(arg, "help"))
                    {
                        WriteHeader();
                        WriteHelp();
                        return 0;
                    }
                    else if (!argument && (arg.EndsWith(".dll") || arg.EndsWith(".exe")))
                    {
                        if (assembly != null)
                        {
                            errs.Add(SR.Format(SR.ErrInvalidArgument, "/assembly", arg));
                        }

                        assembly = arg;
                    }
                    else if (ArgumentMatch(arg, "force"))
                    {
                        force = true;
                    }
                    else if (ArgumentMatch(arg, "proxytypes"))
                    {
                        proxyOnly = true;
                    }
                    else if (ArgumentMatch(arg, "out"))
                    {
                        if (!caseSensitive)
                        {
                            value = value.ToLower(CultureInfo.InvariantCulture);
                        }

                        if (codePath != null)
                        {
                            errs.Add(SR.Format(SR.ErrInvalidArgument, "/out", arg));
                        }

                        codePath = value;
                    }
                    else if (ArgumentMatch(arg, "type"))
                    {
                        types.Add(value);
                    }
                    else if(ArgumentMatch(arg, "casesensitive"))
                    {
                        continue;
                    }
                    else
                    {
                        errs.Add(SR.Format(SR.ErrInvalidArgument, arg));
                        continue;
                    }
                }

                if (errs.Count > 0)
                {
                    foreach (string err in errs)
                    {
                        Console.Error.WriteLine(FormatMessage(true, SR.Format(SR.Warning, err)));
                    }
                }

                if (args.Length == 0 || assembly == null)
                {
                    if (assembly == null)
                    {
                        Console.Error.WriteLine(FormatMessage(false, SR.Format(SR.ErrMissingRequiredArgument, SR.Format(SR.ErrAssembly, "assembly"))));
                    }

                    WriteHelp();
                    return 0;
                }

                GenerateFile(types, assembly, proxyOnly, force, codePath);
            }
            catch (Exception e)
            {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException)
                {
                    throw;
                }

                string errorPrefix = "SGEN: error SGEN1: " + e.Message;
                Error(e, errorPrefix);
                return 1;
            }

            return 0;
        }

        private void GenerateFile(List<string> typeNames, string assemblyName, bool proxyOnly, bool force, string outputDirectory)
        {
            Assembly assembly = LoadAssembly(assemblyName, true);
            Type[] types;

            if (typeNames == null || typeNames.Count == 0)
            {
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException typeException)
                {
                    List<Type> loadedTypes = new List<Type>();
                    foreach (Type type in typeException.Types)
                    {
                        if (type != null)
                        {
                            loadedTypes.Add(type);
                        }
                    }

                    types = loadedTypes.ToArray();
                }
            }
            else
            {
                types = new Type[typeNames.Count];
                int typeIndex = 0;
                foreach (string typeName in typeNames)
                {
                    Type type = assembly.GetType(typeName);
                    if (type == null)
                    {
                        Console.Error.WriteLine(FormatMessage(false, SR.Format(SR.ErrorDetails, SR.Format(SR.ErrLoadType, typeName, assemblyName))));
                    }

                    types[typeIndex++] = type;
                }
            }

            var mappings = new ArrayList();
            var importedTypes = new ArrayList();
            var importer = new XmlReflectionImporter();

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];

                if (type != null)
                {
                    bool isObsolete = false;
                    object[] obsoleteAttributes = type.GetCustomAttributes(typeof(ObsoleteAttribute), false);
                    foreach (object attribute in obsoleteAttributes)
                    {
                        if (((ObsoleteAttribute)attribute).IsError)
                        {
                            isObsolete = true;
                            break;
                        }
                    }

                    if (isObsolete)
                    {
                        continue;
                    }
                }

                if (!proxyOnly)
                {
                    ImportType(type, mappings, importedTypes, importer);
                }
            }

            if (importedTypes.Count > 0)
            {
                var serializableTypes = (Type[])importedTypes.ToArray(typeof(Type));
                var allMappings = (XmlMapping[])mappings.ToArray(typeof(XmlMapping));

                bool gac = assembly.GlobalAssemblyCache;
                outputDirectory = outputDirectory == null ? (gac ? Environment.CurrentDirectory : Path.GetDirectoryName(assembly.Location)) : outputDirectory;
                string serializerName = XmlSerializer.GetXmlSerializerAssemblyName(serializableTypes[0], null);
                string codePath = Path.Combine(outputDirectory, serializerName + ".cs");

                if (!force)
                {
                    if (File.Exists(codePath))
                        throw new InvalidOperationException(SR.Format(SR.ErrSerializerExists, codePath, "force"));
                }

                if (Directory.Exists(codePath))
                {
                    throw new InvalidOperationException(SR.Format(SR.ErrDirectoryExists, codePath));
                }

                if (!Directory.Exists(outputDirectory))
                {
                    throw new ArgumentException(SR.Format(SR.ErrDirectoryNotExists, codePath, outputDirectory));
                }

                bool success;

                try
                {
                    if (File.Exists(codePath))
                    {
                        File.Delete(codePath);
                    }

                    using (FileStream fs = File.Create(codePath))
                    {
                        success = XmlSerializer.GenerateSerializer(serializableTypes, allMappings, fs);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException(SR.Format(SR.DirectoryAccessDenied, outputDirectory));
                }

                if (success)
                {
                    Console.Out.WriteLine(SR.Format(SR.InfoAssemblyName, codePath));
                    Console.Out.WriteLine(SR.Format(SR.InfoGeneratedAssembly, assembly.Location, codePath));
                }
                else
                {
                    Console.Out.WriteLine(FormatMessage(false, SR.Format(SR.ErrGenerationFailed, assembly.Location)));
                }
            }
            else
            {
                Console.Out.WriteLine(FormatMessage(true, SR.Format(SR.InfoNoSerializableTypes, assembly.Location)));
            }
        }

        // assumes all same case.        
        private bool ArgumentMatch(string arg, string formal)
        {
            if (arg[0] != '/' && arg[0] != '-')
            {
                return false;
            }

            arg = arg.Substring(1);
            return (arg == formal || (arg.Length == 1 && arg[0] == formal[0]));
        }

        private void ImportType(Type type, ArrayList mappings, ArrayList importedTypes, XmlReflectionImporter importer)
        {
            XmlTypeMapping xmlTypeMapping = null;
            var localImporter = new XmlReflectionImporter();
            try
            {
                xmlTypeMapping = localImporter.ImportTypeMapping(type);
            }
            catch (Exception e)
            {
                if (e is ThreadAbortException || e is StackOverflowException || e is OutOfMemoryException)
                {
                    throw;
                }
                return;
            }
            if (xmlTypeMapping != null)
            {
                xmlTypeMapping = importer.ImportTypeMapping(type);
                mappings.Add(xmlTypeMapping);
                importedTypes.Add(type);
            }
        }

        private static Assembly LoadAssembly(string assemblyName, bool throwOnFail)
        {
            Assembly assembly = null;
            string path = Path.GetFullPath(assemblyName);
            assembly = Assembly.LoadFile(path);
            if(assembly == null)
            {
                if (throwOnFail)
                {
                    throw new InvalidOperationException(SR.Format(SR.ErrLoadAssembly, assemblyName));
                }
            }

            return assembly;
        }

        private void WriteHeader()
        {
            // do not localize Copyright header
            Console.WriteLine(String.Format(CultureInfo.CurrentCulture, "[Microsoft (R) .NET Framework, Version {0}]", TempAssembly.ThisAssembly.InformationalVersion));
            Console.WriteLine("Copyright (C) Microsoft Corporation. All rights reserved.");
        }

        void WriteHelp()
        {
            //TBD
            Console.WriteLine("In Development");
        }

        private static string FormatMessage(bool warning, string message)
        {
            return FormatMessage(warning, "SGEN1", message);
        }

        private static string FormatMessage(bool warning, string code, string message)
        {
            return "SGEN: " + (warning ? "warning " : "error ") + code + ": " + message;
        }

        static void Error(Exception e, string prefix)
        {
            Console.Error.WriteLine(prefix + e.Message);
            if (e.InnerException != null)
            {
                Error(e.InnerException, prefix);
            }
        }
    }
}