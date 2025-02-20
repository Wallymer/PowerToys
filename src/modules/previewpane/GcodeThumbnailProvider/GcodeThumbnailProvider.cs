﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Common.ComInterlop;
using Common.Utilities;

namespace Microsoft.PowerToys.ThumbnailHandler.Gcode
{
    /// <summary>
    /// G-code Thumbnail Provider.
    /// </summary>
    [Guid("BFEE99B4-B74D-4348-BCA5-E757029647FF")]
    [ClassInterface(ClassInterfaceType.None)]
    [ComVisible(true)]
    public class GcodeThumbnailProvider : IInitializeWithStream, IThumbnailProvider
    {
        /// <summary>
        /// Gets the stream object to access file.
        /// </summary>
        public IStream Stream { get; private set; }

        /// <summary>
        ///  The maximum dimension (width or height) thumbnail we will generate.
        /// </summary>
        private const uint MaxThumbnailSize = 10000;

        /// <summary>
        /// Reads the G-code content searching for thumbnails and returns the largest.
        /// </summary>
        /// <param name="reader">The TextReader instance for the G-code content.</param>
        /// <param name="cx">The maximum thumbnail size, in pixels.</param>
        /// <returns>A thumbnail extracted from the G-code content.</returns>
        public static Bitmap GetThumbnail(TextReader reader, uint cx)
        {
            if (cx > MaxThumbnailSize || reader == null)
            {
                return null;
            }

            Bitmap thumbnail = null;

            var bitmapBase64 = GetBase64Thumbnails(reader)
                .OrderByDescending(x => x.Length)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(bitmapBase64))
            {
                var bitmapBytes = Convert.FromBase64String(bitmapBase64);

                using (var bitmapStream = new MemoryStream(bitmapBytes))
                {
                    thumbnail = new Bitmap(bitmapStream);
                }

                if (thumbnail.Width != cx && thumbnail.Height != cx)
                {
                    // We are not the appropriate size for caller.  Resize now while
                    // respecting the aspect ratio.
                    float scale = Math.Min((float)cx / thumbnail.Width, (float)cx / thumbnail.Height);
                    int scaleWidth = (int)(thumbnail.Width * scale);
                    int scaleHeight = (int)(thumbnail.Height * scale);
                    thumbnail = ResizeImage(thumbnail, scaleWidth, scaleHeight);
                }
            }

            return thumbnail;
        }

        /// <summary>
        /// Gets all thumbnails in base64 format found on the G-code data.
        /// </summary>
        /// <param name="reader">The TextReader instance for the G-code content.</param>
        /// <returns>An enumeration of thumbnails in base64 format found on the G-code.</returns>
        private static IEnumerable<string> GetBase64Thumbnails(TextReader reader)
        {
            string line;
            StringBuilder capturedText = null;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("; thumbnail begin", StringComparison.InvariantCulture))
                {
                    capturedText = new StringBuilder();
                }
                else if (line == "; thumbnail end")
                {
                    if (capturedText != null)
                    {
                        yield return capturedText.ToString();

                        capturedText = null;
                    }
                }
                else if (capturedText != null)
                {
                    capturedText.Append(line[2..]);
                }
            }
        }

        /// <summary>
        /// Resize the image with high quality to the specified width and height.
        /// </summary>
        /// <param name="image">The image to resize.</param>
        /// <param name="width">The width to resize to.</param>
        /// <param name="height">The height to resize to.</param>
        /// <returns>The resized image.</returns>
        public static Bitmap ResizeImage(Image image, int width, int height)
        {
            if (width <= 0 ||
                height <= 0 ||
                width > MaxThumbnailSize ||
                height > MaxThumbnailSize ||
                image == null)
            {
                return null;
            }

            Bitmap destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                graphics.Clear(Color.White);
                graphics.DrawImage(image, 0, 0, width, height);
            }

            return destImage;
        }

        /// <inheritdoc/>
        public void Initialize(IStream pstream, uint grfMode)
        {
            // Ignore the grfMode always use read mode to access the file.
            this.Stream = pstream;
        }

        /// <inheritdoc/>
        public void GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_UNKNOWN;

            if (cx == 0 || cx > MaxThumbnailSize)
            {
                return;
            }

            using (var stream = new ReadonlyStream(this.Stream as IStream))
            {
                using (var reader = new StreamReader(stream))
                {
                    using (Bitmap thumbnail = GetThumbnail(reader, cx))
                    {
                        if (thumbnail != null && thumbnail.Size.Width > 0 && thumbnail.Size.Height > 0)
                        {
                            phbmp = thumbnail.GetHbitmap();
                            pdwAlpha = WTS_ALPHATYPE.WTSAT_RGB;
                        }
                    }
                }
            }
        }
    }
}
