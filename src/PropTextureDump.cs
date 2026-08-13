using System;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// One-shot recon: writes the vanilla upgrade prop's textures to disk and logs its renderer,
/// material and per-submesh UV layout. We need this to know which region of the box's atlas is the
/// front face before pasting custom art into it — guessing would put the poster on every side.
///
/// Off by default; enable DumpPropTextures in the config, start a run, then look in
/// BepInEx/ExtraInventorySlotUpgrade-dump.
/// </summary>
internal static class PropTextureDump
{
    private static bool _done;

    public static string DumpDirectory =>
        Path.Combine(Paths.BepInExRootPath, "ExtraInventorySlotUpgrade-dump");

    public static void Run(GameObject prop, string baseItemName)
    {
        if (_done || !PluginConfig.DumpPropTextures.Value) return;
        _done = true;

        try
        {
            Directory.CreateDirectory(DumpDirectory);
            Log.Info($"Dumping prop textures to {DumpDirectory}");

            var report = new StringBuilder();
            report.AppendLine($"Base item: {baseItemName}");
            report.AppendLine($"Prop root: {prop.name}");
            report.AppendLine();

            int rendererIndex = 0;
            foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                report.AppendLine($"Renderer[{rendererIndex}] \"{renderer.name}\" " +
                                  $"({renderer.GetType().Name}) path={DescribePath(renderer.transform, prop.transform)}");

                DescribeMesh(renderer, report);

                var materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        report.AppendLine($"  Material[{m}] <null>");
                        continue;
                    }

                    report.AppendLine($"  Material[{m}] \"{material.name}\" shader=\"{material.shader?.name}\"");
                    DescribeAndDumpTextures(material, rendererIndex, m, report);
                }

                report.AppendLine();
                rendererIndex++;
            }

            string reportPath = Path.Combine(DumpDirectory, "layout.txt");
            File.WriteAllText(reportPath, report.ToString());
            Log.Info($"Wrote {reportPath}");
        }
        catch (Exception e)
        {
            Log.Warn("Prop texture dump failed: " + e);
        }
    }

    /// <summary>
    /// Per-submesh UV bounds are the useful part: they say which rectangle of the shared atlas each
    /// group of faces samples, which is exactly what we need to target the front face.
    /// </summary>
    private static void DescribeMesh(Renderer renderer, StringBuilder report)
    {
        Mesh mesh = null;
        if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
        else
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null) mesh = filter.sharedMesh;
        }

        if (mesh == null)
        {
            report.AppendLine("  Mesh: <none>");
            return;
        }

        report.AppendLine($"  Mesh \"{mesh.name}\" submeshes={mesh.subMeshCount} readable={mesh.isReadable}");
        if (!mesh.isReadable)
        {
            report.AppendLine("    (not readable at runtime — UV bounds unavailable)");
            return;
        }

        try
        {
            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0)
            {
                report.AppendLine("    (no UV0 channel)");
                return;
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                if (triangles.Length == 0)
                {
                    report.AppendLine($"    submesh[{s}]: empty");
                    continue;
                }

                float minU = float.MaxValue, minV = float.MaxValue;
                float maxU = float.MinValue, maxV = float.MinValue;
                foreach (int index in triangles)
                {
                    if (index < 0 || index >= uvs.Length) continue;
                    Vector2 uv = uvs[index];
                    minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x);
                    minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y);
                }

                report.AppendLine($"    submesh[{s}]: tris={triangles.Length / 3} " +
                                  $"uv=({minU:0.####}, {minV:0.####}) .. ({maxU:0.####}, {maxV:0.####})");
            }
        }
        catch (Exception e)
        {
            report.AppendLine("    (UV read failed: " + e.Message + ")");
        }
    }

    private static void DescribeAndDumpTextures(Material material, int rendererIndex, int materialIndex,
                                                StringBuilder report)
    {
        foreach (string property in material.GetTexturePropertyNames())
        {
            Texture texture = material.GetTexture(property);
            if (texture == null)
            {
                report.AppendLine($"    {property} = <none>");
                continue;
            }

            report.AppendLine($"    {property} = \"{texture.name}\" {texture.width}x{texture.height} " +
                              $"({texture.GetType().Name})");

            string file = Sanitize($"r{rendererIndex}_m{materialIndex}_{property}_{texture.name}") + ".png";
            string path = Path.Combine(DumpDirectory, file);
            if (File.Exists(path)) continue;

            Texture2D readable = null;
            try
            {
                readable = UpgradeAlbedo.ReadBack(texture);
                if (readable == null) continue;
                readable.Apply();
                File.WriteAllBytes(path, readable.EncodeToPNG());
                report.AppendLine($"      -> {file}");
            }
            catch (Exception e)
            {
                report.AppendLine($"      -> dump failed: {e.Message}");
            }
            finally
            {
                if (readable != null) Object.Destroy(readable);
            }
        }
    }

    private static string DescribePath(Transform child, Transform root)
    {
        string path = child.name;
        for (Transform t = child.parent; t != null && t != root.parent; t = t.parent)
        {
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        }
        return sb.ToString();
    }
}
