using System.Security.Cryptography;

namespace SmartX.Api.Services
{
    /// <summary>
    /// Encrypts uploaded attachments before they touch the disk and decrypts them again on download.
    /// Uses one AES-256 key generated at startup, so stored files are only readable for the lifetime of the API process.
    /// </summary>
    public sealed class FileEncryptionService
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);



        /// <summary>
        /// Encrypts the incoming stream with AES and writes it to the destination path.
        /// The generated IV is written to the front of the file so the same file can be decrypted later, and the size on disk is returned.
        /// </summary>
        public async Task<long> EncryptToFileAsync(Stream source, string destinationPath, CancellationToken ct = default)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            await using var destination = File.Create(destinationPath);
            await destination.WriteAsync(aes.IV, ct);

            await using var cryptoStream = new CryptoStream(destination, aes.CreateEncryptor(), CryptoStreamMode.Write);
            await source.CopyToAsync(cryptoStream, ct);
            await cryptoStream.FlushFinalBlockAsync(ct);

            return new FileInfo(destinationPath).Length;
        }
        //----------------------------------------------------------//



        /// <summary>
        /// Reads the IV off the front of the stored file and uses it to decrypt the rest.
        /// Returns the plain content as a rewound MemoryStream, ready to hand straight back to the client.
        /// </summary>
        public async Task<Stream> DecryptFromFileAsync(string sourcePath, CancellationToken ct = default)
        {
            var source = File.OpenRead(sourcePath);

            var iv = new byte[16];
            await source.ReadExactlyAsync(iv, ct);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;

            var output = new MemoryStream();

            await using (var cryptoStream = new CryptoStream(source, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true))
            {
                await cryptoStream.CopyToAsync(output, ct);
            }

            source.Dispose();
            output.Position = 0;
            return output;
        }
        //----------------------------------------------------------//
    }
}

//------------------------------------------- End Of File -------------------------------------------//