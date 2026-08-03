using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;

using GLTFRevitExport.Build;

namespace GLTFRevitExport {
    /// <summary>
    /// Resolves a Revit material's base color texture image (and its
    /// real-world tiling scale) from the material's appearance asset.
    /// Materials whose textures can't be resolved simply export with flat
    /// color. Procedural appearances (wood grain, noise, checker) have no
    /// image file anywhere — Revit only exposes their parameters — so they
    /// fall back to flat color by design.
    /// </summary>
    static class TextureUtils {
        /// <summary>A resolved, glTF-ready texture image.</summary>
        public class ResolvedTexture {
            public byte[] Bytes;
            public string MimeType;
            /// <summary>Feet per horizontal texture repeat.</summary>
            public double ScaleU = DefaultRepeatFeet;
            /// <summary>Feet per vertical texture repeat.</summary>
            public double ScaleV = DefaultRepeatFeet;
        }

        // Texture files larger than this are skipped — they are embedded as
        // base64 in the .gltf and oversized maps would balloon the upload.
        const long MaxTextureFileBytes = 8 * 1024 * 1024;

        // Used when the appearance asset has no real-world scale.
        const double DefaultRepeatFeet = 1.0;

        // Asset-property-name fragments that indicate a non-basecolor map we
        // must not use as the diffuse texture.
        static readonly string[] NonDiffuseHints = {
            "bump", "normal", "relief", "cutout", "opacity", "displace",
            "roughness", "glossiness", "reflect", "transparen", "refr",
            "pattern", "aniso", "emission", "emit"
        };

        // material UniqueId -> resolved texture (null = looked up, nothing
        // found). Cleared at the start of every export via ClearCache.
        static readonly Dictionary<string, ResolvedTexture> _materialCache =
            new Dictionary<string, ResolvedTexture>();

        // fileName(lower) -> full path index of the Autodesk Shared texture
        // library. Built once per session; the library doesn't change.
        static Dictionary<string, string> _sharedLibraryIndex;

        static readonly HashSet<string> _loggedMessages = new HashSet<string>();

        public static void ClearCache() {
            _materialCache.Clear();
            _loggedMessages.Clear();
        }

        /// <summary>Log a message once per export (deduplicated by content).</summary>
        static void LogOnce(string message) {
            if (_loggedMessages.Add(message))
                Logger.Log(message);
        }

        /// <summary>
        /// Try to resolve the base color texture for a material. Cached per
        /// material. Returns null when no texture can be found.
        /// </summary>
        public static ResolvedTexture ResolveTexture(Material material) {
            if (material is null)
                return null;

            string cacheKey = material.UniqueId;
            if (_materialCache.TryGetValue(cacheKey, out ResolvedTexture cached))
                return cached;

            ResolvedTexture result = null;
            try {
                result = ResolveTextureImpl(material);
            }
            catch (Exception ex) {
                LogOnce($"[TEXTURE] Resolution failed for material '{material.Name}': {ex.Message}");
            }

            _materialCache[cacheKey] = result;
            return result;
        }

        static ResolvedTexture ResolveTextureImpl(Material material) {
            var doc = material.Document;

            if (!(doc.GetElement(material.AppearanceAssetId) is AppearanceAssetElement appearance)) {
                LogOnce($"[TEXTURE] Material '{material.Name}' has no appearance asset");
                return null;
            }

            Asset rendering = appearance.GetRenderingAsset();

            // unedited library appearances return an empty asset; look the
            // asset up in the application's appearance library by name
            if (rendering is null || rendering.Size == 0) {
                var appAssets = doc.Application.GetAssets(AssetType.Appearance);
                string assetName = rendering?.Name;
                rendering = appAssets.FirstOrDefault(a => a.Name == assetName);
                if (rendering is null) {
                    LogOnce($"[TEXTURE] Material '{material.Name}' has an empty rendering asset");
                    return null;
                }
            }

            Asset bitmapAsset = FindDiffuseBitmapAsset(rendering);
            if (bitmapAsset is null) {
                LogOnce($"[TEXTURE] No image-based diffuse map on material '{material.Name}' (procedural or plain color)");
                return null;
            }

            string fullPath = null;
            if (bitmapAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is AssetPropertyString pathProp
                    && !string.IsNullOrEmpty(pathProp.Value)) {
                // the property can hold multiple candidates separated by '|'
                foreach (var candidate in pathProp.Value.Split('|')) {
                    fullPath = ResolveTexturePath(candidate.Trim(), doc);
                    if (fullPath != null)
                        break;
                }

                if (fullPath == null) {
                    LogOnce($"[TEXTURE] Reference '{pathProp.Value}' (material '{material.Name}') not found on disk");
                    return null;
                }
            }
            else {
                LogOnce($"[TEXTURE] Diffuse map on material '{material.Name}' has no bitmap path");
                return null;
            }

            var result = LoadAsGltfImage(fullPath);
            if (result is null)
                return null;

            result.ScaleU = GetDistanceInFeet(bitmapAsset, UnifiedBitmap.TextureRealWorldScaleX, DefaultRepeatFeet);
            result.ScaleV = GetDistanceInFeet(bitmapAsset, UnifiedBitmap.TextureRealWorldScaleY, DefaultRepeatFeet);

            LogOnce($"[TEXTURE] Material '{material.Name}' -> {fullPath} ({result.Bytes.Length / 1024}KB, repeat {result.ScaleU:0.###}x{result.ScaleV:0.###}ft)");
            return result;
        }

        /// <summary>
        /// Walk the rendering asset's properties for a connected
        /// UnifiedBitmap asset, preferring diffuse/albedo/color maps over
        /// bump/cutout style maps. Handles all appearance schemas (generic,
        /// PRISM/advanced, ceramic, concrete, ...) without hardcoding
        /// per-schema property names.
        /// </summary>
        static Asset FindDiffuseBitmapAsset(Asset rendering) {
            var candidates = new List<KeyValuePair<string, Asset>>(); // (propName, bitmapAsset)

            for (int i = 0; i < rendering.Size; i++) {
                AssetProperty prop = rendering[i];
                if (prop is null || prop.NumberOfConnectedProperties == 0)
                    continue;

                if (!(prop.GetSingleConnectedAsset() is Asset connected))
                    continue;

                // only image-based maps carry a unifiedbitmap_Bitmap path;
                // procedural maps (noise, checker, wood) do not
                if (connected.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) is AssetPropertyString)
                    candidates.Add(new KeyValuePair<string, Asset>(prop.Name ?? string.Empty, connected));
            }

            if (candidates.Count == 0)
                return null;

            // prefer explicit diffuse/albedo/color maps
            foreach (var c in candidates) {
                string n = c.Key.ToLowerInvariant();
                if (n.Contains("diffuse") || n.Contains("albedo") || n.Contains("color"))
                    return c.Value;
            }

            // then anything not flagged as a bump/cutout style map
            foreach (var c in candidates) {
                string n = c.Key.ToLowerInvariant();
                if (!NonDiffuseHints.Any(h => n.Contains(h)))
                    return c.Value;
            }

            return null;
        }

        static double GetDistanceInFeet(Asset bitmapAsset, string propName, double fallback) {
            if (!(bitmapAsset.FindByName(propName) is AssetPropertyDistance dist) || dist.Value <= 0)
                return fallback;
            try {
                return UnitUtils.Convert(dist.Value, dist.GetUnitTypeId(), UnitTypeId.Feet);
            }
            catch {
                // asset distances are commonly stored in inches
                return dist.Value / 12.0;
            }
        }

        /// <summary>
        /// Resolve a texture path against the absolute path itself, the
        /// model's folder, and the Autodesk Shared materials library.
        /// </summary>
        static string ResolveTexturePath(string fileRef, Document doc) {
            if (string.IsNullOrEmpty(fileRef))
                return null;

            if (Path.IsPathRooted(fileRef) && File.Exists(fileRef))
                return fileRef;

            string fileName;
            try {
                fileName = Path.GetFileName(fileRef);
            }
            catch (ArgumentException) {
                return null;
            }

            // next to the model file itself
            try {
                if (!string.IsNullOrEmpty(doc.PathName)) {
                    var local = Path.Combine(Path.GetDirectoryName(doc.PathName), fileName);
                    if (File.Exists(local))
                        return local;
                }
            }
            catch { /* unsaved or cloud document */ }

            if (_sharedLibraryIndex == null)
                _sharedLibraryIndex = BuildSharedLibraryIndex();

            // relative library paths like "1/Mats/brick.png" resolve by file
            // name against the shared library index
            return _sharedLibraryIndex.TryGetValue(fileName.ToLowerInvariant(), out string path) ? path : null;
        }

        static Dictionary<string, string> BuildSharedLibraryIndex() {
            var index = new Dictionary<string, string>();

            var roots = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), @"Autodesk Shared\Materials\Textures"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"Autodesk Shared\Materials\Textures"),
            };

            foreach (var root in roots.Distinct()) {
                if (!Directory.Exists(root))
                    continue;
                try {
                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
                        var key = Path.GetFileName(file).ToLowerInvariant();
                        if (!index.ContainsKey(key))
                            index[key] = file;
                    }
                }
                catch (Exception ex) {
                    LogOnce($"[TEXTURE] Failed indexing '{root}': {ex.Message}");
                }
            }

            LogOnce($"[TEXTURE] Autodesk Shared library index: {index.Count} files");
            return index;
        }

        /// <summary>
        /// Load image bytes in a glTF-legal encoding. JPEGs pass through
        /// unchanged; PNGs get ancillary chunks stripped; other formats
        /// (bmp/tiff/tga/gif) are re-encoded to PNG.
        /// </summary>
        static ResolvedTexture LoadAsGltfImage(string path) {
            var info = new FileInfo(path);
            if (info.Length > MaxTextureFileBytes) {
                LogOnce($"[TEXTURE] Skipping oversized texture ({info.Length / (1024 * 1024)}MB): {path}");
                return null;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
                return new ResolvedTexture { Bytes = File.ReadAllBytes(path), MimeType = "image/jpeg" };

            if (ext == ".png")
                return new ResolvedTexture { Bytes = StripAncillaryPngChunks(File.ReadAllBytes(path)), MimeType = "image/png" };

            // everything else is re-encoded to a plain PNG; the GDI+ PNG
            // encoder adds gAMA/sRGB chunks on save, so strip after encoding
            try {
                using (var img = System.Drawing.Image.FromFile(path))
                using (var ms = new MemoryStream()) {
                    img.Save(ms, ImageFormat.Png);
                    return new ResolvedTexture { Bytes = StripAncillaryPngChunks(ms.ToArray()), MimeType = "image/png" };
                }
            }
            catch (Exception ex) {
                LogOnce($"[TEXTURE] Could not re-encode '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Remove ancillary PNG chunks (gAMA, sRGB, cHRM, iCCP, pHYs, ...)
        /// that glTF validators flag as "non-default colorspace information",
        /// keeping only what's needed to decode: IHDR, PLTE, tRNS, IDAT, IEND.
        /// Returns the input unchanged if it doesn't parse as a PNG.
        /// </summary>
        static byte[] StripAncillaryPngChunks(byte[] png) {
            if (png.Length < 8 || png[0] != 0x89 || png[1] != 0x50)
                return png;

            var keep = new HashSet<string> { "IHDR", "PLTE", "tRNS", "IDAT", "IEND" };
            using (var output = new MemoryStream()) {
                output.Write(png, 0, 8); // signature
                int pos = 8;
                while (pos + 12 <= png.Length) {
                    int length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                    string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                    int chunkTotal = 12 + length;
                    if (length < 0 || pos + chunkTotal > png.Length)
                        return png; // malformed; keep original

                    if (keep.Contains(type))
                        output.Write(png, pos, chunkTotal);

                    pos += chunkTotal;
                    if (type == "IEND")
                        break;
                }
                return output.ToArray();
            }
        }
    }
}
