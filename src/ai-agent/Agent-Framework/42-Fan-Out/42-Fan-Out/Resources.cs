using System;
using System.Collections.Generic;
using System.Text;

namespace _42_Fan_Out
{
    internal static class Resources
    {
        private const string ResourceFolder = "Resources";

        public static string Read(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ResourceFolder, fileName));
    }
}
