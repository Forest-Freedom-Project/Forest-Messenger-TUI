using System.Drawing;
using System.IO;
using QRCoder;

namespace ForestMSG.Core.Utils.QRCodes
{
    public class QRCodeCreator
    {
        public void SaveQRCodeToFile(string data, string path)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Default);
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeBytes = qrCode.GetGraphic(64);

                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, qrCodeBytes);
                }
            }
        }
    }
}