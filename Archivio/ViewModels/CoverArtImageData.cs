using System;

namespace Archivio.ViewModels
{
    public sealed class CoverArtImageData
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string MimeType { get; set; } = "image/jpeg";
        public string Description { get; set; } = string.Empty;
    }
}
