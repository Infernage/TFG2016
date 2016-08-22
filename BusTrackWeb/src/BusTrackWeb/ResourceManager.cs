using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BusTrackWeb
{
    /// <summary>
    /// Manages all embed resources to perform a easy access to them.
    /// </summary>
    class ResourceManager
    {
        /// <summary>
        /// Resources deployed.
        /// </summary>
        static readonly IReadOnlyDictionary<string, string> resources;
        static readonly string prefix = "BusTrackWeb.";

        /// <summary>
        /// Static constructor.
        /// </summary>
        static ResourceManager()
        {
            // Get all resources names
            List<string> res = Assembly.GetEntryAssembly().GetManifestResourceNames().ToList();
            string path = Path.Combine(AppContext.BaseDirectory, "Resources");

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            Dictionary<string, string> dic = new Dictionary<string, string>();

            // Extract every resource
            foreach (string resource in res)
            {
                string basePath = path;
                string name = resource.Replace(prefix, "");
                string baseName = Path.GetFileNameWithoutExtension(name);

                // Resource with directory found!
                if (baseName.Contains('.'))
                {
                    int idx = baseName.LastIndexOf('.');
                    basePath = Path.Combine(path, baseName.Substring(0, idx).Replace('.', Path.DirectorySeparatorChar));
                    name = name.Substring(idx + 1);
                    if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);
                }

                // Add the resource to the dictionary
                dic.Add(resource, Path.Combine(basePath, name));

                if (File.Exists(Path.Combine(basePath, name))) continue; // Resource already extracted! Skip

                using (var reader = Assembly.GetEntryAssembly().GetManifestResourceStream(resource))
                using (var writer = new FileStream(Path.Combine(basePath, name), FileMode.CreateNew))
                {
                    reader.CopyTo(writer);
                }
            }

            // Update read-only dictionary
            resources = dic;
        }

        /// <summary>
        /// Gets a resource location, given a key name.
        /// </summary>
        /// <param name="name">The resource key name.</param>
        /// <returns>Returns the location where the resource is deployed.</returns>
        public static string GetResourceLocation(string name)
        {
            if (!name.StartsWith(prefix)) name = prefix + name;

            return resources[name];
        }
    }
}
