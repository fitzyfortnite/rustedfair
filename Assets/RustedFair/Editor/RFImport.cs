using UnityEditor;
using UnityEngine;

namespace RustedFair.EditorTools
{
    /// <summary>Automatic import settings for the generated art and audio (mobile-friendly).</summary>
    public class RFImport : AssetPostprocessor
    {
        const string TexDir = "Assets/RustedFair/Resources/RF/Textures/";
        const string UIDir = "Assets/RustedFair/Resources/RF/UI/";
        const string AudioDir = "Assets/RustedFair/Resources/RF/Audio/";

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            string path = assetPath.Replace('\\', '/');
            string file = System.IO.Path.GetFileNameWithoutExtension(path);

            // v1.0.0: splash logo (PlayerSettings.SplashScreen.logos needs a Sprite)
            if (path.StartsWith("Assets/RustedFair/Branding/app_icon"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 1024;
                return;
            }
            if (path.StartsWith("Assets/RustedFair/Branding/"))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 2048;
                return;
            }

            if (path.StartsWith(UIDir))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = file == "app_icon" ? 1024 : 1024;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                if (file.StartsWith("panel_bg"))
                {
                    ti.spriteBorder = new Vector4(48, 48, 48, 48);
                }
                else if (file.StartsWith("button_bg"))
                {
                    ti.spriteBorder = new Vector4(32, 32, 32, 32);
                }
                if (file.StartsWith("app_icon")) // app_icon, app_icon_1024, app_icon_fg_1024, app_icon_bg_1024
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                }
                return;
            }

            if (path.StartsWith(TexDir) && file.StartsWith("robot_"))
            {
                // v1.0.3: robot atlases (albedo + alpha-cut mask, normal, metallic/AO/smoothness)
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 2048;
                ti.anisoLevel = 2;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Compressed;
                if (file.EndsWith("_n"))
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.sRGBTexture = false;
                }
                else
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = !file.EndsWith("_ms");
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = false;
                    ti.mipMapsPreserveCoverage = !file.EndsWith("_ms"); // keeps alpha-tested holes stable in the distance
                    ti.alphaTestReferenceValue = 0.5f;
                }
                return;
            }

            if (path.StartsWith(TexDir))
            {
                ti.mipmapEnabled = true;
                ti.maxTextureSize = 1024;
                ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                if (file.EndsWith("_n"))
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.sRGBTexture = false;
                }
                else if (file == "flashlight_cookie")
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                    ti.mipmapEnabled = false;
                    ti.sRGBTexture = false;
                }
                else
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaIsTransparency = file.StartsWith("decal_") || file.StartsWith("sign_logo") || file == "note_paper" ||
                                             file == "fence" || file == "rain_streak" || file == "eye_glow" || file == "cloud_layer";
                    if (file.StartsWith("decal_") || file == "eye_glow" || file == "rain_streak" || file == "note_paper" || file.StartsWith("sign_") || file.StartsWith("poster_"))
                        ti.wrapMode = TextureWrapMode.Clamp;
                    else
                        ti.wrapMode = TextureWrapMode.Repeat;
                }
            }
        }

        // v1.1: the bundled fonts lack the task status glyphs (✓ ▸ ○). Dynamic fonts fall back to these OS fonts.
        static readonly string[] SymbolFallbacks =
        {
            "Segoe UI Symbol", "Arial Unicode MS", "Apple Symbols", "Apple Color Emoji", "Noto Sans Symbols",
            "Noto Sans Symbols2", "Noto Sans CJK SC", "Roboto", "DejaVu Sans", "Arial"
        };

        void OnPreprocessAsset()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/RustedFair/Resources/RF/Fonts/")) return;
            var fi = assetImporter as TrueTypeFontImporter;
            if (fi == null) return;
            fi.fontTextureCase = FontTextureCase.Dynamic;
            fi.includeFontData = true;
            var names = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(fi.fontTTFName)) names.Add(fi.fontTTFName);
            names.AddRange(SymbolFallbacks);
            fi.fontNames = names.ToArray();
        }

        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AudioDir)) return;
            var ai = (AudioImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            var s = ai.defaultSampleSettings;
            bool longLoop = file.StartsWith("mus_") || file.StartsWith("amb_");
            if (longLoop)
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.85f;
            }
            ai.defaultSampleSettings = s;
            ai.loadInBackground = longLoop;
        }
    }
}
