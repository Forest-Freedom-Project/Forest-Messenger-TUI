using System.Text;
using IronSoftware.Drawing;

namespace ForestMessengerTUI.ImageConverting
{
    public class ImageConverter
    {
        private const int DEFAULT_TERMINAL_WIDTH = 80;
        private const int DEFAULT_TERMINAL_HEIGHT = 40;
        private const int DEFAULT_COLOR_CLUSTERS = 16;

        public async Task<string> ConvertImageToAscii(
            AnyBitmap image,
            int maxWidth = DEFAULT_TERMINAL_WIDTH,
            int maxHeight = DEFAULT_TERMINAL_HEIGHT,
            int colorClusters = DEFAULT_COLOR_CLUSTERS,
            char darkChar = '█',
            char lightChar = '░',
            bool preserveAspectRatio = true,
            string paletteType = "standard",
            bool useColor = false)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            if (colorClusters <= 2)
            {
                return await ConvertToAsciiBinary(image, maxWidth, maxHeight, darkChar, lightChar, preserveAspectRatio);
            }

            return await ConvertToAsciiQuantized(image, maxWidth, maxHeight, colorClusters, preserveAspectRatio, paletteType, useColor);
        }

        private async Task<string> ConvertToAsciiQuantized(
            AnyBitmap image,
            int maxWidth,
            int maxHeight,
            int colorClusters,
            bool preserveAspectRatio,
            string paletteType,
            bool useColor)
        {
            int targetWidth, targetHeight;
            if (preserveAspectRatio)
            {
                (targetWidth, targetHeight) = CalculateScaledSize(image, maxWidth, maxHeight);
            }
            else
            {
                targetWidth = maxWidth;
                targetHeight = maxHeight;
            }

            var scaledImage = ScaleAnyBitmap(image, targetWidth, targetHeight);

            var quantizer = new DominantColorQuantizer(colorClusters);
            var (centroids, labels, clusterColors) = quantizer.QuantizeWithColors(scaledImage);

            int bgCluster = FindBackgroundCluster(scaledImage, labels, centroids);
            bool backgroundIsDark = CalculateLuminance(centroids[bgCluster]) < 128;

            var sorted = centroids
                .Select((c, i) => new
                {
                    Color = c,
                    Index = i,
                    Luminance = CalculateLuminance(c),
                    AvgColor = clusterColors[i]
                })
                .OrderBy(x => x.Luminance)
                .ToList();

            string palette = GetPalette(paletteType, colorClusters);

            if (!backgroundIsDark)
            {
                palette = new string(palette.Reverse().ToArray());
            }

            var charMap = new Dictionary<int, char>();
            var colorMap = new Dictionary<int, Color>();

            for (int i = 0; i < sorted.Count; i++)
            {
                charMap[sorted[i].Index] = palette[i % palette.Length];
                colorMap[sorted[i].Index] = sorted[i].AvgColor;
            }

            var sb = new StringBuilder();

            int pixelIndex = 0;
            int nonTransparentPixels = labels.Length;

            for (int y = 0; y < scaledImage.Height; y++)
            {
                for (int x = 0; x < scaledImage.Width; x++)
                {
                    var pixel = scaledImage.GetPixel(x, y);

                    if (pixel.A == 0)
                    {
                        sb.Append(' ');
                        continue;
                    }

                    if (pixelIndex < nonTransparentPixels)
                    {
                        int label = labels[pixelIndex];
                        char symbol = charMap.TryGetValue(label, out char c) ? c : '?';

                        if (useColor && colorMap.TryGetValue(label, out Color color))
                        {
                            sb.Append($"\x1b[38;2;{color.R};{color.G};{color.B}m{symbol}\x1b[0m");
                        }
                        else
                        {
                            sb.Append(symbol);
                        }
                        pixelIndex++;
                    }
                    else
                    {
                        sb.Append('?');
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private int FindBackgroundCluster(AnyBitmap image, int[] labels, Color[] centroids)
        {
            var edgeLabels = new List<int>();

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (x == 0 || x == image.Width - 1 || y == 0 || y == image.Height - 1)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.A != 0)
                        {
                            // Находим ближайший кластер
                            int label = FindNearestCluster(pixel, centroids);
                            edgeLabels.Add(label);
                        }
                    }
                }
            }

            if (edgeLabels.Count == 0)
                return 0;

            var frequency = new int[centroids.Length];
            foreach (int label in edgeLabels)
                frequency[label]++;

            int maxFreq = -1;
            int bgCluster = 0;
            for (int i = 0; i < frequency.Length; i++)
            {
                if (frequency[i] > maxFreq)
                {
                    maxFreq = frequency[i];
                    bgCluster = i;
                }
            }

            return bgCluster;
        }

        private int FindNearestCluster(Color pixel, Color[] centroids)
        {
            int nearest = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < centroids.Length; i++)
            {
                float dr = pixel.R - centroids[i].R;
                float dg = pixel.G - centroids[i].G;
                float db = pixel.B - centroids[i].B;
                float dist = dr * dr + dg * dg + db * db;

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        private double CalculateLuminance(Color color)
        {
            return 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        }

        private async Task<string> ConvertToAsciiBinary(
            AnyBitmap image,
            int maxWidth,
            int maxHeight,
            char darkChar,
            char lightChar,
            bool preserveAspectRatio)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            int targetWidth, targetHeight;
            if (preserveAspectRatio)
            {
                (targetWidth, targetHeight) = CalculateScaledSize(image, maxWidth, maxHeight);
            }
            else
            {
                targetWidth = maxWidth;
                targetHeight = maxHeight;
            }

            var scaledImage = ScaleAnyBitmap(image, targetWidth, targetHeight);
            byte[,] data = await ConvertImageToData(scaledImage, 128, 128, 128);
            return await ConvertToASCII(data, darkChar, lightChar);
        }

        private AnyBitmap ScaleAnyBitmap(AnyBitmap source, int targetWidth, int targetHeight)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentException("Target dimensions must be positive");

            if (source.Width == targetWidth && source.Height == targetHeight)
                return source;

            var result = new AnyBitmap(targetWidth, targetHeight);

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.For(0, targetHeight, options, y =>
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    float srcX = (float)x / targetWidth * source.Width;
                    float srcY = (float)y / targetHeight * source.Height;

                    int x0 = (int)Math.Floor(srcX);
                    int x1 = Math.Min(x0 + 1, source.Width - 1);
                    int y0 = (int)Math.Floor(srcY);
                    int y1 = Math.Min(y0 + 1, source.Height - 1);

                    float fx = srcX - x0;
                    float fy = srcY - y0;

                    var p00 = source.GetPixel(x0, y0);
                    var p10 = source.GetPixel(x1, y0);
                    var p01 = source.GetPixel(x0, y1);
                    var p11 = source.GetPixel(x1, y1);

                    byte r = (byte)(p00.R * (1 - fx) * (1 - fy) +
                                   p10.R * fx * (1 - fy) +
                                   p01.R * (1 - fx) * fy +
                                   p11.R * fx * fy);

                    byte g = (byte)(p00.G * (1 - fx) * (1 - fy) +
                                   p10.G * fx * (1 - fy) +
                                   p01.G * (1 - fx) * fy +
                                   p11.G * fx * fy);

                    byte b = (byte)(p00.B * (1 - fx) * (1 - fy) +
                                   p10.B * fx * (1 - fy) +
                                   p01.B * (1 - fx) * fy +
                                   p11.B * fx * fy);

                    byte a = (byte)(p00.A * (1 - fx) * (1 - fy) +
                                   p10.A * fx * (1 - fy) +
                                   p01.A * (1 - fx) * fy +
                                   p11.A * fx * fy);

                    result.SetPixel(x, y, new Color(r, g, b, a));
                }
            });

            return result;
        }

        public byte[,] ScaleData(byte[,] data, int targetWidth, int targetHeight)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int sourceWidth = data.GetLength(0);
            int sourceHeight = data.GetLength(1);

            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
                return data;

            if (targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentException("Target dimensions must be positive");

            var result = new byte[targetWidth, targetHeight];

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    float srcX = (float)x / targetWidth * sourceWidth;
                    float srcY = (float)y / targetHeight * sourceHeight;

                    int x0 = (int)Math.Floor(srcX);
                    int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                    int y0 = (int)Math.Floor(srcY);
                    int y1 = Math.Min(y0 + 1, sourceHeight - 1);

                    float fx = srcX - x0;
                    float fy = srcY - y0;

                    float val00 = data[x0, y0];
                    float val10 = data[x1, y0];
                    float val01 = data[x0, y1];
                    float val11 = data[x1, y1];

                    float interpolated = val00 * (1 - fx) * (1 - fy) +
                                        val10 * fx * (1 - fy) +
                                        val01 * (1 - fx) * fy +
                                        val11 * fx * fy;

                    result[x, y] = interpolated >= 0.5f ? (byte)1 : (byte)0;
                }
            }

            return result;
        }

        public byte[,] ScaleDataPreservingAspect(byte[,] data, int maxWidth, int maxHeight)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int sourceWidth = data.GetLength(0);
            int sourceHeight = data.GetLength(1);

            float aspectRatio = (float)sourceWidth / sourceHeight;

            int targetWidth = maxWidth;
            int targetHeight = (int)(maxWidth / aspectRatio);

            if (targetHeight > maxHeight)
            {
                targetHeight = maxHeight;
                targetWidth = (int)(maxHeight * aspectRatio);
            }

            targetWidth = Math.Max(1, targetWidth);
            targetHeight = Math.Max(1, targetHeight);

            return ScaleData(data, targetWidth, targetHeight);
        }

        public async Task<string> ConvertToASCII(byte[,] colorData, char darkChar = 'X', char lightChar = ' ')
        {
            if (colorData == null)
                throw new ArgumentNullException(nameof(colorData));

            int width = colorData.GetLength(0);
            int height = colorData.GetLength(1);

            var sb = new StringBuilder(width * height + height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    sb.Append(colorData[x, y] == 0 ? lightChar : darkChar);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public async Task<AnyBitmap> ConvertDataToImage(byte[,] colorData, Color foreground, Color background)
        {
            if (colorData == null)
                throw new ArgumentNullException(nameof(colorData));

            int width = colorData.GetLength(0);
            int height = colorData.GetLength(1);

            if (width == 0 || height == 0)
                throw new ArgumentException("Color data array cannot be empty", nameof(colorData));

            var result = new AnyBitmap(width, height);

            int blockSize = 100;
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.For(0, (height + blockSize - 1) / blockSize, options, blockIndex =>
            {
                int yStart = blockIndex * blockSize;
                int yEnd = Math.Min(yStart + blockSize, height);

                for (int y = yStart; y < yEnd; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        result.SetPixel(x, y, colorData[x, y] == 0 ? foreground : background);
                    }
                }
            });

            return result;
        }

        public async Task<byte[,]> ConvertImageToData(AnyBitmap image, short rBrightness, short gBrightness, short bBrightness)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            byte rThreshold = (byte)Math.Clamp(rBrightness, (short)0, (short)255);
            byte gThreshold = (byte)Math.Clamp(gBrightness, (short)0, (short)255);
            byte bThreshold = (byte)Math.Clamp(bBrightness, (short)0, (short)255);

            var pixelColorData = new byte[image.Width, image.Height];

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            Parallel.For(0, image.Height, options, y =>
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);

                    if (pixel.A != 0)
                    {
                        bool isBright = pixel.R >= rThreshold ||
                                       pixel.G >= gThreshold ||
                                       pixel.B >= bThreshold;

                        pixelColorData[x, y] = isBright ? (byte)1 : (byte)0;
                    }
                }
            });

            return pixelColorData;
        }

        private string GetPalette(string type, int count)
        {
            var palettes = new Dictionary<string, string>
            {
                { "standard", " .:-=+*#%@" },
                { "detailed", " .'`^\",:;Il!i><~+_-?][}{1)(|\\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$" },
                { "blocks", " ░▒▓█" },
                { "gradient", "▀▄▌▐█"},
                { "binary", " █" },
                { "shades", " ▁▂▃▄▅▆▇█" },
                { "numbers", " 123456789" },
                { "letters", " abcdefghijklmnopqrstuvwxyz" }
            };

            if (!palettes.TryGetValue(type, out string palette))
                palette = palettes["standard"];

            if (palette.Length < count)
            {
                var sb = new StringBuilder();
                while (sb.Length < count)
                {
                    sb.Append(palette);
                }
                return sb.ToString();
            }

            return palette;
        }

        private (int width, int height) CalculateScaledSize(AnyBitmap image, int maxWidth, int maxHeight)
        {
            float aspectRatio = (float)image.Width / image.Height;

            int width = maxWidth;
            int height = (int)(maxWidth / aspectRatio);

            if (height > maxHeight)
            {
                height = maxHeight;
                width = (int)(maxHeight * aspectRatio);
            }

            width = Math.Max(1, width);
            height = Math.Max(1, height);

            return (width, height);
        }

        public (int width, int height) GetTerminalSize()
        {
            try
            {
                int width = Console.WindowWidth;
                int height = Console.WindowHeight;
                return (width - 1, height - 2);
            }
            catch
            {
                return (DEFAULT_TERMINAL_WIDTH, DEFAULT_TERMINAL_HEIGHT);
            }
        }

        public void AnalyzeCompression(byte[,] original, byte[,] scaled)
        {
            int origWidth = original.GetLength(0);
            int origHeight = original.GetLength(1);
            int scaledWidth = scaled.GetLength(0);
            int scaledHeight = scaled.GetLength(1);

            long origPixels = origWidth * origHeight;
            long scaledPixels = scaledWidth * scaledHeight;
            float compressionRatio = (float)origPixels / scaledPixels;

            Console.WriteLine($"Оригинал: {origWidth}x{origHeight} ({origPixels} пикселей)");
            Console.WriteLine($"Сжато: {scaledWidth}x{scaledHeight} ({scaledPixels} пикселей)");
            Console.WriteLine($"Степень сжатия: {compressionRatio:F2}x");
            Console.WriteLine($"Потеря информации: {(1 - 1 / compressionRatio) * 100:F2}%");
        }
    }

    public class DominantColorQuantizer
    {
        private readonly int _maxColors;
        private readonly int _maxIterations;
        private readonly Random _random;

        public DominantColorQuantizer(int maxColors = 16, int maxIterations = 10)
        {
            _maxColors = maxColors;
            _maxIterations = maxIterations;
            _random = new Random();
        }

        public (Color[] centroids, int[] labels, Color[] clusterColors) QuantizeWithColors(AnyBitmap image)
        {
            var pixels = new List<ColorPoint>();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.A != 0)
                    {
                        pixels.Add(new ColorPoint(pixel.R, pixel.G, pixel.B));
                    }
                }
            }

            if (pixels.Count == 0)
                throw new InvalidOperationException("No visible pixels in image");

            int actualClusters = Math.Min(_maxColors, Math.Max(1, pixels.Count / 100));
            actualClusters = Math.Max(2, actualClusters);

            var centroids = InitializeCentroids(pixels, actualClusters);
            var labels = new int[pixels.Count];

            for (int iter = 0; iter < _maxIterations; iter++)
            {
                for (int i = 0; i < pixels.Count; i++)
                {
                    labels[i] = FindNearestCentroid(pixels[i], centroids);
                }

                var newCentroids = UpdateCentroids(pixels, labels, actualClusters);

                if (HasConverged(centroids, newCentroids))
                    break;

                centroids = newCentroids;
            }

            var clusterColors = ComputeClusterColors(pixels, labels, actualClusters);

            var frequency = new int[actualClusters];
            for (int i = 0; i < labels.Length; i++)
                frequency[labels[i]]++;

            var sortedClusters = Enumerable.Range(0, actualClusters)
                .OrderByDescending(i => frequency[i])
                .ToList();

            var colorCentroids = new Color[actualClusters];
            var finalColors = new Color[actualClusters];
            var labelMap = new int[actualClusters];

            for (int i = 0; i < actualClusters; i++)
            {
                int originalIndex = sortedClusters[i];
                labelMap[originalIndex] = i;
                colorCentroids[i] = new Color(
                    (byte)Math.Clamp(centroids[originalIndex].R, 0, 255),
                    (byte)Math.Clamp(centroids[originalIndex].G, 0, 255),
                    (byte)Math.Clamp(centroids[originalIndex].B, 0, 255)
                );
                finalColors[i] = clusterColors[originalIndex];
            }

            var newLabels = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                newLabels[i] = labelMap[labels[i]];

            return (colorCentroids, newLabels, finalColors);
        }

        private Color[] ComputeClusterColors(List<ColorPoint> pixels, int[] labels, int clusterCount)
        {
            var sums = new (float R, float G, float B, int Count)[clusterCount];

            for (int i = 0; i < pixels.Count; i++)
            {
                int label = labels[i];
                sums[label].R += pixels[i].R;
                sums[label].G += pixels[i].G;
                sums[label].B += pixels[i].B;
                sums[label].Count++;
            }

            var colors = new Color[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                if (sums[i].Count > 0)
                {
                    colors[i] = new Color(
                        (byte)Math.Clamp(sums[i].R / sums[i].Count, 0, 255),
                        (byte)Math.Clamp(sums[i].G / sums[i].Count, 0, 255),
                        (byte)Math.Clamp(sums[i].B / sums[i].Count, 0, 255)
                    );
                }
                else
                {
                    colors[i] = new Color(0, 0, 0);
                }
            }

            return colors;
        }

        private ColorPoint[] InitializeCentroids(List<ColorPoint> pixels, int clusterCount)
        {
            var centroids = new ColorPoint[clusterCount];
            centroids[0] = pixels[_random.Next(pixels.Count)];

            for (int i = 1; i < clusterCount; i++)
            {
                float maxDist = -1;
                int selectedIndex = 0;

                for (int j = 0; j < pixels.Count; j++)
                {
                    float minDist = float.MaxValue;
                    for (int k = 0; k < i; k++)
                    {
                        float dist = ColorPoint.Distance(pixels[j], centroids[k]);
                        if (dist < minDist)
                            minDist = dist;
                    }

                    if (minDist > maxDist)
                    {
                        maxDist = minDist;
                        selectedIndex = j;
                    }
                }

                centroids[i] = pixels[selectedIndex];
            }

            return centroids;
        }

        private int FindNearestCentroid(ColorPoint pixel, ColorPoint[] centroids)
        {
            int nearest = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < centroids.Length; i++)
            {
                float dist = ColorPoint.Distance(pixel, centroids[i]);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        private ColorPoint[] UpdateCentroids(List<ColorPoint> pixels, int[] labels, int clusterCount)
        {
            var sums = new (float R, float G, float B, int Count)[clusterCount];

            for (int i = 0; i < pixels.Count; i++)
            {
                int label = labels[i];
                sums[label].R += pixels[i].R;
                sums[label].G += pixels[i].G;
                sums[label].B += pixels[i].B;
                sums[label].Count++;
            }

            var centroids = new ColorPoint[clusterCount];
            for (int i = 0; i < clusterCount; i++)
            {
                if (sums[i].Count > 0)
                {
                    centroids[i] = new ColorPoint(
                        sums[i].R / sums[i].Count,
                        sums[i].G / sums[i].Count,
                        sums[i].B / sums[i].Count
                    );
                }
                else
                {
                    centroids[i] = new ColorPoint(0, 0, 0);
                }
            }

            return centroids;
        }

        private bool HasConverged(ColorPoint[] old, ColorPoint[] newCentroids)
        {
            float threshold = 0.001f;
            for (int i = 0; i < old.Length; i++)
            {
                if (ColorPoint.Distance(old[i], newCentroids[i]) > threshold)
                    return false;
            }
            return true;
        }

        private class ColorPoint
        {
            public float R { get; set; }
            public float G { get; set; }
            public float B { get; set; }

            public ColorPoint(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
            }

            public static float Distance(ColorPoint a, ColorPoint b)
            {
                float dr = a.R - b.R;
                float dg = a.G - b.G;
                float db = a.B - b.B;
                return (float)Math.Sqrt(dr * dr + dg * dg + db * db);
            }
        }
    }
}