﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;

namespace Axe.Windows.Core.CustomObjects
{
    public class Config
    {
#pragma warning disable CA1819 // Properties should not return arrays: represents a JSON collection
        [JsonProperty("properties")]
        public CustomProperty[] Properties { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays: represents a JSON collection

        public static Config ReadFromText(string text)
        {
            Config conf = JsonConvert.DeserializeObject<Config>(text);
            conf.Validate();
            // We made it here, so config must be valid.
            return conf;
        }

        public static Config ReadFromFile(string path) { return Config.ReadFromText(File.ReadAllText(path)); }

        private void Validate()
        {
            if (Properties == null || !Properties.Any()) throw new InvalidDataException("Empty or missing definition of custom properties.");
            foreach (CustomProperty p in Properties) 
                p.Validate();
        }
    }
}