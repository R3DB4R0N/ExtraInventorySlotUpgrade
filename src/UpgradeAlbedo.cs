using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ExtraInventorySlotUpgrade;

/// <summary>
/// Builds the upgrade prop's albedo at load time. No asset bundle and no Unity Editor: the mesh is
/// the vanilla "Upgrade Pack" box, only its texture changes.
///
/// The vanilla albedo is a 512x512 unfolded carton. Measured from a runtime dump of
/// Upgrade_Grab-Strength_Albedo:
///   rows   0-154 : transparent, unused UV space
///   rows 155-229 : top and bottom flaps
///   rows 230-511 : the four side faces, laid out horizontally
/// and the strongest colour discontinuities across that band sit at x = 186, 254 and 440, which is
/// consistent with a box 186 wide by 70 deep (2*186 + 2*70 = 512). The front face — the panel
/// carrying the hero art — is therefore x 256-441, y 230-511 in image space.
///
/// Expressed as UVs so it survives a texture resolution change, and exposed as a config entry so a
/// game update that shifts the atlas can be corrected without a rebuild.
///
/// Note on orientation: EncodeToPNG writes top-down while Unity's pixel arrays are bottom-up, so
/// image row 230-511 is UV v 0.0-0.551.
/// </summary>
internal static class UpgradeAlbedo
{
    private const string ResourceName = "ExtraInventorySlotUpgrade.Pack-texture.png";

    private static readonly Dictionary<Texture, Texture2D> Cache = new();
    private static Texture2D _customArt;
    private static bool _customArtLoaded;

    /// <summary>
    /// Pink, custom-fronted copy of <paramref name="source"/>, or null if the round-trip failed
    /// (the caller then falls back to plain colour tinting).
    /// </summary>
    public static Texture2D Generate(Texture source, Color pink)
    {
        if (source == null) return null;
        if (Cache.TryGetValue(source, out Texture2D cached) && cached != null) return cached;

        try
        {
            // Recolour at native resolution — it is a per-pixel HSV pass, and doing it before any
            // upscale keeps it cheap.
            Texture2D albedo = ReadBack(source, source.width, source.height);
            if (albedo == null) return null;

            Recolour(albedo, pink);
            albedo.Apply();

            int target = Mathf.Clamp(PluginConfig.AlbedoResolution.Value, source.width, 4096);
            if (target != albedo.width)
            {
                Texture2D upscaled = ReadBack(albedo, target, target);
                if (upscaled != null)
                {
                    Object.Destroy(albedo);
                    albedo = upscaled;
                }
            }

            HealSideBleed(albedo);
            PasteFrontFace(albedo);

            albedo.Apply(updateMipmaps: true);
            albedo.wrapMode = source.wrapMode;
            albedo.filterMode = source.filterMode;
            albedo.hideFlags = HideFlags.DontUnloadUnusedAsset;

            Cache[source] = albedo;
            Log.Info($"Generated albedo from \"{source.name}\" at {albedo.width}x{albedo.height}.");
            return albedo;
        }
        catch (Exception e)
        {
            Log.Warn($"Could not generate an albedo from \"{source.name}\"; " +
                     "falling back to plain colour tinting. " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// CPU-side copy of any texture at an arbitrary size, readable or not. Import settings usually
    /// leave game textures non-readable, so GetPixels would throw; a GPU blit sidesteps that and
    /// gives filtered rescaling for free.
    /// </summary>
    public static Texture2D ReadBack(Texture source, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        RenderTexture temp = RenderTexture.GetTemporary(
            width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source, temp);
            RenderTexture.active = temp;

            var readable = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = source.name + " (Extra Inventory Slot)"
            };
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            return readable;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);
        }
    }

    public static Texture2D ReadBack(Texture source) => ReadBack(source, source.width, source.height);

    /// <summary>
    /// Push every pixel onto the target hue while keeping its original value (brightness), so the
    /// carton's printed detail, wear and shading survive. Nearly-grey pixels take a muted version
    /// of the hue rather than full saturation, which stops shadows turning into flat magenta.
    /// </summary>
    private static void Recolour(Texture2D texture, Color pink)
    {
        Color.RGBToHSV(pink, out float pinkHue, out float pinkSaturation, out _);

        Color[] pixels = texture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color source = pixels[i];
            Color.RGBToHSV(source, out _, out float saturation, out float value);

            float targetSaturation = Mathf.Lerp(pinkSaturation * 0.55f, pinkSaturation, saturation);
            Color recoloured = Color.HSVToRGB(pinkHue, targetSaturation, value);
            recoloured.a = source.a;
            pixels[i] = recoloured;
        }
        texture.SetPixels(pixels);
    }

    /// <summary>
    /// The vanilla hero art wraps the front/side fold: the Grab Strength starburst spills onto the
    /// side panel at x 442-467, y 288-365. Our art stops at the fold, so that spill would be left
    /// stranded as a bright blob on the side of the box.
    ///
    /// Healed by mirroring the clean side-panel texture immediately to its right back over it.
    /// Mirroring rather than flat-filling keeps the printed grunge noise continuous, so the repair
    /// does not read as a patch.
    /// </summary>
    private static void HealSideBleed(Texture2D albedo)
    {
        string setting = PluginConfig.SideBleedUV.Value;
        if (string.IsNullOrWhiteSpace(setting)) return; // explicitly disabled

        if (!TryParseUvRect(setting, out Rect uv))
        {
            Log.Warn($"Could not parse SideBleedUV \"{setting}\"; expected \"u0,v0,u1,v1\". " +
                     "Skipping the fold cleanup.");
            return;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(uv.xMin * albedo.width), 0, albedo.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(uv.xMax * albedo.width), x0 + 1, albedo.width);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(uv.yMin * albedo.height), 0, albedo.height - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(uv.yMax * albedo.height), y0 + 1, albedo.height);

        int width = x1 - x0;
        int height = y1 - y0;

        // Need an equally wide strip of clean texture to the right to mirror from.
        int available = albedo.width - x1;
        if (available < 1)
        {
            Log.Warn("No clean texture to the right of the fold to heal from; skipping.");
            return;
        }
        int mirrorWidth = Mathf.Min(width, available);

        Color[] source = albedo.GetPixels(x1, y0, mirrorWidth, height);
        Color[] target = albedo.GetPixels(x0, y0, width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Mirror about the fold so the seam at x1 stays continuous.
                int mirrored = Mathf.Clamp(width - 1 - x, 0, mirrorWidth - 1);
                Color sample = source[y * mirrorWidth + mirrored];
                sample.a = target[y * width + x].a;
                target[y * width + x] = sample;
            }
        }

        albedo.SetPixels(x0, y0, width, height, target);
        Log.Verbose($"Healed the fold bleed: {width}x{height} at ({x0},{y0}).");
    }

    /// <summary>Stamps the custom art over the front panel, leaving the other faces recoloured.</summary>
    private static void PasteFrontFace(Texture2D albedo)
    {
        Texture2D art = LoadCustomArt();
        if (art == null) return;

        if (!TryParseUvRect(PluginConfig.FrontFaceUV.Value, out Rect uv))
        {
            Log.Warn($"Could not parse FrontFaceUV \"{PluginConfig.FrontFaceUV.Value}\"; " +
                     "expected \"u0,v0,u1,v1\". Leaving the front face recoloured only.");
            return;
        }

        int x0 = Mathf.Clamp(Mathf.RoundToInt(uv.xMin * albedo.width), 0, albedo.width - 1);
        int x1 = Mathf.Clamp(Mathf.RoundToInt(uv.xMax * albedo.width), x0 + 1, albedo.width);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(uv.yMin * albedo.height), 0, albedo.height - 1);
        int y1 = Mathf.Clamp(Mathf.RoundToInt(uv.yMax * albedo.height), y0 + 1, albedo.height);

        int width = x1 - x0;
        int height = y1 - y0;

        Color[] region = albedo.GetPixels(x0, y0, width, height);
        for (int y = 0; y < height; y++)
        {
            // Both the destination region and a LoadImage'd texture are bottom-up, so v maps
            // straight through with no flip.
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color sample = art.GetPixelBilinear(u, v);
                Color existing = region[y * width + x];

                // Keep the carton's own alpha so the poster cannot punch a hole in the mesh.
                sample.a = existing.a;
                region[y * width + x] = sample;
            }
        }
        albedo.SetPixels(x0, y0, width, height, region);

        Log.Info($"Stamped custom front art into {width}x{height} at ({x0},{y0}) " +
                 $"of the {albedo.width}x{albedo.height} albedo.");
    }

    private static Texture2D LoadCustomArt()
    {
        if (_customArtLoaded) return _customArt;
        _customArtLoaded = true;

        try
        {
            Assembly assembly = typeof(Plugin).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Log.Warn($"Embedded resource \"{ResourceName}\" is missing; the prop will be a " +
                         "plain recolour. This is a build problem, not a config one.");
                return null;
            }

            var data = new byte[stream.Length];
            int read = 0;
            while (read < data.Length)
            {
                int chunk = stream.Read(data, read, data.Length - read);
                if (chunk <= 0) break;
                read += chunk;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            if (!texture.LoadImage(data))
            {
                Log.Warn("Custom front art failed to decode.");
                Object.Destroy(texture);
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _customArt = texture;
            Log.Info($"Loaded custom front art ({texture.width}x{texture.height}).");
            return _customArt;
        }
        catch (Exception e)
        {
            Log.Warn("Could not load the custom front art: " + e.Message);
            return null;
        }
    }

    private static bool TryParseUvRect(string value, out Rect rect)
    {
        rect = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] parts = value.Split(',');
        if (parts.Length != 4) return false;

        var numbers = new float[4];
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        float u0 = Mathf.Min(numbers[0], numbers[2]);
        float u1 = Mathf.Max(numbers[0], numbers[2]);
        float v0 = Mathf.Min(numbers[1], numbers[3]);
        float v1 = Mathf.Max(numbers[1], numbers[3]);
        if (u1 <= u0 || v1 <= v0) return false;

        rect = Rect.MinMaxRect(u0, v0, u1, v1);
        return true;
    }
}
