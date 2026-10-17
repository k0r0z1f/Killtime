using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Catalogue des textures de sol (extrait sans modification depuis MapEditorDevWindow.cs).
    /// </summary>
    public partial class MapEditorDevWindow
    {
        public void RefreshAvailableGroundTextures()
        {
            _availableGroundTextures.Clear();
            _groundCategories.Clear();
            _groundCategories.Add("Tous");

            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Grounds" });
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                int idx = path.IndexOf("Resources/Grounds/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string rel = path.Substring(idx + "Resources/Grounds/".Length);
                    string ext = Path.GetExtension(rel);
                    if (!string.IsNullOrEmpty(ext)) rel = rel.Substring(0, rel.Length - ext.Length);

                    if (!loaded.Contains(rel))
                    {
                        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (tex != null)
                        {
                            string dir = Path.GetDirectoryName(rel).Replace('\\', '/');
                            string cat = string.IsNullOrEmpty(dir) ? "Racine" : dir;
                            if (!_groundCategories.Contains(cat)) _groundCategories.Add(cat);

                            _availableGroundTextures.Add(new GroundTextureRecord
                            {
                                RelativePath = rel,
                                Category = cat,
                                DisplayName = Path.GetFileName(rel),
                                Texture = tex
                            });
                            loaded.Add(rel);
                        }
                    }
                }
            }
#endif

            if (_availableGroundTextures.Count == 0)
            {
                var allTextures = Resources.LoadAll<Texture2D>("Grounds");
                for (int i = 0; i < allTextures.Length; i++)
                {
                    var tex = allTextures[i];
                    if (tex != null && !loaded.Contains(tex.name))
                    {
                        _availableGroundTextures.Add(new GroundTextureRecord
                        {
                            RelativePath = tex.name,
                            Category = "Grounds",
                            DisplayName = tex.name,
                            Texture = tex
                        });
                        loaded.Add(tex.name);
                    }
                }
            }
        }
    }
}
