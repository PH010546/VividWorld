using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class MergeResult
    {
        public JObject Merged { get; }
        public IReadOnlyList<string> AddedPaths { get; }

        public MergeResult(JObject merged, IReadOnlyList<string> addedPaths)
        {
            Merged = merged ?? throw new ArgumentNullException(nameof(merged));
            AddedPaths = addedPaths ?? Array.Empty<string>();
        }
    }

    public static class ConfigMerge
    {
        /// <summary>
        /// 把 canonical 裡有、existing 裡沒有的路徑補進去。
        /// 已存在的值一律保留，包括物件內的巢狀值。
        /// </summary>
        public static MergeResult AddMissingKeys(JObject existing, JObject canonical)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (canonical == null) throw new ArgumentNullException(nameof(canonical));

            var merged = (JObject)existing.DeepClone();
            var addedPaths = new List<string>();

            MergeObjects(merged, canonical, string.Empty, addedPaths);

            return new MergeResult(merged, addedPaths);
        }

        private static void MergeObjects(JObject target, JObject source, string currentPath, List<string> addedPaths)
        {
            foreach (var sourceProp in source.Properties())
            {
                var key = sourceProp.Name;
                var path = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}.{key}";

                var targetProp = target.Property(key, StringComparison.OrdinalIgnoreCase);

                if (targetProp == null)
                {
                    if (sourceProp.Value is JObject sourceChildObj)
                    {
                        var newChildObj = new JObject();
                        target[key] = newChildObj;
                        MergeObjects(newChildObj, sourceChildObj, path, addedPaths);
                        if (!sourceChildObj.Properties().Any())
                        {
                            addedPaths.Add(path);
                        }
                    }
                    else
                    {
                        target[key] = sourceProp.Value.DeepClone();
                        addedPaths.Add(path);
                    }
                }
                else
                {
                    if (targetProp.Value is JObject targetChildObj && sourceProp.Value is JObject sourceChildObj)
                    {
                        MergeObjects(targetChildObj, sourceChildObj, path, addedPaths);
                    }
                }
            }
        }
    }
}
