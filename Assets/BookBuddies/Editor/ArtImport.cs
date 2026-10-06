using UnityEditor;

namespace BookBuddies.EditorTools
{
    /// <summary>
    /// Import settings for everything in Resources/BookBuddies, so any PNG you drop in (a new building,
    /// a redrawn emote) comes in sharp and the right shape: no power-of-two resizing, smooth edges, mipmaps
    /// for zooming out, and high-quality compression.
    /// </summary>
    public sealed class ArtImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/BookBuddies/Resources/BookBuddies/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default; // sprites are cut in code from art_index.json
            t.npotScale = TextureImporterNPOTScale.None;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = true;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.filterMode = UnityEngine.FilterMode.Trilinear;
            t.maxTextureSize = 4096;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
